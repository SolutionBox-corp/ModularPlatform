# Oblast 30 — Model management, comparison & cost optimization

> **Rozsah oblasti.** Backend pro správu LLM modelů, jejich srovnání a optimalizaci nákladů v modulu **HybridRag**. Tato oblast definuje **jediný chokepoint pro VŠECHNA model volání** (`ILlmGateway`), per-request usage capture, append-only usage ledger, per-tenant/user/feature cost atribuci, hard budgety (429), 3-vrstvou cache, config-driven pricing, 2-tier routing + confidence cascade, multi-provider fallback chains, A/B + shadow testing + champion/challenger, cost-optimization doporučení a model registry.
>
> **Klíčové invarianty oblasti (frozen):**
> - `ILlmGateway` je **jediný** vstupní bod pro chat/embed/rerank volání kdekoli v modulu (oblasti 03 embed, 05 dense, 08 rerank, 13 answer, 21 streaming, 15 MCP, 18 eval — všechny volají JEN přes tento port). Mirror `IStripeGateway` (provider port, fake-under-flag `Rag:UseFakeGateways`).
> - Identita VŽDY z tokenu `ITenantContext.UserId` / `TenantId` — NIKDY z body ani z LLM výstupu (Law 10).
> - Usage ledger = **append-only** (jako credit ledger, oblast Billing `BillingLedgerTests`), RLS-isolated (`IUserOwned` → `Guid UserId`; `ITenantScoped`).
> - **Real-time budget enforcement = Redis token/cost bucket (oblast 22), NE lagging rollup (oblast 19).** Rollup je pro reporting; bucket je pro hard stop.
> - Vše UTC (`IClock.UtcNow`), EF/LINQ only, errors = `ModularPlatformException` subclass + errorCode v `SharedResource.resx` (en+cs).
> - Pricing, routing tiers, A/B váhy, shadow % a cache prahy = **config** (`Rag:Models:*`, registr v oblasti 24, override přes `RagSetting`). Nový model = config edit + registry řádek, **žádná změna kódu**.
> - OTel GenAI semconv spany + `PlatformMetrics.Meter` (`platform.hybridrag.*`) na každém model volání (oblast 19).
>
> **Entita `RagModel`** (model registry): `Id`, `Name`, `Provider` (anthropic|openai|cohere|voyage|fake…), `Kind` (chat|embed|rerank), `PricingInputPerMTok`, `PricingOutputPerMTok`, `PricingCacheCreatePerMTok`, `PricingCacheReadPerMTok`, `EmbedDimensions?`, `Status` (active|shadow|deprecated|disabled), `IsDefault`, `Tier` (cheap|frontier), `CreatedUtc`, `UpdatedUtc`, `xmin`. Tabulka `hybridrag_models`.
> **Entita `LlmUsageEntry`** (usage ledger): `Id`, `TenantId`, `UserId`, `Feature` (string: answer|embed|rerank|extract|eval|judge…), `ModelName`, `Provider`, `PromptTokens`, `CompletionTokens`, `CacheCreationTokens`, `CacheReadTokens`, `CostUsd` (decimal), `CacheLayerHit` (none|exact|semantic|provider_prefix), `RequestId` (idempotency), `ConversationId?`, `TurnId?`, `CreatedUtc`. Tabulka `hybridrag_llm_usage_entries`, append-only, `IUserOwned`+`ITenantScoped`.

---

## UC-30-01 — `ILlmGateway` jako jediný chokepoint pro všechna model volání

- **Actor / role:** Interní (každý handler modulu, který potřebuje LLM: embed oblast 03, dense oblast 05, rerank oblast 08, answer oblast 13, judge oblast 18, MCP oblast 15). · **Precondition:** modul registruje `ILlmGateway` v `RegisterServices` (real adapter `LlmGateway`, nebo `FakeLlmGateway` pod `Rag:UseFakeGateways=true`). · **Trigger:** libovolný handler volá `gateway.ChatAsync(...)`, `gateway.EmbedAsync(...)` nebo `gateway.RerankAsync(...)`. · **Main flow:**
  1. Handler injektuje `ILlmGateway` (NIKDY přímo Anthropic/OpenAI/Cohere SDK — to je ArchUnitNET porušení, viz EC-30-01-02).
  2. Gateway vyřeší cílový model přes **routing** (UC-30-09) → jméno modelu + provider z registry (`RagModel`).
  3. Gateway zkontroluje **cache** (UC-30-08) — exact → semantic → provider prefix; cache hit → vrátí cached odpověď + zaznamená `CacheLayerHit`, přeskočí provider volání.
  4. Cache miss → gateway zkontroluje **budget bucket** (UC-30-05, Redis oblast 22). Nad limit → `BusinessRuleException` `rag.budget_exceeded` + 429.
  5. Gateway zavolá real provider adapter (s `cache_creation`/`cache_read` prompt-cache headers, oblast 14) přes retry/fallback chain (UC-30-10).
  6. Po odpovědi: **usage capture** (UC-30-02) — 4 token countery × per-model pricing → `CostUsd`; zápis do usage ledgeru (UC-30-03); inkrement Redis bucket (UC-30-05); OTel GenAI span + metriky (oblast 19); naplnění cache vrstev.
  7. Vrátí odpověď handleru. Veškerá cross-cutting logika (routing, fallback, cache, usage, budget, telemetrie) je **na jednom místě** — handler nic z toho neřeší.
- **Postcondition / záruky:** každé model volání v modulu prošlo jedním seamem; usage je vždy zachyceno; žádné volání neobejde budget/telemetrie. · **Tenancy / permissions:** gateway čte `ITenantContext` pro tenant/user tagy ledgeru a budget klíče — interní, žádný endpoint. · **Reuse / canonical pattern:** provider port fake-under-flag = `IStripeGateway` + `MarketingModule.cs:51`; chat/agent struktura = `ClaudeVibeAgentGateway.cs:85`. · **Data dotčena:** čte `hybridrag_models`, zapisuje `hybridrag_llm_usage_entries`, Redis bucket (oblast 22), cache store. · **Eventy:** žádné (synchronní gateway); rollup agregace přes outbox event volitelná (oblast 19). · **Priorita:** P0

### Edge cases UC-30-01
- **EC-30-01-01 — Gateway volán mimo request scope (worker/job bez HttpContext)** · Trigger: ingest saga (oblast 04) embeduje chunky v Worker hostu, kde není HttpContext. · Očekávané chování: `ITenantContext` = `SystemTenantContext`/`HttpTenantContext` fallback (CLAUDE.md tenant isolation); usage ledger dostane skutečný `TenantId`/`UserId` z message payloadu (propagovaný z původního requestu), NE „system". · Mechanismus: tenant/user na ingest message (oblast 04), gateway čte z explicitně předaného kontextu, ne z ambient claim. +Law 10. · Severity: P0 · Test: integration — embed v workeru zapíše ledger s tenantem z původního uploadu, ne null.
- **EC-30-01-02 — Handler obejde gateway a volá SDK přímo** · Trigger: vývojář injektuje `Anthropic.Client` přímo v answer handleru. · Očekávané chování: build fail — ArchUnitNET rule „žádný typ v HybridRag.Core nereferencuje provider SDK přímo, jen `ILlmGateway`". · Mechanismus: ArchitectureTests boundary rule (analogie §3 reuse-first). · Severity: P1 · Test: ArchUnitNET — porušení = červená.
- **EC-30-01-03 — `Rag:UseFakeGateways=true` ale registry odkazuje real provider** · Trigger: test harness s fake flag, ale `RagModel.Provider=anthropic`. · Očekávané chování: `FakeLlmGateway` ignoruje provider a vrací deterministické fake odpovědi/embeddingy/rerank skóre podle `Kind`; pricing se počítá normálně (z registry) aby cost testy fungovaly. · Mechanismus: fake-under-flag jako `FakeStripeGateway`. · Severity: P1 · Test: harness — fake gateway nevolá síť, cost > 0 dle pricing tabulky.

---

## UC-30-02 — Per-request usage capture (4 token countery × pricing → USD)

- **Actor / role:** Interní (gateway, po každém provider volání). · **Precondition:** provider vrátil usage blok (`input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens` — Anthropic semantika); `RagModel` má kompletní pricing. · **Trigger:** dokončené (ne-streamované) provider volání nebo streamovaný `message_delta` s usage (UC-30-06). · **Main flow:**
  1. Gateway extrahuje **4 countery** z provider response: `PromptTokens` (input), `CompletionTokens` (output), `CacheCreationTokens`, `CacheReadTokens`. (U embed/rerank providerů, které dávají jen `total_tokens`, se mapuje na `PromptTokens`, ostatní 0.)
  2. Načte per-model **price table** z registry (`PricingInputPerMTok`, `PricingOutputPerMTok`, `PricingCacheCreatePerMTok`, `PricingCacheReadPerMTok`).
  3. Spočítá `CostUsd = (prompt·inRate + completion·outRate + cacheCreate·createRate + cacheRead·readRate) / 1_000_000`, decimal aritmetika (žádný float — peníze).
  4. Naplní `LlmUsageEntry` (tenant/user/feature/model/4 countery/USD/cache layer/requestId).
  5. Předá do usage ledgeru (UC-30-03) a do Redis budget bucketu (UC-30-05).
- **Postcondition / záruky:** každý request má deterministickou USD cenu odvozenou z registry, ne hardcoded; cache-read tokeny jsou účtovány levnější sazbou (typicky 0.1× input). · **Tenancy / permissions:** interní. · **Reuse / canonical pattern:** decimal money aritmetika = Billing ledger (`available = posted − pending` princip); registry = UC-30-12. · **Data dotčena:** čte `hybridrag_models`, vytváří `LlmUsageEntry`. · **Eventy:** žádné synchronní; volitelný `LlmUsageRecordedIntegrationEvent` do rollup oblasti 19. · **Priorita:** P0

### Edge cases UC-30-02
- **EC-30-02-01 — Pricing chybí pro model (nový model bez config sazeb)** · Trigger: routing vybral model, který v registry nemá vyplněné `Pricing*`. · Očekávané chování: gateway odmítne volání PŘED provider callem → `BusinessRuleException` `rag.model_pricing_missing` (ne 429, ne 500 po volání) — fail-fast, aby se neúčtovalo neznámou cenou ani nepustila netagovaná cost. Alternativa dle config `Rag:Models:RejectOnMissingPricing` (default true); pokud false → účtuje `CostUsd=null` + WARN + `platform.hybridrag.cost_untagged` counter. · Mechanismus: validace v gateway před voláním; fail-fast pattern (CLAUDE.md §8 secrets fail-fast analogie). · Severity: P0 · Test: integration — model bez pricing → 422/business rule, žádné provider volání.
- **EC-30-02-02 — Provider nevrátí cache countery (starší API / jiný provider)** · Trigger: OpenAI embed bez cache semantiky. · Očekávané chování: `CacheCreationTokens=0`, `CacheReadTokens=0`, cost počítán jen z prompt/completion; žádná NRE. · Mechanismus: defensivní mapování adapteru, default 0. · Severity: P2 · Test: unit — usage mapper s chybějícími poli.
- **EC-30-02-03 — Token counter přeteče / je negativní** · Trigger: poškozený provider payload. · Očekávané chování: countery validovány `>= 0`; negativní → odmítnuto, WARN, request fail (ne tichý zápis záporné ceny do ledgeru). · Mechanismus: guard v usage mapperu. · Severity: P2 · Test: unit — negativní input → reject.

---

## UC-30-03 — Append-only usage ledger (per LLM volání)

- **Actor / role:** Interní (gateway). · **Precondition:** usage capture (UC-30-02) hotov; `LlmUsageEntry` naplněn. · **Trigger:** dokončené model volání (po odpovědi / po stream reconciliation). · **Main flow:**
  1. Gateway zapíše `LlmUsageEntry` přes scoped write context (NE `ExecuteUpdate` — chceme audit + xmin; ledger je append-only, jeden INSERT per volání).
  2. `RequestId` je UNIQUE (idempotency key) — druhý zápis stejného requestu (retry, fallback re-entry) je catch `DbUpdateException` → no-op (UC-30-03 idempotence, EC-30-03-01).
  3. `TenantStampingInterceptor` stampne `TenantId`; RLS `app.principal_id` GUC zajistí, že čtení ledgeru je per-user isolated (`IUserOwned`).
  4. Zápis je součástí téže transakce jako business write handleru tam, kde to dává smysl (answer handler ukládá `RagTurn` + usage entry atomicky); jinde samostatný commit.
- **Postcondition / záruky:** ledger je nepřepisatelný zdroj pravdy o nákladech; součet `CostUsd` per tenant/user/feature = autoritativní účtenka (lagging — pro reporting, ne pro real-time stop). · **Tenancy / permissions:** RLS per-user; tenant-scoped; admin cross-tenant read přes `PlatformPermissions.RagCostRead` (UC-30-04). · **Reuse / canonical pattern:** append-only ledger = Billing `credit_entries` (UNIQUE idempotency_key, catch `DbUpdateException`); outbox write = `RegisterUserHandler.cs:22`. · **Data dotčena:** `hybridrag_llm_usage_entries`. · **Eventy:** volitelný `LlmUsageRecordedIntegrationEvent` (rollup). · **Priorita:** P0

### Edge cases UC-30-03
- **EC-30-03-01 — Dvojí zápis usage (retry / fallback re-entry / at-least-once worker)** · Trigger: gateway retry po transient chybě, nebo Worker re-deliver embed message. · Očekávané chování: UNIQUE `RequestId` → druhý INSERT vyhodí `DbUpdateException` → catch → no-op (cena se NEzapočítá dvakrát, budget bucket se NEinkrementuje dvakrát). · Mechanismus: UNIQUE key + catch `DbUpdateException` (CLAUDE.md idempotency law); inbox dedup pro worker cesty. · Severity: P0 · Test: integration — 2× stejný `RequestId` → 1 řádek, 1× cost.
- **EC-30-03-02 — Cost atribuce chybí tenant tag** · Trigger: gateway volán s nevyřešeným `TenantId` (background bez propagace). · Očekávané chování: zápis s `TenantId=null` je ZAKÁZÁN pro tenant-scoped entitu — buď systémový sentinel tenant (s explicitním `Feature=system`), nebo reject; WARN + `platform.hybridrag.cost_untagged`. Nikdy tiše netagovaný náklad, který nelze vyúčtovat. · Mechanismus: `TenantStampingInterceptor` + non-null guard; EC-30-01-01. · Severity: P0 · Test: integration — chybějící tenant → reject/sentinel, ne null.
- **EC-30-03-03 — Ledger zápis selže po úspěšném provider volání** · Trigger: DB výpadek po tom, co provider už účtoval. · Očekávané chování: business write se rollbackne, ale provider volání proběhlo (peníze utraceny u providera) → usage zápis je zařazen do outboxu/durable retry, aby se náklad neztratil; reconcile (oblast 19/UC-30 nemá vlastní, používá rollup) dohledá. · Mechanismus: usage zápis přes outbox kde je samostatný; jinak accept ztrátu reportingu (provider je zdroj pravdy pro fakturaci providerem). · Severity: P1 · Test: integration — DB fail po provider call → usage v dead-letter/retry, ne tiché ztracení.

---

## UC-30-04 — Per-tenant / user / feature cost atribuce (reporting query)

- **Actor / role:** Tenant admin / platform admin. · **Precondition:** existují `LlmUsageEntry` řádky. · **Trigger:** `GET /v1/hybridrag/cost?from=&to=&groupBy=feature|user|model`. · **Main flow:**
  1. Endpoint mapuje request → `GetCostBreakdownQuery` (read-only, `IReadDbContextFactory`).
  2. Handler agreguje `SUM(CostUsd)`, `SUM(PromptTokens)`, … přes ledger, GROUP BY zvolené dimenze, filtrováno časovým oknem (UTC).
  3. RLS + tenant filter zajistí, že tenant admin vidí jen svůj tenant; user vidí jen sebe (bez `RagCostRead`).
  4. Vrací `ApiResponse<CostBreakdownResponse>` (řádky: dimenze, cost, tokeny, cache hit rate per vrstva, počet volání).
- **Postcondition / záruky:** read-only, žádná mutace; čísla odpovídají ledgeru (lagging, ne real-time). · **Tenancy / permissions:** vlastní data bez permission; cross-user v tenantu = `PlatformPermissions.RagCostRead`; cross-tenant = platform-admin (oblast 16/23). · **Reuse / canonical pattern:** read query = `GetProfileHandler.cs:12` (`IReadDbContextFactory`); paging = platform `Paged`/`totalCount`. · **Data dotčena:** čte `hybridrag_llm_usage_entries`. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-30-04
- **EC-30-04-01 — User chce cizí breakdown bez permission** · Trigger: user volá `groupBy=user` přes celý tenant. · Očekávané chování: bez `RagCostRead` query vrátí jen vlastní `UserId` (RLS to vynutí na DB úrovni i kdyby handler zapomněl filtr). · Mechanismus: Postgres RLS `IUserOwned` (defence-in-depth, CLAUDE.md). · Severity: P1 · Test: integration — user A nevidí náklady usera B.
- **EC-30-04-02 — Velké časové okno → pomalá agregace** · Trigger: `from` 2 roky zpět. · Očekávané chování: query má max window cap (config `Rag:Cost:MaxBreakdownDays`) nebo nutí pre-agregovaný rollup (oblast 19) pro dlouhá okna; jinak indexovaný `(tenant_id, created_utc)`. · Mechanismus: index + cap; rollup tabulka pro reporting. · Severity: P2 · Test: perf — okno nad cap → 422 nebo přepnutí na rollup.

---

## UC-30-05 — Hard per-tenant token/cost budgety → 429 (real-time enforce)

- **Actor / role:** Tenant (nepřímo přes každé LLM volání). · **Precondition:** tenant má nastavený budget (`Rag:Budgets:*` config, override `RagSetting`, vázaný na Billing entitlement — UC propojení). · **Trigger:** gateway před každým provider voláním (UC-30-01 krok 4). · **Main flow:**
  1. Gateway před voláním inkrementuje **Redis token/cost bucket** (oblast 22) atomicky (`INCRBY` na klíči `rag:budget:{tenantId}:{window}`), s předpokládaným cost odhadem (input tokeny + max output) — rezervace.
  2. Pokud by hodnota překročila `BudgetLimitUsd` / `BudgetLimitTokens` pro okno (denní/měsíční) → gateway **odmítne** volání: `BusinessRuleException` `rag.budget_exceeded`, HTTP 429 + `Retry-After` (do konce okna).
  3. Po skutečném usage capture (UC-30-02) gateway **rekonciliuje** rezervaci na skutečnou cenu (`INCRBY` o rozdíl; u cache hitů refund).
  4. Limit je **real-time** (Redis bucket), NE lagging rollup (oblast 19) — protože rollup zaostává a tenant by mohl přečerpat o celé okno.
  5. Budget tie do Billing: hard limit = entitlement plan (`Rag:Budgets` odvozeno z tenant plánu); překročení může nabídnout top-up přes Billing credits (cross-module event, ne JOIN).
- **Postcondition / záruky:** tenant nikdy nepřečerpá hard limit o víc než jedno in-flight volání; po vyčerpání všechna LLM volání 429 do resetu okna. · **Tenancy / permissions:** per-tenant bucket; klíč MUSÍ nést `tenantId` (jinak cross-tenant leak, EC-30-09-04/EC-30-05-03). · **Reuse / canonical pattern:** Redis bucket = oblast 22 (rate-limit); 429+Retry-After = request-edge hardening (CLAUDE.md); entitlement = Billing/tenant_entitlements (oblast 16). · **Data dotčena:** Redis bucket; čte `RagSetting`/config; nepřímo Billing entitlement. · **Eventy:** `RagBudgetExceededIntegrationEvent` (volitelně → Notifications alert tenant adminovi). · **Priorita:** P0

### Edge cases UC-30-05
- **EC-30-05-01 — Budget exceeded → správná 429 + Retry-After** · Trigger: tenant vyčerpá denní budget. · Očekávané chování: `BusinessRuleException` `rag.budget_exceeded` → `GlobalExceptionMiddleware` → 429 (ne 400/403) + `Retry-After` = sekundy do půlnoci UTC (resp. reset okna); i18n message en+cs. · Mechanismus: errorCode → status mapping; oblast 17 degradace může vrátit retrieval-only odpověď bez LLM. · Severity: P0 · Test: integration — vyčerpaný bucket → 429 + Retry-After header.
- **EC-30-05-02 — Redis nedostupný (budget bucket down)** · Trigger: Redis výpadek. · Očekávané chování: **fail-closed vs fail-open je config rozhodnutí** (`Rag:Budgets:FailMode`, default fail-open s WARN + degraded counter — neblokovat produkci kvůli Redis, ale logovat, že budget není vynucen; vysoce-citliví tenanti mohou fail-closed). NIKDY tiše bez logu. · Mechanismus: oblast 22 Redis fallback; explicitní config. · Severity: P1 · Test: integration — Redis down → dle FailMode (open=projde+WARN, closed=429).
- **EC-30-05-03 — Cross-tenant budget klíč kolize** · Trigger: bucket klíč nenese `tenantId`. · Očekávané chování: klíč je VŽDY `rag:budget:{tenantId}:{userId?}:{window}` — jeden tenant nesmí čerpat z budgetu druhého. · Mechanismus: klíč composition s tenant, oblast 16 isolation. · Severity: P0 · Test: integration — 2 tenanti, vyčerpání A neovlivní B.
- **EC-30-05-04 — Rezervace přestřelí, in-flight volání selže** · Trigger: gateway rezervuje max-output odhad, ale volání spadne/cache hit → skutečná cena nižší. · Očekávané chování: reconciliation (krok 3) vrátí přebytek do bucketu; selhané volání refundne celou rezervaci. · Mechanismus: rezervace + reconcile (jako Billing pending→confirm). · Severity: P1 · Test: integration — cache hit po rezervaci → bucket refund.

---

## UC-30-06 — Streaming usage reconciliation (`include_usage`, post-disconnect)

- **Actor / role:** Interní (gateway při streamovaném chatu, oblast 21 SSE). · **Precondition:** answer běží streamovaně (`gateway.ChatStreamAsync`), provider posílá `include_usage`/`message_delta` s průběžným/finálním usage. · **Trigger:** streamované volání. · **Main flow:**
  1. Gateway akumuluje delty (text → SSE klientovi přes `IRealtimePublisher`/oblast 21) a **paralelně** sbírá usage z `message_start` (input tokeny) + `message_delta` (output tokeny, cumulative).
  2. Po `message_stop` (nebo po disconnektu klienta) gateway **přečte finální usage** z posledního delta a zapíše ledger (UC-30-03) **až poté** — usage capture se NEvolá na začátku.
  3. Multi-chunk akumulace: output tokeny se **akumulují**, NEPŘEPISUJÍ (poslední delta nese kumulativní/finální, ale gateway drží i mezisoučty pro případ, že stream skončí předčasně).
  4. **Disconnect klienta NEZASTAVÍ** server-side čtení streamu — gateway dočte usage z provideru (provider účtuje i tak), zapíše ledger, inkrementuje bucket. Jinak by se přestal počítat náklad za odpovědi, které klient „odpojil".
- **Postcondition / záruky:** usage je zachyceno i když klient odpadne; output tokeny nikdy nejsou ztracené ani dvojnásobné; ledger má kompletní cenu streamu. · **Tenancy / permissions:** interní; tenant z původního requestu. · **Reuse / canonical pattern:** SSE = oblast 21 + `MapRealtimeStream`; disconnect-safe save = vibe streaming (`ClaudeVibeAgentGateway`, MEMORY „disconnect-safe save"). · **Data dotčena:** `hybridrag_llm_usage_entries`; Redis bucket. · **Eventy:** žádné. · **Priorita:** P0

### Edge cases UC-30-06
- **EC-30-06-01 — Klient se odpojí v půlce streamu** · Trigger: browser zavře SSE. · Očekávané chování: server dočte provider stream do konce (nebo do provider close), zapíše finální usage; náklad NEzmizí. Cancellation token klienta NEpropaguje do provider čtení usage. · Mechanismus: oddělený lifetime klient-SSE vs provider-stream; disconnect-safe. · Severity: P0 · Test: integration — abort SSE v půlce → ledger má plnou cenu.
- **EC-30-06-02 — Usage delta přijde víckrát / přepis** · Trigger: provider pošle kumulativní output v každém delta. · Očekávané chování: gateway bere finální/max hodnotu, NEsčítá kumulativní delty (jinak double-count). Pro providery s inkrementálními deltami sčítá. Adapter zná semantiku providera. · Mechanismus: per-provider usage accumulation strategie v adapteru. · Severity: P1 · Test: unit — kumulativní deltas → 1× finální cost, ne suma.
- **EC-30-06-03 — Stream skončí chybou před `message_stop`** · Trigger: provider 500 v půlce. · Očekávané chování: gateway zapíše usage z toho, co dorazilo (input + partial output), označí turn jako partial (oblast 17 degradace), fallback na další model NEpokračuje stejný stream (nový request, nový `RequestId`). · Mechanismus: partial usage capture + oblast 10 fallback. · Severity: P1 · Test: integration — stream error → partial usage zapsáno.

---

## UC-30-07 — 3-vrstvá cache: exact → semantic → provider prefix-cache

- **Actor / role:** Interní (gateway před provider voláním). · **Precondition:** cache zapnutá (`Rag:Cache:Enabled`), Redis dostupný (vrstva exact/semantic). · **Trigger:** chat/embed volání. · **Main flow:**
  1. **Vrstva 1 — exact:** hash normalizovaného promptu (+model+params) → Redis lookup `rag:cache:exact:{tenantId}:{hash}`. Hit → vrátí cached odpověď, `CacheLayerHit=exact`, žádný provider call, žádné nové completion tokeny (jen logging).
  2. **Vrstva 2 — semantic:** embedding promptu (přes gateway embed) → nearest-neighbor v semantic cache store (pgvector/Redis vektor) v rámci tenanta; pokud `CosineSimilarity >= Rag:Cache:SemanticThreshold` → hit, `CacheLayerHit=semantic`.
  3. **Vrstva 3 — provider prefix-cache:** miss na 1+2 → volání jde providerovi s prompt-cache headers (oblast 14, Anthropic `cache_control`) → `cache_read_input_tokens > 0` ⇒ `CacheLayerHit=provider_prefix` (levnější input).
  4. Po provider odpovědi se naplní vrstvy 1 a 2 (s TTL `Rag:Cache:TtlMinutes`, MAXLEN/eviction).
  5. **Hit-rate per vrstva** se měří (`platform.hybridrag.cache_hit{layer}` counter, oblast 19) pro cost reporting (UC-30-04 vrací cache hit rate).
- **Postcondition / záruky:** opakované/podobné dotazy neplatí plnou cenu; cache klíč je **tenant-scoped** (žádný cross-tenant leak); semantic hit jen nad bezpečným prahem. · **Tenancy / permissions:** každý cache klíč nese `tenantId` (UC-30-08 EC poisoning); per-user volitelně dle citlivosti dat. · **Reuse / canonical pattern:** semantic = pgvector HNSW CosineDistance (oblast 05 dense); prefix-cache = oblast 14; Redis = oblast 22. · **Data dotčena:** Redis cache, semantic vektor store; čte registry pro embed model. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-30-07
- **EC-30-07-01 — Semantic cache false-hit (příliš nízký práh vrátí špatnou odpověď)** · Trigger: `SemanticThreshold` nastaven nízko (např. 0.80), podobný-ale-jiný dotaz dostane cizí odpověď. · Očekávané chování: default práh konzervativní (≥0.95–0.97 cosine), config fail-fast na nízkou hodnotu mimo Dev; semantic cache se aplikuje JEN na dotazy bez čerstvosti/personalizace (freshness oblast 09 invaliduje); pro RAG answer s citacemi je semantic cache defaultně OFF (citace závisí na čerstvém retrievalu). · Mechanismus: vysoký práh + scope omezení + freshness invalidace (oblast 09). · Severity: P0 · Test: integration — dva sémanticky blízké ale fakticky odlišné dotazy nesdílí odpověď při default prahu.
- **EC-30-07-02 — Cache poisoning per tenant (klíč nenese tenant)** · Trigger: cache klíč jen z prompt hashe bez `tenantId`. · Očekávané chování: klíč VŽDY `{tenantId}` prefixed; tenant A nikdy nedostane odpověď naplněnou tenantem B (jejich korpusy se liší → odpověď by leakla data). · Mechanismus: tenant-scoped klíč, oblast 16 isolation. · Severity: P0 · Test: integration — stejný prompt, 2 tenanti, 2 různé odpovědi (žádné sdílení).
- **EC-30-07-03 — Cache vrací stale odpověď po re-ingestu dokumentů** · Trigger: korpus se změnil (oblast 01/09), ale exact/semantic cache drží starou odpověď. · Očekávané chování: cache klíč zahrnuje **corpus version / freshness token** (oblast 09) nebo TTL je krátký; re-ingest invaliduje tenant cache namespace. · Mechanismus: freshness oblast 09 + cache versioning. · Severity: P1 · Test: integration — re-ingest → cache miss na předchozí dotaz.
- **EC-30-07-04 — Redis down → cache vrstvy 1/2 nedostupné** · Trigger: Redis výpadek. · Očekávané chování: graceful degrade — přeskočí exact/semantic, jde rovnou na provider (prefix-cache stále funguje), WARN; žádný fail. · Mechanismus: oblast 22 Redis fallback. · Severity: P2 · Test: integration — Redis down → volání projde bez cache vrstev 1/2.

---

## UC-30-08 — Per-model pricing jako config (nový model = config edit)

- **Actor / role:** Platform admin / DevOps. · **Precondition:** `PlatformPermissions.RagModelManage` (pro DB registry) nebo config deploy. · **Trigger:** přidání/úprava modelu — buď `Rag:Models:Pricing:*` config, nebo `POST/PUT /v1/hybridrag/admin/models` (UC-30-12). · **Main flow:**
  1. Pricing pro každý model = 4 sazby (`input`, `output`, `cache_create`, `cache_read` per MTok) uložené v `RagModel` (DB registry, autoritativní) s config seedem/overridem (`Rag:Models`, oblast 24, override `RagSetting`).
  2. Nový provider model: přidá se registry řádek + pricing, status `active`. **Žádná změna kódu** — gateway čte sazby z registry za běhu.
  3. Změna ceny providerem: update řádku → nové volání používá novou cenu; **historické ledger řádky se NEPŘEPOČÍTÁVAJÍ** (cena byla zaznamenána v čase volání — append-only).
- **Postcondition / záruky:** cost capture vždy odráží aktuální pricing; historie je imutabilní. · **Tenancy / permissions:** registry edit = platform-admin (`RagModelManage`), audit přes `hybridrag_audit_entries`. · **Reuse / canonical pattern:** config registr = oblast 24; admin write = vertical slice `Features/.../RegisterUser/*`; audit = `AuditInterceptor`. · **Data dotčena:** `hybridrag_models`. · **Eventy:** `RagModelUpdatedIntegrationEvent` (volitelně, invaliduje routing/pricing cache). · **Priorita:** P1

### Edge cases UC-30-08
- **EC-30-08-01 — Pricing chybí pro nový model** · Viz EC-30-02-01 — fail-fast před voláním, ne tichý null cost. · Severity: P0 · Test: viz EC-30-02-01.
- **EC-30-08-02 — Záporná / nulová sazba** · Trigger: překlep v config (`output=-5`). · Očekávané chování: `RagModelValidator` (FluentValidation `.WithErrorCode("rag.model_pricing_invalid")`) odmítne; config validator fail-fast na startu. · Mechanismus: validátor (CLAUDE.md ValidationBehavior); options validator. · Severity: P1 · Test: unit — záporná sazba → validation error.
- **EC-30-08-03 — Pricing override z `RagSetting` koliduje s config** · Trigger: DB `RagSetting` i appsettings nastaví různou cenu. · Očekávané chování: jasná precedence — `RagSetting` (DB, per-tenant/runtime) > appsettings > default (oblast 24 registr definuje pořadí). · Mechanismus: oblast 24 config precedence. · Severity: P2 · Test: unit — DB override vyhrává.

---

## UC-30-09 — Model routing 2-tier (levný default → frontier) + confidence cascade

- **Actor / role:** Interní (gateway, routing krok). · **Precondition:** registry má aspoň `cheap` default a `frontier` model pro `Kind=chat`; routing config `Rag:Routing:*`. · **Trigger:** chat volání z answer handleru (oblast 13). · **Main flow:**
  1. **Tier 1 (default):** routing pošle dotaz na **levný** model (`Tier=cheap`, `IsDefault`), pokud query klasifikace neoznačí jako „hard".
  2. **Klasifikace hard:** lehký klasifikátor (heuristika délky/složitosti, nebo levný model jako classifier) + signály z retrievalu (oblast 05/07/17 — nízké RRF skóre, málo relevantních chunků = poor retrieval) → „hard" → rovnou frontier.
  3. **Confidence cascade:** levný model odpoví; pokud odpověď signalizuje **low-confidence** (model self-report, nebo nízká retrieval-grounding, nebo guard heuristika) → **eskalace** na frontier model, druhé volání. Obě volání se účtují (UC-30-02) — eskalace má cost overhead, sledováno (`platform.hybridrag.routing_escalation` counter).
  4. Routing rozhodnutí + finální model se zaznamená do `RagTrace` (oblast 13) a usage ledgeru (`Feature`, `ModelName`).
- **Postcondition / záruky:** většina dotazů jde levně; jen hard/low-confidence platí frontier; kvalita se nezhorší pod práh (cascade chytí slabou odpověď). · **Tenancy / permissions:** routing může být per-tenant/feature override (UC-30-11 A/B). · **Reuse / canonical pattern:** config-driven = oblast 24; trace = oblast 13; retrieval signály = oblasti 07/17. · **Data dotčena:** čte registry + routing config; zapisuje trace + usage. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-30-09
- **EC-30-09-01 — Routing pošle hard query na levný model (kvalita drop, cascade ji nechytí)** · Trigger: klasifikátor podcení složitost, cascade confidence signál je slabý → uživatel dostane horší odpověď. · Očekávané chování: cascade má **více nezávislých signálů** (self-report + retrieval grounding + answer-length/refusal heuristika); konzervativní default (raději eskalovat při pochybnosti); online eval (oblast 18) měří kvalitu per tier a alertuje na drop → ladění klasifikátoru. Citlivé feature (`Feature` flag) mohou mít „always frontier". · Mechanismus: multi-signal cascade + oblast 18 eval feedback + per-feature override. · Severity: P1 · Test: golden set (oblast 18) — hard dotazy na cheap-tier nesmí spadnout pod quality práh; jinak fail.
- **EC-30-09-02 — Cascade eskaluje VŽDY (levný model vždy low-confidence)** · Trigger: confidence práh moc přísný → každý dotaz jde 2× (cheap+frontier) = dražší než kdyby šel rovnou frontier. · Očekávané chování: monitoring eskalační rate; pokud `escalation_rate > Rag:Routing:MaxEscalationRate` → alert „cascade neefektivní" + doporučení (UC-30-11) přehodit default na frontier nebo upravit práh. · Mechanismus: `routing_escalation` counter + oblast 19 alert. · Severity: P2 · Test: integration — vysoká eskalace → WARN/metrika.
- **EC-30-09-03 — Levný i frontier model nedostupný** · Trigger: oba tiers down. · Očekávané chování: fallback chain (UC-30-10) → další provider; pokud žádný → oblast 17 degradace (retrieval-only odpověď, `RetrievalStatus=degraded`), ne 500. · Mechanismus: fallback + degradace. · Severity: P1 · Test: integration — vše down → degradovaná odpověď.

---

## UC-30-10 — Multi-provider fallback chains (rate-limit / outage → next)

- **Actor / role:** Interní (gateway). · **Precondition:** registry/config definuje fallback chain per `Kind` (`Rag:Fallback:Chat = [claude-primary, claude-secondary, openai-fallback]`). · **Trigger:** provider volání vrátí retryable chybu (429 rate-limit, 5xx, timeout, outage). · **Main flow:**
  1. Gateway zabalí provider volání do retry policy (Polly — battle-tested, CLAUDE.md §6) s exponential backoff + jitter pro transient.
  2. Po vyčerpání retry na primárním modelu (nebo na hard 429 s dlouhým `Retry-After`) → **fallback** na další model v chainu (jiný model/provider).
  3. Každý pokus má **vlastní `RequestId`** odvozený (base + attempt) → usage ledger nezdvojí (UC-30-03 idempotence); účtuje se jen úspěšný + případně failnuté pokusy s partial usage.
  4. Chain má **max hloubku** (config) → po vyčerpání → oblast 17 degradace nebo `BusinessRuleException` `rag.all_providers_unavailable`.
  5. Respektuje provider `Retry-After` header (nečeká déle než config max).
- **Postcondition / záruky:** dočasný výpadek/limit jednoho providera nezpůsobí selhání requestu, pokud existuje fallback; žádná nekonečná smyčka. · **Tenancy / permissions:** interní. · **Reuse / canonical pattern:** retry/backoff = Polly (CLAUDE.md battle-tested); messaging retry/DLQ analogie (oblast 22); degradace = oblast 17. · **Data dotčena:** usage ledger (per attempt), metriky fallback (`platform.hybridrag.provider_fallback{from,to}`). · **Eventy:** žádné; alert na opakovaný fallback (oblast 19). · **Priorita:** P0

### Edge cases UC-30-10
- **EC-30-10-01 — Fallback loop (chain cyklí / nekonečné retry)** · Trigger: špatná config s cyklem nebo retry bez cap. · Očekávané chování: chain je acyklický seznam s max hloubkou; každý model zkoušen max jednou v jednom requestu; total attempt budget (config `Rag:Fallback:MaxAttempts`) zastaví. · Mechanismus: bounded chain + attempt counter. · Severity: P0 · Test: integration — všechny modely 503 → skončí po MaxAttempts, ne nekonečně.
- **EC-30-10-02 — Provider 429 s `Retry-After`** · Trigger: Anthropic 429. · Očekávané chování: gateway čte provider `Retry-After`, pokud > config práh → okamžitý fallback na další model (nečeká); jinak krátký backoff a retry; promítne do tenant budget bucketu (ne refund, volání neproběhlo). · Mechanismus: respekt `Retry-After` + práh; oblast 22. · Severity: P1 · Test: integration — provider 429+Retry-After → fallback.
- **EC-30-10-03 — Fallback na model s jinou kvalitou/cenou bez vědomí** · Trigger: fallback z frontier na výrazně levnější/horší. · Očekávané chování: fallback zaznamená do trace `fallback_used=true` + `degraded_quality` flag; usage ledger má skutečný model (ne původně routovaný); answer může nést metadata, že běžel fallback. · Mechanismus: trace oblast 13 + transparentní model atribuce. · Severity: P2 · Test: integration — fallback → trace+ledger nese náhradní model.
- **EC-30-10-04 — Idempotence při fallbacku (dvojí účtování)** · Trigger: retry zapíše usage, pak fallback uspěje a zapíše znovu. · Očekávané chování: každý fyzický provider pokus = unikátní `RequestId`; jen pokusy, které reálně utratily tokeny, se zapíšou; žádné duplicitní započtení stejného pokusu (UC-30-03 EC-30-03-01). · Mechanismus: UNIQUE `RequestId` per attempt. · Severity: P0 · Test: integration — fallback řetěz → ledger řádky = počet reálných pokusů, ne víc.

---

## UC-30-11 — Model A/B + swap config-driven per tenant/feature

- **Actor / role:** Platform admin / tenant admin (dle scope). · **Precondition:** registry má ≥2 kompatibilní modely pro daný `Kind`/`Feature`; A/B config (`Rag:Experiments:*` nebo `RagSetting`). · **Trigger:** admin nastaví A/B split / swap defaultu pro feature/tenant. · **Main flow:**
  1. Routing (UC-30-09) čte per-tenant/feature **model assignment** z config/`RagSetting` (`{feature: answer, tenant: X} → modelB`). **Swap = config edit, žádná změna kódu, žádný redeploy.**
  2. **A/B split:** deterministické přiřazení requestu do varianty podle hash(`{tenantId|userId|conversationId}`) → stabilní bucket (stejný uživatel/konverzace = stejná varianta, ne flapping), poměr dle config (`A=80%, B=20%`).
  3. Varianta se zaznamená do usage ledgeru (`ModelName`) + trace + experiment tag → srovnání cost+kvalita per varianta (oblast 18 online eval).
  4. Po vyhodnocení admin „flipne" default (swap) — opět jen config.
- **Postcondition / záruky:** model lze měnit per tenant/feature bez deploye; A/B přiřazení je stabilní a měřitelné; swap je auditovaný. · **Tenancy / permissions:** platform-admin pro globální, tenant-admin pro vlastní tenant (`RagModelManage`); audit. · **Reuse / canonical pattern:** config = oblast 24; routing = UC-30-09; eval srovnání = oblast 18; deterministický bucket = stabilní hash (jako feature flags backlog). · **Data dotčena:** čte config/`RagSetting`; zapisuje usage+trace s experiment tagem. · **Eventy:** `RagModelSwappedIntegrationEvent` (audit/cache invalidace). · **Priorita:** P2

### Edge cases UC-30-11
- **EC-30-11-01 — Model swap bez re-embed (embedding drift)** · Trigger: admin swapne **embed** model (jiný dimenze/prostor) bez re-ingestu korpusu → dotazové embeddingy z modelu B v indexu z modelu A = nesmyslná podobnost. · Očekávané chování: swap embed modelu je **blokován**, pokud `EmbedDimensions` nesouhlasí s indexem, nebo vyžaduje explicitní re-embed migraci (oblast 03/09): `BusinessRuleException` `rag.embed_model_dimension_mismatch`. Dotaz a index MUSÍ být ze stejného embed modelu; swap embed modelu = re-ingest celého korpusu (oblast 01/03) nebo dual-index migrace. · Mechanismus: dimenze guard + oblast 03 re-embed + oblast 09 freshness; `halfvec(3072)` sloupec je fixní dimenze. · Severity: P0 · Test: integration — swap embed na jinou dimenzi bez re-embed → blokováno; chat model swap → OK.
- **EC-30-11-02 — A/B přiřazení flapuje uprostřed konverzace** · Trigger: bucket podle requestu, ne podle konverzace. · Očekávané chování: bucket klíč = `conversationId` (resp. `userId`) → celá konverzace běží na jedné variantě (konzistence kontextu/prompt-cache). · Mechanismus: stabilní hash na conversation scope. · Severity: P1 · Test: integration — 5 turnů jedné konverzace = stejná varianta.
- **EC-30-11-03 — Config swap na neexistující/disabled model** · Trigger: admin nastaví model, který v registry chybí nebo má `Status=disabled`. · Očekávané chování: validace odmítne swap (`rag.model_not_available`); routing nikdy nepošle na disabled model (fallback na default). · Mechanismus: validátor + registry status check. · Severity: P1 · Test: integration — swap na disabled → reject.

---

## UC-30-12 — Model registry (RagModel CRUD + status lifecycle)

- **Actor / role:** Platform admin. · **Precondition:** `PlatformPermissions.RagModelManage`. · **Trigger:** `GET/POST/PUT/PATCH /v1/hybridrag/admin/models`. · **Main flow:**
  1. **List/Get:** read query přes `IReadDbContextFactory` — vrací modely (name, provider, kind, pricing, status, tier, isDefault, embedDimensions).
  2. **Create:** `RegisterRagModelCommand` → validuje (pricing kompletní, dimenze pro embed, unikátní name) → insert `RagModel`, status `active` nebo `shadow`.
  3. **Update:** pricing/status/tier/isDefault změna; mutace tracked entity (xmin + `ConcurrencyRetryBehavior`); audit přes `AuditInterceptor`.
  4. **Status lifecycle:** `active` ↔ `shadow` (UC-30-13) ↔ `deprecated` (nedostává nový traffic, jen doběh) ↔ `disabled` (žádný traffic). `IsDefault` může mít jen jeden model per `Kind`/scope (guard).
- **Postcondition / záruky:** registry je zdroj pravdy pro routing/pricing/fallback; každá změna auditovaná; nový model přidatelný bez deploye. · **Tenancy / permissions:** platform-admin only; registry je platform-scoped (ne per-tenant data, ale per-tenant assignment je UC-30-11). · **Reuse / canonical pattern:** write slice = `RegisterUser/*`; read = `GetProfileHandler.cs:12`; entity+config = `User.cs` pattern; audit = `AuditInterceptor`→`hybridrag_audit_entries`. · **Data dotčena:** `hybridrag_models`. · **Eventy:** `RagModelUpdatedIntegrationEvent` (cache invalidace routing/pricing). · **Priorita:** P1

### Edge cases UC-30-12
- **EC-30-12-01 — Dva modely `IsDefault=true` pro stejný Kind** · Trigger: admin omylem nastaví dva default chat modely. · Očekávané chování: guard — nastavení nového defaultu atomicky shodí předchozí (`ExecuteUpdate` na `IsDefault=false WHERE kind=X`), nebo validace odmítne druhý default. Routing musí mít jednoznačný default. · Mechanismus: atomic guard / validátor. · Severity: P1 · Test: integration — set default B → A přestane být default.
- **EC-30-12-02 — Smazání modelu, na který odkazuje config/aktivní assignment** · Trigger: admin smaže model použitý v A/B nebo fallback chainu. · Očekávané chování: soft-disable místo hard-delete (`Status=disabled`), nebo block s `rag.model_in_use` dokud se neodstraní reference; historický ledger drží `ModelName` jako string (ne FK) → historie přežije. · Mechanismus: soft-delete + reference guard; ledger string ne FK. · Severity: P1 · Test: integration — disable použitého modelu → routing fallback, ledger historie čitelná.
- **EC-30-12-03 — Embed model bez `EmbedDimensions`** · Trigger: create embed modelu bez dimenze. · Očekávané chování: validátor vyžaduje `EmbedDimensions` pro `Kind=embed` (a kontrolu vs `halfvec(3072)` sloupec); jinak `rag.embed_dimensions_required`. · Mechanismus: validátor + EC-30-11-01. · Severity: P1 · Test: unit — embed bez dimenze → reject.

---

## UC-30-13 — Shadow testing kandidáta na 10 % trafficu (bez vlivu na produkci)

- **Actor / role:** Platform admin (nastaví shadow), pak interní (gateway). · **Precondition:** kandidát model v registry `Status=shadow`, shadow config (`Rag:Shadow:{candidate}:Percent=10`, `Feature` scope). · **Trigger:** produkční request padne do shadow vzorku (10 % dle deterministického hashe). · **Main flow:**
  1. Produkční (champion) volání proběhne normálně a **jeho odpověď se vrací uživateli** — shadow NIKDY neovlivní produkční odpověď.
  2. Pro vzorkovaný request gateway **paralelně/asynchronně** zavolá shadow kandidáta se stejným promptem (přes outbox/worker, oblast 04 — ne v request path, aby nezdržoval).
  3. Shadow odpověď + usage se zapíše do ledgeru s tagem `Feature=shadow:{candidate}` a uloží pro srovnání; **NEvrací se klientovi**, NEpíše do produkční konverzace/citací.
  4. LLM-judge (UC-30-14, oblast 18) porovná champion vs shadow kvalitu + cost offline.
- **Postcondition / záruky:** kandidát se testuje na reálném trafficu bez rizika pro UX; shadow náklad je viditelný a omezený (10 %); žádný leak shadow výstupu do produkce. · **Tenancy / permissions:** platform-admin; shadow usage účtováno (cost transparency), může mít vlastní budget. · **Reuse / canonical pattern:** async work = outbox/worker `ProvisionCreditAccountHandler.cs:13` + 202 vzor; deterministický vzorek = UC-30-11 bucket. · **Data dotčena:** `hybridrag_llm_usage_entries` (shadow tag), shadow result store pro srovnání. · **Eventy:** `RagShadowSampleCapturedIntegrationEvent` (→ eval oblast 18). · **Priorita:** P2

### Edge cases UC-30-13
- **EC-30-13-01 — Shadow eval leak do produkce** · Trigger: bug, kdy shadow odpověď nahradí champion v response/konverzaci/citacích. · Očekávané chování: shadow běží mimo request path (worker), výsledek má separátní store + tag; produkční answer handler NIKDY nečte shadow store; ArchUnitNET/test guard. · Mechanismus: oddělená cesta + tag isolation; shadow je read-only pro eval. · Severity: P0 · Test: integration — shadow zapnut → uživatelská odpověď vždy z champion, citace z champion retrievalu.
- **EC-30-13-02 — Shadow zdvojí náklady nad rozpočet** · Trigger: shadow 10 % ale kandidát je drahý frontier → nečekaný cost spike. · Očekávané chování: shadow má **vlastní budget bucket** (UC-30-05) + cap; překročení vypne shadow (ne produkci); alert. · Mechanismus: separátní shadow budget + auto-disable. · Severity: P1 · Test: integration — shadow překročí budget → shadow off, produkce běží.
- **EC-30-13-03 — Shadow volání selže/timeout** · Trigger: kandidát down. · Očekávané chování: shadow chyba je swallowed (jen WARN + metrika), NIKDY neovlivní produkční request; worker retry/DLQ dle messaging resilience. · Mechanismus: shadow je best-effort, izolovaný od champion. · Severity: P2 · Test: integration — shadow timeout → produkce nedotčena.

---

## UC-30-14 — LLM-judge kvalita + cost srovnání PŘED flipnutím (champion/challenger)

- **Actor / role:** Platform admin (čte srovnání + rozhodne flip). · **Precondition:** běží A/B (UC-30-11) nebo shadow (UC-30-13); judge model v registry; golden/online dataset (oblast 18). · **Trigger:** `GET /v1/hybridrag/admin/model-comparison?challenger=&champion=&window=` nebo periodický job. · **Main flow:**
  1. Sesbírá párované odpovědi champion vs challenger (z A/B nebo shadow store) za okno.
  2. **LLM-judge** (přes `ILlmGateway`, `Feature=judge`) hodnotí kvalitu páru (grounding, relevance, citační přesnost — oblast 18 metriky) → skóre per varianta.
  3. Spojí s **cost** z usage ledgeru (UC-30-04): průměrný cost/request per varianta.
  4. Vrací srovnání: kvalita (judge win-rate, eval metriky) × cost × latence → doporučení (UC-30-15): „challenger = N % kvality championa za M % ceny".
  5. Admin rozhodne **flip** (swap default, UC-30-11) — rozhodnutí je manuální (gate), ne automatické.
- **Postcondition / záruky:** flip je informované rozhodnutí podložené kvalitou i cenou na reálných datech; judge náklad účtován. · **Tenancy / permissions:** platform-admin (`RagModelManage`); judge volání tagováno. · **Reuse / canonical pattern:** judge = `ILlmGateway` + oblast 18 eval; chat = `ClaudeVibeAgentGateway.cs:85`; read = `GetProfileHandler.cs:12`. · **Data dotčena:** čte usage ledger + eval/shadow store; zapisuje judge usage. · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-30-14
- **EC-30-14-01 — Judge bias / nekonzistence** · Trigger: judge favorizuje delší odpovědi nebo vlastní rodinu modelů. · Očekávané chování: judge prompt je neutrální, position-swapped (A/B pořadí randomizováno proti position bias), volitelně více judge modelů/runs s agregací; metriky doplněny o deterministické (citační recall, grounding) z oblasti 18, ne jen judge. · Mechanismus: oblast 18 hybrid eval (deterministické + judge); position swap. · Severity: P1 · Test: eval harness — swap pořadí nemění verdikt systematicky.
- **EC-30-14-02 — Nedostatek párovaných vzorků pro signifikanci** · Trigger: malý traffic, judge srovnání na 5 vzorcích. · Očekávané chování: srovnání vrací confidence/sample size, neflipuje na malém vzorku; doporučení (UC-30-15) potlačeno pod min sample (config). · Mechanismus: min sample gate. · Severity: P2 · Test: unit — pod min sample → „insufficient data".

---

## UC-30-15 — Cost-optimization doporučení („model B = 90 % kvality za 30 % ceny")

- **Actor / role:** Platform / tenant admin. · **Precondition:** existují srovnání (UC-30-14) + usage data (UC-30-04). · **Trigger:** `GET /v1/hybridrag/admin/cost-recommendations` nebo periodický job (oblast 19). · **Main flow:**
  1. Job/query analyzuje usage ledger + comparison + cache hit-rates + routing escalation rate.
  2. Generuje konkrétní doporučení: „přehoď default `answer` z frontier na challenger B (90 % kvality, 30 % ceny)", „zvyš semantic cache práh — 40 % false-hit riziko", „snížit eskalační rate cascade (UC-30-09-02)", „model X má 0 % cache hit — zkontroluj prefix-cache".
  3. Vrací seřazená doporučení s odhadem úspory ($/měsíc) + dopadem na kvalitu.
  4. Doporučení jsou **read-only návrhy** — žádný auto-apply (admin gate, jako UC-30-14 flip).
- **Postcondition / záruky:** admin má actionable cost insights podložené daty; žádná automatická změna produkce. · **Tenancy / permissions:** platform-admin (globální) / tenant-admin (vlastní); `RagCostRead`. · **Reuse / canonical pattern:** read+agregace = `GetProfileHandler.cs:12`; periodický job = `BillingExpireCreditsJob` (Quartz, oblast Jobs); metriky = `PlatformMetrics.Meter`. · **Data dotčena:** čte ledger + comparison + registry. · **Eventy:** volitelný `RagCostRecommendationGeneratedIntegrationEvent` → Notifications (alert admina). · **Priorita:** P3

### Edge cases UC-30-15
- **EC-30-15-01 — Doporučení na základě zastaralých srovnání** · Trigger: comparison data stará (model/pricing se mezitím změnil). · Očekávané chování: doporučení nese timestamp zdrojových dat + varování při staleness nad práh; nepoužije srovnání starší než config okno. · Mechanismus: freshness check na zdrojích. · Severity: P2 · Test: unit — stale comparison → doporučení označeno stale/suppressed.
- **EC-30-15-02 — Doporučení optimalizuje cenu na úkor kvality pod práh** · Trigger: „90 % kvality" je pod akceptovatelnou hranicí feature. · Očekávané chování: doporučení respektuje per-feature minimální kvalitu (`Rag:Quality:MinAcceptable`); pod práh = nedoporučí swap, jen ho zmíní s varováním. · Mechanismus: quality floor v doporučovacím algoritmu. · Severity: P2 · Test: unit — challenger pod quality floor → nedoporučeno.

---

## UC-30-16 — Per-feature / per-tenant usage rollup pro reporting (lagging agregace)

- **Actor / role:** Interní (job) + admin (čte). · **Precondition:** ledger plní se (UC-30-03). · **Trigger:** periodický rollup job (Quartz, `Rag:Cost:RollupCron`) nebo on-demand. · **Main flow:**
  1. Rollup job agreguje `hybridrag_llm_usage_entries` do denních/měsíčních sum per (`tenant`, `user`, `feature`, `model`) → `hybridrag_usage_rollup` (rychlé dotazy pro UC-30-04/15 na dlouhá okna).
  2. Rollup je **idempotentní** (re-run pro okno přepočítá deterministicky, UNIQUE per dimenze+okno).
  3. **Rollup je LAGGING** — slouží reportingu, NE real-time enforce (to je Redis bucket UC-30-05). Explicitně oddělené, aby se budget nevynucoval ze zaostávajícího součtu.
- **Postcondition / záruky:** rychlý reporting bez skenování celého ledgeru; rollup nikdy není zdroj pro hard stop. · **Tenancy / permissions:** job běží jako system; čtení dle UC-30-04. · **Reuse / canonical pattern:** Quartz job = `BillingExpireCreditsJob`; oblast 19 observ; idempotentní rollup = UNIQUE key. · **Data dotčena:** čte `hybridrag_llm_usage_entries`, zapisuje `hybridrag_usage_rollup`. · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-30-16
- **EC-30-16-01 — Rollup běží 2× pro stejné okno (double agregace)** · Trigger: job re-fire / overlap. · Očekávané chování: idempotentní upsert per (dimenze, okno) → druhý běh přepočítá, nezdvojí; nebo UNIQUE + recompute. · Mechanismus: idempotentní rollup (UNIQUE key). · Severity: P1 · Test: integration — 2× rollup → stejné sumy.
- **EC-30-16-02 — Pozdní ledger zápis po rollupu okna** · Trigger: durable usage zápis (EC-30-03-03) dorazí po uzávěrce okna. · Očekávané chování: rollup okna se přepočítá při dalším běhu (sliding re-aggregate posledních N oken), nebo late entry zařazena do aktuálního okna s flagem; reporting se dorovná. · Mechanismus: sliding re-aggregation window. · Severity: P2 · Test: integration — pozdní entry → příští rollup ho zahrne.
