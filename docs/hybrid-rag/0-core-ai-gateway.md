# Oblast CORE — LLM/AI gateway (core building-block `ModularPlatform.Ai`)

> ⛔ **PREREKVIZITA — postavit PŘED prací na HybridRag modulu.** Toto NENÍ modul, ale **core building-block** (`src/building-blocks/ModularPlatform.Ai`). Důvod (CONVENTIONS §17): LLM volá ≥2 moduly (Marketing už `IVibeAgentGateway`, RAG přidá) → patří do core; cost tracking + budgety musí být platform-wide; „jediný chokepoint pro všechna model volání" funguje jen v core. Oblast 30 (RAG model-comparison) tento building-block KONZUMUJE.

---

## Úvodní poznámky k záměru building-blocku

`ModularPlatform.Ai` zavádí JEDINÝ technický seam mezi platformou a poskytovateli LLM. Žádná feature, žádný modul, žádný handler nesmí instancovat `AnthropicClient`, `OpenAIClient` ani volat `IChatClient` mimo tento building-block. Smysl: na jednom místě se odehrává routing, fallback, cache, usage capture, cost atribuce, budget enforcement, PII redakce a observabilita. Tím vzniká auditovatelný „chokepoint" pro veškerá model-volání platformy a odstraňuje se třída chyb „modul X loguje cost jinak než modul Y" nebo „modul Z obešel budget".

Building-block stojí na `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator<string, Embedding<float>>`) jako neutrální abstrakci, pod ní `Anthropic.SDK` pro chat a `OpenAI` pro embeddingy. Provider seam následuje frozen pattern `IStripeGateway` (real adapter + `Fake*` pod konfiguračním flagem `Ai:UseFakeGateways`, výhradně pro test harness a CI).

Klíčová OTEVŘENÁ sub-decision (viz UC-CORE-17): zda `ModularPlatform.Ai` zůstane **čistý building-block s vlastní perzistencí** (`AiDbContext`, migrace přes building-block) nebo se z něj stane **tenký always-on platform modul `ModularPlatform.Ai`** (protože nese entity `AiUsageLedger`/`AiModel` + admin endpointy, což nahrává modulové variantě). Tuto volbu NEIMPROVIZOVAT — eskalovat uživateli (Law 11).

---

## UC-CORE-01 — `ILlmGateway` port jako jediný chokepoint pro chat-model volání
- **Actor / role:** consuming-module (Marketing vibe agent, budoucí RAG generation), nepřímo přes dispatcher/handler
- **Precondition:** building-block zaregistrován v DI (`AddPlatformAi`), aspoň jeden chat model `Enabled` v `AiModel` registry, provider klíč v Options (nebo `Ai:UseFakeGateways=true`) · **Trigger:** interní volání `ILlmGateway.CompleteAsync(LlmRequest, ct)` z handleru spotřebovávajícího modulu
- **Main flow:**
  1. Volající sestaví `LlmRequest` (zprávy, system prompt, `feature` tag, `modelHint?`, `responseFormat?`, `cachePolicy?`, `maxTokens`, `temperature`).
  2. Gateway resolvne efektivní `tenant_id`/`user_id` z `ITenantContext` (NIKDY z requestu/LLM výstupu — Law 10).
  3. Gateway projde pipeline: **budget pre-check** (UC-CORE-06) → **cache lookup** 3 vrstvy (UC-CORE-07) → **routing** výběr modelu (UC-CORE-09) → provider call přes MEAI `IChatClient` → **usage capture** (UC-CORE-03) → **ledger zápis** (UC-CORE-04) → **OTel span + metrics** (UC-CORE-15) → cache store.
  4. Vrátí `LlmResponse` (text/structured, `Usage`, `model`, `cacheLayerHit`, `costUsd`, `latencyMs`, `finishReason`).
- **Postcondition / záruky:** každé úspěšné chat-volání má právě jeden řádek v `ai_usage_ledger`, právě jeden GenAI span, započtený cost; žádná cesta k provideru neexistuje mimo gateway · **Tenancy / permissions:** Scope = volající identita z tokenu; ledger zápis `ITenantScoped`+`IUserOwned` (RLS); žádný permission gate (interní port) · **Reuse / canonical pattern:** provider seam dle `Billing/.../Stripe/IStripeGateway.cs` (real+fake pod flagem); MEAI `IChatClient` · **Data dotčena:** `ai_usage_ledger` (insert), `ai_models` (read) · **Eventy:** žádný (synchronní in-proc); volitelně `AiCallCompletedIntegrationEvent` pouze pokud RAG/Marketing potřebuje async reakci — nepřidávat spekulativně · **Priorita:** P0

### Edge cases UC-CORE-01
- **EC-CORE-01-01 — Modul obejde gateway a volá provider přímo** · Trigger: handler instancuje `AnthropicClient` napřímo · Očekávané chování: zakázáno — ArchUnitNET boundary rule blokuje referenci na `Anthropic.SDK`/`OpenAI` z modulů; jediná povolená cesta je `ILlmGateway` · Mechanismus: architektonický test + `internal` provider adaptéry uvnitř building-blocku (Law 4 REUSE-FIRST) · Severity: P0 · Test: ArchUnitNET assert „no module references Anthropic.SDK except ModularPlatform.Ai".
- **EC-CORE-01-02 — Žádný chat model `Enabled`** · Trigger: registry prázdná / vše `Disabled` · Očekávané chování: `BusinessRuleException` `ai.no_model_available` (500-class config error, fail-fast, ne tichý null) · Mechanismus: errorCode do `SharedResource.resx` en+cs · Severity: P1 · Test: registry bez aktivního modelu → exception, ne NRE.
- **EC-CORE-01-03 — `Ai:UseFakeGateways=true` v produkci** · Trigger: misconfig nasadí fake v prod · Očekávané chování: fail-fast při startu mimo Development (validátor Options), fake je pouze test-harness · Mechanismus: `AiOptionsValidator` (analogie `JwtOptionsValidator`/`RlsBootstrapper` fail-fast) · Severity: P0 · Test: Production env + UseFakeGateways → startup throw.
- **EC-CORE-01-04 — `feature` tag chybí v `LlmRequest`** · Trigger: volající nezadá feature · Očekávané chování: gateway odmítne (`ai.feature_tag_required`) — bez feature nelze atribuovat cost (UC-CORE-05) · Mechanismus: validace requestu uvnitř gateway · Severity: P1 · Test: request bez feature → exception.
- **EC-CORE-01-05 — `ITenantContext` bez tenant/user (background bez kontextu)** · Trigger: gateway volána z Wolverine handleru bez HttpContext · Očekávané chování: použít system tenant kontext (`SystemTenantContext`/`HttpTenantContext` fallback per CONVENTIONS), ledger zapíše system scope — NIKDY nehádat tenant · Mechanismus: tenant isolation pattern (CONVENTIONS §4 „Tenant isolation") · Severity: P1 · Test: worker volá gateway → ledger řádek je system-scoped, ne náhodný tenant.
- **EC-CORE-01-06 — Provider vrátí prázdný/oříznutý výstup (`finishReason=length`)** · Trigger: model dosáhne maxTokens · Očekávané chování: vrátit částečný výstup + `finishReason` ve `LlmResponse`, NEpovažovat za chybu, usage se započítá normálně · Mechanismus: usage capture nezávislé na finish reason · Severity: P2 · Test: maxTokens=1 → response s `finishReason=length`, cost > 0.
- **EC-CORE-01-07 — Cancellation token zruší volání během provider callu** · Trigger: klient disconnect / `ct` cancel · Očekávané chování: provider call se přeruší, usage co dorazilo se reconciluje (UC-CORE-12 pro streaming; non-stream → žádný ledger zápis pokud nepřišla usage) · Mechanismus: `OperationCanceledException` propaguje, ledger zápis jen při zachycené usage · Severity: P2 · Test: cancel během callu → žádný phantom cost.
- **EC-CORE-01-08 — Souběžná volání téhož uživatele (paralelní requesty)** · Trigger: 2 handlery volají gateway současně · Očekávané chování: každé volání nezávislý ledger řádek (vlastní idempotency key), budget check atomicky serializován (UC-CORE-06) · Mechanismus: idempotentní append-only ledger + Redis atomic budget · Severity: P1 · Test: 10-way paralelní volání → 10 ledger řádků, žádný lost update na budgetu.

---

## UC-CORE-02 — `IEmbeddingGenerator` (embeddingy přes OpenAI) + fake
- **Actor / role:** consuming-module (RAG ingest/query, semantic cache UC-CORE-07)
- **Precondition:** embed model `Enabled` v registry (kind=`embed`), OpenAI klíč v Options nebo fake flag · **Trigger:** interní `IEmbeddingGenerator.GenerateAsync(IReadOnlyList<string>, ct)`
- **Main flow:**
  1. Volající předá batch textů + `feature` tag.
  2. Gateway resolvne embed model (default z config `Ai:Embeddings:DefaultModel`), aplikuje budget pre-check, volá OpenAI přes MEAI `IEmbeddingGenerator<string, Embedding<float>>`.
  3. Usage capture (embed tokeny → cost dle embed pricing rate), ledger zápis s `kind=embed`, vrátí `IReadOnlyList<Embedding<float>>` + dimenze + model name (pro drift detekci, UC-CORE-11 / RAG 03/09).
- **Postcondition / záruky:** embeddingy nesou `model`+`dimensions` metadata (kritické pro reembed/drift); cost zaúčtován stejným ledgerem jako chat · **Tenancy / permissions:** Scope z tokenu; ledger `IUserOwned` (RLS) · **Reuse / canonical pattern:** stejný provider-seam pattern jako UC-CORE-01; fake `FakeEmbeddingGenerator` (deterministický hash→vektor) · **Data dotčena:** `ai_usage_ledger` (insert, kind=embed) · **Eventy:** žádný · **Priorita:** P0

### Edge cases UC-CORE-02
- **EC-CORE-02-01 — Batch překročí provider limit (počet/tokeny)** · Trigger: 5000 textů v jednom volání · Očekávané chování: gateway interně chunkuje na provider-safe batche, agreguje usage do jednoho ledger řádku per logické volání · Mechanismus: chunking helper uvnitř building-blocku · Severity: P1 · Test: nadlimitní batch → správný výsledek + 1 agregovaný cost.
- **EC-CORE-02-02 — Embed model swap bez reembed (dimenze/sémantika drift)** · Trigger: admin změní default embed model, RAG má staré vektory · Očekávané chování: gateway vrací `model`+`dimensions`; konzument (RAG 03/09) MUSÍ detekovat mismatch a reembedovat — building-block drift nemaskuje, jen exponuje metadata · Mechanismus: model metadata na response; cross-ref RAG reembed flow · Severity: P0 · Test: dvě volání různými modely → různé `model`/`dimensions` ve výstupu.
- **EC-CORE-02-03 — Prázdný/whitespace text v batchi** · Trigger: konzument pošle `""` · Očekávané chování: validace odmítne nebo skipne (dle policy), nikdy neposlat prázdný text provideru (plýtvání + některé providery error) · Mechanismus: input sanitizace · Severity: P2 · Test: prázdný text → validační chyba `ai.empty_embedding_input`.
- **EC-CORE-02-04 — Provider 429 na embed** · Trigger: rate limit OpenAI · Očekávané chování: respekt `Retry-After`, fallback chain (UC-CORE-10) na alternativní embed provider pokud nakonfigurován, jinak `BusinessRuleException` · Mechanismus: fallback chain + Retry-After honor · Severity: P1 · Test: simulované 429 → retry/fallback, ne crash.
- **EC-CORE-02-05 — Fake generator nedeterministický** · Trigger: testy semantic cache potřebují stabilní vektory · Očekávané chování: `FakeEmbeddingGenerator` deterministický (hash textu → fixní vektor) aby semantic cache testy byly reproducibilní · Mechanismus: deterministická fake impl · Severity: P2 · Test: dvojí generování téhož textu → identický vektor.
- **EC-CORE-02-06 — Cost atribuce embed bez feature tagu** · Trigger: semantic cache interně embeduje query · Očekávané chování: interní embed volání nese systémový feature tag (`ai.semantic_cache`) aby cost nebyl „nezatříděný" · Mechanismus: building-block stampuje vlastní feature na interní volání · Severity: P2 · Test: semantic cache embed → ledger feature=`ai.semantic_cache`.

---

## UC-CORE-03 — Per-request usage capture se 4 token countery × per-model price table → USD
- **Actor / role:** system (building-block interní krok)
- **Precondition:** model má v `ai_models` vyplněné pricing rates (input, output, cache_creation, cache_read) · **Trigger:** dokončení provider callu (chat i embed)
- **Main flow:**
  1. Z provider response se přečtou 4 countery: `prompt_tokens`, `completion_tokens`, `cache_creation_tokens`, `cache_read_tokens` (Anthropic prompt-caching semantika; OpenAI mapování ekvivalentů, chybějící counter = 0).
  2. Lookup pricing pro daný `model` z registry (rates per 1M tokenů, oddělené sazby pro cache creation/read).
  3. `costUsd = Σ(counter_i × rate_i)`; výpočet v `decimal`, UTC timestamp z `IClock`.
  4. Předá `UsageCapture` do ledger zápisu (UC-CORE-04) + metrics (UC-CORE-15).
- **Postcondition / záruky:** cost je deterministicky odvozen z token counts + registry pricing (NE z provider-reportovaného dolaru — provideři ne vždy reportují cenu); cache-read tokeny levnější než fresh · **Tenancy / permissions:** N/A (interní) · **Reuse / canonical pattern:** „cost z token counts" (CONVENTIONS §15) · **Data dotčena:** `ai_models` (read pricing) · **Eventy:** žádný · **Priorita:** P0

### Edge cases UC-CORE-03
- **EC-CORE-03-01 — Pricing chybí pro nový/neznámý model** · Trigger: model přidán do registry bez rates, nebo provider vrátí jiný model než vyžádaný · Očekávané chování: NEodhadovat tiše — zapsat ledger s `costUsd=null` + flag `pricing_missing=true` + WARN log + metrika `platform.ai.pricing_missing`; volání NEselže (data se neztratí), ale dashboardy vidí gap · Mechanismus: explicit null-cost + alert (CONVENTIONS §10 cost autorita) · Severity: P0 · Test: model bez rates → ledger costUsd=null, WARN, request OK.
- **EC-CORE-03-02 — Provider vrátí jiný model než vyžádaný (silent upgrade/alias)** · Trigger: `claude-x-latest` → konkrétní verze · Očekávané chování: usage capture použije `model` z RESPONSE (ne z requestu) pro pricing lookup; alias se resolvne na konkrétní řádek registry · Mechanismus: response-driven model resolution · Severity: P1 · Test: alias request → ledger nese skutečný model id.
- **EC-CORE-03-03 — Cache counter chybí (provider nereportuje)** · Trigger: OpenAI bez cache_creation semantiky · Očekávané chování: chybějící counter = 0, cost se počítá jen z dostupných · Mechanismus: defensivní mapping s defaulty · Severity: P2 · Test: response bez cache countů → cost jen prompt+completion.
- **EC-CORE-03-04 — Floating-point drift v ceně** · Trigger: cost počítán v `double` · Očekávané chování: VŽDY `decimal` (peníze), zaokrouhlení na definovanou přesnost (např. 10 desetinných pro micro-cost agregaci) · Mechanismus: money correctness (CONVENTIONS — decimal jako u Billing) · Severity: P1 · Test: 10000 micro-callů → agregace bez drift.
- **EC-CORE-03-05 — Tokenizer mismatch (counter vs lokální odhad)** · Trigger: gateway pre-flight odhad ≠ provider-reported · Očekávané chování: AUTORITATIVNÍ je provider-reported usage; lokální tokenizer (UC-CORE-14) jen pro pre-flight budget odhad/cache klíč, ledger bere provider čísla · Mechanismus: provider-reported wins · Severity: P1 · Test: odhad ≠ skutečnost → ledger = skutečnost.
- **EC-CORE-03-06 — Pricing změna v čase (rate revize)** · Trigger: provider zlevní model, admin upraví rate · Očekávané chování: cost počítán pricingem platným v čase volání; historické ledger řádky se NEpřepočítávají (append-only, immutable cost) · Mechanismus: cost zamražen při zápisu; volitelně `pricing_version` na řádku · Severity: P2 · Test: změna rate → staré řádky beze změny, nové dle nového rate.

---

## UC-CORE-04 — `AiUsageLedger` (append-only, platform-wide cost) s idempotentním zápisem
- **Actor / role:** system (zápis), platform-admin / consuming-module (read agregace)
- **Precondition:** `ai_usage_ledger` migrovaná (kind `ITenantScoped`+`IUserOwned`) · **Trigger:** dokončení usage capture (UC-CORE-03)
- **Main flow:**
  1. Sestaví se `AiUsageEntry`: `Id` (Guid v7), `tenant_id`, `user_id`, `feature`, `model`, `provider`, `kind` (chat/embed/rerank), 4 countery, `costUsd`, `cacheLayerHit` (none/exact/semantic/provider_prefix), `latencyMs`, `requestId` (idempotency key), `occurredAtUtc`.
  2. Insert přes write context; UNIQUE na `request_id` (idempotency).
  3. Pokud `DbUpdateException` (duplicitní request_id z retry) → zápis se považuje za již proběhlý, vrátí se bez chyby.
- **Postcondition / záruky:** append-only (žádné UPDATE/DELETE řádků — AML/cost audit); platform-wide (jedna tabulka, NE per-modul); idempotentní vůči retry · **Tenancy / permissions:** `ITenantScoped`+`IUserOwned` → RLS auto; agregační čtení gated `PlatformPermissions.AiManage` nebo per-tenant scope · **Reuse / canonical pattern:** append-only ledger + UNIQUE idempotency key + catch `DbUpdateException` jako `Billing` credit ledger; `Guid.CreateVersion7()` · **Data dotčena:** `ai_usage_ledger` · **Eventy:** žádný (cost je fakt, ne event); RAG observabilita (Oblast 19) čte tuto tabulku · **Priorita:** P0

### Edge cases UC-CORE-04
- **EC-CORE-04-01 — Dvojí zápis při retry (Wolverine redelivery / provider retry)** · Trigger: stejné volání zaúčtováno 2× · Očekávané chování: UNIQUE `request_id` → druhý insert chycen jako duplicate, žádné dvojí účtování · Mechanismus: UNIQUE key + catch `DbUpdateException` (CONVENTIONS money idempotence) · Severity: P0 · Test: 2× insert se stejným request_id → 1 řádek.
- **EC-CORE-04-02 — Ledger write selže, ale provider call proběhl (cost „unbilled")** · Trigger: DB výpadek po úspěšném model callu · Očekávané chování: cost se nesmí ztratit — buď (a) zápis ledgeru je součástí stejné transakce/outboxu jako navazující práce, nebo (b) reconciliation job (cross-ref UC-CORE-15) sesouhlasí provider usage reporty s ledgerem · Mechanismus: rozhodnout: in-band write vs out-of-band reconciliation — OTEVŘENÁ sub-decision, eskalovat (Law 11) · Severity: P0 · Test: DB fail po callu → reconciliation/alert, ne tichá ztráta.
- **EC-CORE-04-03 — Pokus o UPDATE/DELETE ledger řádku** · Trigger: někdo chce „opravit" cost · Očekávané chování: zakázáno (append-only); korekce = nový kompenzační řádek (negativní/adjustment), původní immutable · Mechanismus: append-only invariant (jako AML ledger) · Severity: P1 · Test: pokus o mutaci → zamítnuto policy/review.
- **EC-CORE-04-04 — Cross-tenant leak při agregaci** · Trigger: tenant-admin čte cost dashboard · Očekávané chování: RLS na `user_id`/`tenant_id` zajistí, že tenant vidí jen své; platform-admin (cross-tenant) jen přes `AiManage` permission + explicitní system scope · Mechanismus: RLS + permission gate (Law 10) · Severity: P0 · Test: tenant A nevidí řádky tenant B.
- **EC-CORE-04-05 — `ExecuteUpdate`/`ExecuteDelete` na ledgeru (bypass audit)** · Trigger: hromadná „cleanup" operace · Očekávané chování: zakázáno na cost řádcích (bypass auditu+xmin); retention/anonymizace přes řízený flow, ne raw bulk · Mechanismus: CONVENTIONS §4 caveat (ExecuteUpdate bypassuje interceptor) · Severity: P1 · Test: review/arch rule.
- **EC-CORE-04-06 — GDPR erasure uživatele s ledger historií** · Trigger: `UserErasureRequested` · Očekávané chování: cost řádky (finanční/AML) se FYZICKY nemažou — anonymizují (`user_id` → tombstone/null-pseudonym), částky zůstávají pro agregaci · Mechanismus: `IErasePersonalData` impl building-blocku (anonymize, ne delete — CONVENTIONS GDPR) · Severity: P0 · Test: erasure → ledger zachová cost, ztratí PII vazbu.
- **EC-CORE-04-07 — Ledger roste neomezeně (objem)** · Trigger: vysoký traffic · Očekávané chování: agregační/partitioning strategie (měsíční rollup tabulka pro dashboardy), raw řádky s retencí dle policy; NEpoužívat lagging rollup pro budget enforcement (UC-CORE-06) · Mechanismus: rollup ≠ enforcement source · Severity: P2 · Test: rollup job agreguje, real-time budget čte Redis.

---

## UC-CORE-05 — Per-tenant/user/feature cost atribuce
- **Actor / role:** consuming-module (poskytuje feature tag na call site), platform-admin (čte atribuci)
- **Precondition:** `LlmRequest`/embed volání nese `feature` tag; `ITenantContext` má tenant+user · **Trigger:** každé gateway volání
- **Main flow:**
  1. Na call site konzument předá stabilní `feature` identifikátor (např. `marketing.vibe_chat`, `rag.answer_generation`, `rag.query_embedding`).
  2. Gateway doplní `tenant_id`+`user_id` z `ITenantContext` (NIKDY z LLM výstupu/body).
  3. Tato trojice (`tenant`, `user`, `feature`) + `model` se zapíše na ledger řádek → umožní agregaci „kolik stojí feature X pro tenant Y".
- **Postcondition / záruky:** každý dolar je atribuovatelný na (tenant, user, feature, model); žádný „nezatříděný" cost · **Tenancy / permissions:** identita z tokenu (Law 10); feature z call site (kompilační konstanta, ne user input) · **Reuse / canonical pattern:** tag z `ITenantContext`; feature jako konstanty (analogie `PlatformPermissions` konstant) · **Data dotčena:** `ai_usage_ledger` · **Eventy:** žádný · **Priorita:** P0

### Edge cases UC-CORE-05
- **EC-CORE-05-01 — Feature tag z user inputu (injection)** · Trigger: konzument by chtěl feature z request body · Očekávané chování: zakázáno — feature je server-side konstanta na call site, nikdy z klienta (jinak tenant zfalšuje atribuci) · Mechanismus: typovaný `AiFeature` enum/konstanty · Severity: P1 · Test: feature není parametr veřejného endpointu.
- **EC-CORE-05-02 — Chybí tenant tag (system volání)** · Trigger: platform job volá LLM bez tenanta · Očekávané chování: atribuce na system tenant (explicitní pseudo-tenant), ne null/náhodný · Mechanismus: `SystemTenantContext` · Severity: P1 · Test: jobs host volání → feature atribuováno system.
- **EC-CORE-05-03 — Cost atribuce nesedí s Billing entitlement** · Trigger: ledger říká tenant X spotřeboval, Billing nemá záznam · Očekávané chování: ledger je autorita pro SPOTŘEBU; tie do Billing (UC-CORE-06) je enforcement, ne dvojí účetnictví; nesoulad → WARN + reconciliation · Mechanismus: jeden source pro spotřebu (ledger), Billing jen limit · Severity: P2 · Test: porovnání ledger vs entitlement → konzistentní.
- **EC-CORE-05-04 — Stejný feature napříč moduly kolize jména** · Trigger: dva moduly použijí `chat` · Očekávané chování: feature konvence `{module}.{usecase}` (namespaced) brání kolizi · Mechanismus: pojmenovací konvence vynucená v review · Severity: P3 · Test: lint/review na feature tagy.
- **EC-CORE-05-05 — Sub-volání (cache embed) atribuováno na user feature** · Trigger: semantic cache interně embeduje · Očekávané chování: interní volání má vlastní system feature, ne user feature (jinak zkreslí atribuci) · Mechanismus: viz EC-CORE-02-06 · Severity: P2 · Test: ledger odlišuje user vs interní embed.

---

## UC-CORE-06 — Hard per-tenant token/cost budgety → 429 `ai.quota_exceeded` + Retry-After, real-time enforce (Redis bucket), tie do Billing
- **Actor / role:** consuming-module (nepřímo), tenant-admin (vidí spotřebu), platform-admin (nastavuje limity)
- **Precondition:** tenant má definovaný budget (z Billing entitlements / config `Ai:Budgets`); Redis dostupný pro sdílený bucket · **Trigger:** budget pre-check na začátku každého gateway volání
- **Main flow:**
  1. Před provider callem gateway odhadne náklad (pre-flight tokeny přes tokenizer UC-CORE-14 × pricing) NEBO inkrementuje token/cost counter v Redis bucketu klíčovaném `tenant_id` + perioda.
  2. Atomická operace (Redis `INCRBY`/Lua skript) ověří `spent + estimate <= limit`.
  3. Pokud překročeno → `BusinessRuleException` `ai.quota_exceeded` → HTTP 429 + `Retry-After` (do konce periody / reset window).
  4. Po skutečném callu se bucket dorovná o reálnou spotřebu (estimate → actual reconciliation).
- **Postcondition / záruky:** budget je real-time vynucen sdíleným stavem (NE lagging rollup z ledgeru — multi-instance Api/Worker by jinak přestřelil); over-budget volání se neprovede · **Tenancy / permissions:** Scope per tenant; enforcement nezávislý na permission · **Reuse / canonical pattern:** sdílený Redis bucket jako rate-limiter (CONVENTIONS „rate limiting = sdílený stav"); `BusinessRuleException`→429; tie do `Billing` entitlements (`ModuleEntitlementGuard` analogie) · **Data dotčena:** Redis bucket (runtime), `ai_usage_ledger` (post-fact dorovnání), Billing entitlement (read limit) · **Eventy:** volitelně `AiBudgetExceededIntegrationEvent` pro notifikaci tenant-admina (Notifications modul) · **Priorita:** P0

### Edge cases UC-CORE-06
- **EC-CORE-06-01 — Concurrent budget check race (multi-instance)** · Trigger: 50 paralelních volání těsně pod limitem · Očekávané chování: atomický Redis INCR/Lua zajistí, že součet nepřekročí limit; žádný lost update · Mechanismus: atomic Redis (analogie atomic `ExecuteUpdate` guard u money) · Severity: P0 · Test: 50-way paralelní → spotřeba ≤ limit, přebytek dostane 429.
- **EC-CORE-06-02 — Redis nedostupný (bucket down)** · Trigger: výpadek Redis · Očekávané chování: conservative fail — buď fail-closed (odmítnout, `ai.budget_unavailable`) nebo fail-open s degraded warning dle policy; NEignorovat tiše (jinak unbounded cost) — OTEVŘENÁ sub-decision, eskalovat · Mechanismus: explicitní fallback policy · Severity: P0 · Test: Redis down → definované chování, ne unbounded spend.
- **EC-CORE-06-03 — Estimate < actual (pre-flight podstřelil)** · Trigger: výstup delší než odhad · Očekávané chování: bucket se dorovná po callu reálnou spotřebou; tenant může krátkodobě mírně přestřelit (bounded over-run), další volání odmítnuto · Mechanismus: estimate-then-reconcile · Severity: P1 · Test: velký výstup → bucket dorovnán, příští call 429.
- **EC-CORE-06-04 — `Retry-After` chybí / nesmyslný** · Trigger: 429 bez hlavičky · Očekávané chování: vždy nastavit `Retry-After` na sekundy do resetu periody (denní/měsíční window) · Mechanismus: Web error mapping (RFC9457 + header) · Severity: P1 · Test: 429 response má validní Retry-After.
- **EC-CORE-06-05 — `ai.quota_exceeded` vs Billing entitlement nesoulad** · Trigger: Ai budget říká OK, Billing entitlement modul vypnut · Očekávané chování: oba guardy nezávislé — entitlement (modul povolen?) PŘED budgetem (kolik?); pořadí jasné, errorCody odlišné (`ai.module_not_entitled` vs `ai.quota_exceeded`) · Mechanismus: dvě vrstvy guardů · Severity: P1 · Test: vypnutý entitlement → entitlement error, ne quota.
- **EC-CORE-06-06 — Budget periodreset (rollover)** · Trigger: přechod den/měsíc · Očekávané chování: bucket má TTL = perioda, reset automatický; žádný „přenos" nevyčerpaného (hard budget, ne credit) · Mechanismus: Redis TTL keyed periodou · Severity: P2 · Test: po resetu periody → spotřeba 0.
- **EC-CORE-06-07 — Per-user budget vs per-tenant budget** · Trigger: jeden user vyčerpá celý tenant budget · Očekávané chování: podpora obou úrovní (tenant hard cap + volitelný per-user sub-cap); enforcement bere přísnější · Mechanismus: vícevrstvý bucket (tenant + user klíč) · Severity: P2 · Test: user sub-cap → user 429 i když tenant má rezervu.
- **EC-CORE-06-08 — Streaming volání a budget (cost neznámý předem)** · Trigger: dlouhý stream · Očekávané chování: pre-check na základě maxTokens cap; po streamu reconcile reálné usage (UC-CORE-12) do bucketu · Mechanismus: maxTokens jako horní mez pro pre-check · Severity: P1 · Test: stream → budget rezervuje maxTokens, dorovná po done.

---

## UC-CORE-07 — Cache 3 vrstvy (exact → semantic → provider prefix), tenant-scoped klíč, hit-rate per vrstva
- **Actor / role:** system (interní), nepřímo všichni konzumenti
- **Precondition:** cache backend (Redis) dostupný; embed model pro semantic vrstvu · **Trigger:** cache lookup před provider callem (pokud `cachePolicy` povoluje)
- **Main flow:**
  1. **Vrstva 1 — exact:** hash z (`tenant_id` + model + normalizované zprávy + params) → Redis lookup; hit → vrátit cached response, ledger `cacheLayerHit=exact`, cost (cache read = ~0).
  2. **Vrstva 2 — semantic:** embed query (UC-CORE-02), ANN/similarity search v tenant-scoped semantic cache; pokud similarity ≥ threshold (`Ai:Cache:SemanticThreshold`) → vrátit, `cacheLayerHit=semantic`.
  3. **Vrstva 3 — provider prefix-cache:** sestavit prompt s prefix-cache breakpointy (UC-CORE-13) → provider naúčtuje `cache_read_tokens` levněji; `cacheLayerHit=provider_prefix`.
  4. Miss → plný call, výsledek se uloží do exact+semantic vrstvy s TTL.
- **Postcondition / záruky:** cache klíč VŽDY nese `tenant_id` (žádný cross-tenant leak); hit-rate měřen per vrstva (`platform.ai.cache_hit{layer}`) · **Tenancy / permissions:** klíč tenant-scoped (poisoning guard); semantic index per tenant · **Reuse / canonical pattern:** Redis (StackExchange.Redis); cache jako u Realtime replay (TTL+bounded) · **Data dotčena:** Redis cache, semantic index · **Eventy:** žádný · **Priorita:** P0

### Edge cases UC-CORE-07
- **EC-CORE-07-01 — Cache poisoning / cross-tenant leak (klíč bez tenant_id)** · Trigger: dva tenanti stejný prompt · Očekávané chování: klíč MUSÍ obsahovat `tenant_id` → tenant A nikdy nedostane odpověď generovanou pro tenant B (data leak!) · Mechanismus: tenant-scoped cache key (NEPODKROČITELNÉ) · Severity: P0 · Test: tenant A i B stejný prompt → oddělené cache entries, žádný leak.
- **EC-CORE-07-02 — Semantic cache false-hit (nízký práh)** · Trigger: threshold moc nízký → podobná ale významově jiná query vrátí špatnou odpověď · Očekávané chování: konzervativní default threshold (vysoký), konfigurovatelný per feature; raději miss než false-hit · Mechanismus: tunable threshold + per-feature override · Severity: P0 · Test: dvě podobné-ale-jiné query → ne false hit při default thresholdu.
- **EC-CORE-07-03 — Cache vrací stale odpověď po změně system promptu/modelu** · Trigger: prompt/model verze se změní · Očekávané chování: cache klíč zahrnuje model + prompt verzi/hash → změna invaliduje · Mechanismus: verzovaný cache klíč · Severity: P1 · Test: změna promptu → cache miss, nová generace.
- **EC-CORE-07-04 — Cache hit pro structured-output, ale schema se změnilo** · Trigger: konzument změní JsonSchema · Očekávané chování: schema hash součástí klíče · Mechanismus: viz EC-CORE-07-03 · Severity: P2 · Test: změna schématu → miss.
- **EC-CORE-07-05 — Semantic cache pro PII/citlivý obsah** · Trigger: cache uloží odpověď s PII · Očekávané chování: cache hodnoty per-tenant izolované + TTL; volitelně cachování vypnuto pro feature s PII (`cachePolicy=NoStore`) · Mechanismus: per-feature cache policy + PII redakce (UC-CORE-15) · Severity: P1 · Test: feature s NoStore → nic v cache.
- **EC-CORE-07-06 — Cache hit se počítá jako budget spend?** · Trigger: tenant nad budgetem, ale cache hit · Očekávané chování: exact/semantic hit má ~0 cost → projde i nad budgetem (žádné nové provider tokeny); ledger zaznamená hit s cost≈0 · Mechanismus: cost-aware budget (hit nezvyšuje spend) · Severity: P2 · Test: over-budget tenant + cache hit → vrátí cached, ne 429.
- **EC-CORE-07-07 — Redis cache výpadek** · Trigger: Redis down · Očekávané chování: degraded — přeskočit exact+semantic, jít na plný call (fail-open na CACHE je bezpečné, na rozdíl od budgetu); WARN log · Mechanismus: cache miss-on-error · Severity: P1 · Test: Redis down → volání funguje bez cache.
- **EC-CORE-07-08 — Semantic index drift po model swapu** · Trigger: embed model změněn, staré vektory v indexu · Očekávané chování: semantic cache index verzovaný embed modelem; mismatch → ignorovat staré vektory / rebuild · Mechanismus: model-versioned index (cross-ref UC-CORE-02-02) · Severity: P1 · Test: swap embed → starý index se nepoužije pro nový model.
- **EC-CORE-07-09 — Cache nekonzistence při souběhu (stampede)** · Trigger: 100 identických queries miss současně → 100 provider callů · Očekávané chování: single-flight / request coalescing (jeden call, ostatní čekají na výsledek) — cross-ref Redis single-flight v MEMORY · Mechanismus: single-flight lock per cache klíč · Severity: P1 · Test: 100 paralelních identických → 1 provider call.

---

## UC-CORE-08 — Model registry `AiModel` + pricing jako config + admin endpointy (permission-gated)
- **Actor / role:** platform-admin (CRUD modelů), system (read pro routing/pricing)
- **Precondition:** `ai_models` migrovaná; admin má `PlatformPermissions.AiManage` · **Trigger:** HTTP admin endpoint (`GET/POST/PATCH /v1/ai/admin/models`) nebo seed při startu
- **Main flow:**
  1. `AiModel` entita: `name`/`id`, `provider`, `kind` (chat|embed|rerank), pricing rates (`inputRate`, `outputRate`, `cacheCreationRate`, `cacheReadRate` per 1M), `status` (active|deprecated|disabled), `contextWindow`, `maxOutput`, `tier` (cheap|frontier), `aliases`.
  2. Admin endpointy: list / create / update / disable — všechny gated `.RequirePermission(PlatformPermissions.AiManage)`.
  3. Nový model nebo změna ceny = config/DB edit (NE změna kódu); registry je cache-ovaná in-memory s invalidací při zápisu.
- **Postcondition / záruky:** přidání modelu / změna pricingu bez deployu kódu; pricing autoritativní pro cost · **Tenancy / permissions:** `AiManage` permission (auto-seeded, auto-granted admin); system-scoped data (modely nejsou tenant-specific) · **Reuse / canonical pattern:** admin slice jako Billing catalogue (`billing.manage`); endpoint pod `/v1` group; `PlatformPermissions` konstanta · **Data dotčena:** `ai_models` · **Eventy:** volitelně `AiModelRegistryChangedIntegrationEvent` pro invalidaci cache routingu · **Priorita:** P1

### Edge cases UC-CORE-08
- **EC-CORE-08-01 — Admin endpoint IDOR / chybějící permission** · Trigger: ne-admin volá update modelu · Očekávané chování: 403 `ForbiddenException`; permission z tokenu (Law 10) · Mechanismus: `.RequirePermission(AiManage)` · Severity: P0 · Test: user bez AiManage → 403.
- **EC-CORE-08-02 — Disable modelu, který je aktivně routovaný** · Trigger: admin disable default model · Očekávané chování: routing přestane model vybírat; běžící volání dokončí; pokud žádný aktivní zůstane → `ai.no_model_available` (EC-CORE-01-02); registry cache invalidace · Mechanismus: status check v routingu · Severity: P1 · Test: disable jediného aktivního → další volání error.
- **EC-CORE-08-03 — Pricing rate záporný / nulový / nesmyslný** · Trigger: admin zadá `inputRate=-5` · Očekávané chování: validace odmítne (`ai.invalid_pricing`) · Mechanismus: FluentValidation na admin command · Severity: P1 · Test: záporná cena → 400.
- **EC-CORE-08-04 — Duplicitní model name/alias** · Trigger: dva řádky stejný name · Očekávané chování: UNIQUE na name+provider; alias collision odmítnuta · Mechanismus: UNIQUE constraint · Severity: P2 · Test: duplicitní name → conflict.
- **EC-CORE-08-05 — Registry cache stale po edit (multi-instance)** · Trigger: admin upraví rate na instanci A, instance B má starou · Očekávané chování: invalidace přes event/Redis pub-sub nebo krátký TTL na cache; pricing edit se propíše · Mechanismus: cache invalidace (event/TTL) · Severity: P1 · Test: edit na A → B vidí nový rate do X s.
- **EC-CORE-08-06 — Audit změn pricingu** · Trigger: admin mění cenu · Očekávané chování: `AuditInterceptor` zaznamená změnu (kdo, kdy, stará→nová hodnota) — pricing je finančně citlivý · Mechanismus: `AuditInterceptor` na `ai_models` context · Severity: P1 · Test: pricing edit → audit řádek.
- **EC-CORE-08-07 — Context window / maxOutput nesmyslné vůči provideru** · Trigger: admin zadá contextWindow větší než provider podporuje · Očekávané chování: validace varuje; runtime stejně řeší provider error → fallback · Mechanismus: validace + runtime guard · Severity: P3 · Test: nadhodnocený window → routing nepřekročí provider limit.

---

## UC-CORE-09 — Model routing 2-tier (levný default, frontier na hard/low-confidence) + confidence cascade
- **Actor / role:** system (interní routing)
- **Precondition:** ≥1 cheap + ≥1 frontier model `active`; routing policy v config (`Ai:Routing`) · **Trigger:** gateway volání bez explicitního `modelHint`, nebo s `routingPolicy`
- **Main flow:**
  1. Default: vybrat **cheap** tier model.
  2. Classifier/heuristika (délka, klíčová slova, feature-specific policy, volitelně lehký classifier call) určí „hard?" → eskalace na **frontier**.
  3. **Confidence cascade:** spustit cheap, pokud model/odpověď signalizuje nízkou confidence (např. self-report, validační selhání structured-output, refusal) → re-run na frontier; jinak vrátit levný výsledek.
  4. Vybraný model + důvod routingu se loguje na span + ledger.
- **Postcondition / záruky:** většina trafficu na levném modelu, frontier jen kde třeba → cost optimalizace bez kvalitativního propadu · **Tenancy / permissions:** routing policy může být per-tenant/feature override (UC-CORE-11) · **Reuse / canonical pattern:** config-driven (Options), žádná business logika v kódu napevno · **Data dotčena:** `ai_models` (read), `ai_usage_ledger` (zapíše skutečný model + routing reason) · **Eventy:** žádný · **Priorita:** P1

### Edge cases UC-CORE-09
- **EC-CORE-09-01 — Routing pošle hard query na levný model (kvalita drop)** · Trigger: classifier podstřelí složitost · Očekávané chování: confidence cascade zachytí (low-confidence → frontier re-run); cost obou volání se zaúčtuje · Mechanismus: cascade + dvojí ledger řádek · Severity: P1 · Test: hard query → cheap fail/low-conf → frontier retry, korektní výstup.
- **EC-CORE-09-02 — Cascade nekonečná (cheap i frontier low-confidence)** · Trigger: oba modely nejistí · Očekávané chování: max 1 eskalace (cheap→frontier), pak vrátit frontier výsledek i při low-conf (ne loop) · Mechanismus: bounded cascade depth · Severity: P1 · Test: oba low-conf → max 2 volání, vrátí poslední.
- **EC-CORE-09-03 — Classifier call sám stojí víc než úspora** · Trigger: drahý classifier na triviální query · Očekávané chování: classifier je levný/heuristický (ne frontier call); volitelně lokální (tokenizer délka, keyword) bez LLM · Mechanismus: lightweight classifier · Severity: P2 · Test: classifier cost « routovaný call cost.
- **EC-CORE-09-04 — `modelHint` explicitní obchází routing** · Trigger: konzument vyžádá konkrétní model · Očekávané chování: hint respektován (bypass routing), ale stále podléhá budgetu + fallbacku · Mechanismus: explicit override path · Severity: P2 · Test: hint → daný model, budget stále platí.
- **EC-CORE-09-05 — Frontier model disabled, routing chce eskalovat** · Trigger: hard query, ale frontier `disabled` · Očekávané chování: fallback chain (UC-CORE-10) najde další nejlepší dostupný; pokud žádný → degradace na cheap s WARN · Mechanismus: routing↔fallback integrace · Severity: P1 · Test: frontier down → next-best, ne crash.
- **EC-CORE-09-06 — Routing reason chybí v observabilitě** · Trigger: debugging „proč drahý model?" · Očekávané chování: každý routing zapíše `routing_reason` (default/classified_hard/cascade_escalation/explicit_hint) na span · Mechanismus: OTel span atribut · Severity: P2 · Test: span obsahuje routing_reason.

---

## UC-CORE-10 — Multi-provider fallback chains (rate-limit/outage → next tier/provider)
- **Actor / role:** system (interní resilience)
- **Precondition:** fallback chain definovaná v config (`Ai:Fallback` — seřazený seznam modelů/providerů per tier) · **Trigger:** provider call selže (429, 5xx, timeout, network)
- **Main flow:**
  1. Primární model selže transientně (429/timeout/5xx).
  2. Gateway respektuje `Retry-After` (u 429), pak zkusí další položku v chain (jiný model téhož provideru / jiný provider).
  3. Cold-start konzervativně (ne hned frontier zaplavit); každý pokus má vlastní timeout.
  4. Po vyčerpání chain → `BusinessRuleException` `ai.all_providers_unavailable` (degradace, ne crash).
- **Postcondition / záruky:** transientní výpadek jednoho provideru neshodí feature; cost účtován za skutečně použitý model · **Tenancy / permissions:** N/A · **Reuse / canonical pattern:** retry-with-cooldown jako Wolverine messaging resilience; respekt provider `Retry-After` (CONVENTIONS provider 429) · **Data dotčena:** `ai_usage_ledger` (zapíše skutečně úspěšný model + attempt count), metriky · **Eventy:** žádný · **Priorita:** P1

### Edge cases UC-CORE-10
- **EC-CORE-10-01 — Fallback loop / všichni provideři down** · Trigger: celá chain nedostupná · Očekávané chování: bounded počet pokusů, žádný nekonečný loop; finálně `ai.all_providers_unavailable` (503-class), degradace · Mechanismus: bounded chain length · Severity: P0 · Test: all down → jeden definovaný error, ≤N pokusů.
- **EC-CORE-10-02 — Provider 429 bez respektu Retry-After** · Trigger: gateway retry hned · Očekávané chování: VŽDY respektovat `Retry-After`; bez něj exponenciální backoff · Mechanismus: Retry-After honor + backoff · Severity: P1 · Test: 429 s Retry-After=10 → další pokus ne dřív než 10s (nebo skok na jiný provider).
- **EC-CORE-10-03 — Fallback na model s jinou cenou/kvalitou (tiché zhoršení)** · Trigger: frontier → cheap fallback · Očekávané chování: fallback zaznamená degradaci (WARN + span atribut `fallback_used`), cost dle skutečného modelu; konzument může vidět nižší kvalitu · Mechanismus: observabilita fallbacku · Severity: P2 · Test: fallback → ledger nese skutečný (levnější) model + flag.
- **EC-CORE-10-04 — Dvojí účtování při fallbacku (selhal po částečné usage)** · Trigger: primární vrátil partial usage pak selhal · Očekávané chování: účtovat usage co reálně proběhla u každého pokusu (i selhaného, pokud provider naúčtoval); idempotency per attempt · Mechanismus: per-attempt ledger s distinct request_id · Severity: P1 · Test: partial+fallback → cost obou pokusů, žádné duplicity.
- **EC-CORE-10-05 — Non-transientní chyba (400 bad request) zbytečně fallbackuje** · Trigger: chybný prompt/schema → 400 · Očekávané chování: NEretry/fallbackovat na 4xx (deterministická chyba) — jen na transientní (429/5xx/timeout/network) · Mechanismus: klasifikace chyb transient vs permanent · Severity: P1 · Test: 400 → okamžitý error, žádný fallback.
- **EC-CORE-10-06 — Cold-start zaplaví frontier při výpadku cheap** · Trigger: cheap provider down, všechen traffic skočí na frontier · Očekávané chování: konzervativní cold-start (postupné, ne okamžitý plný náraz), volitelně circuit-breaker · Mechanismus: circuit-breaker / gradual ramp · Severity: P2 · Test: cheap down → frontier nedostane okamžitě 100% bez omezení.
- **EC-CORE-10-07 — Streaming fallback uprostřed streamu** · Trigger: stream se přeruší v půlce · Očekávané chování: nelze transparentně pokračovat na jiném provideru (klient už dostal část) — buď restart streamu (pokud klient unese) nebo error; usage reconcile co přišlo (UC-CORE-12) · Mechanismus: stream není mid-flight fallbackovatelný · Severity: P2 · Test: stream break → definované chování, usage co přišlo zaúčtováno.

---

## UC-CORE-11 — Model A/B + swap (config-driven per tenant/feature) + shadow testing + LLM-judge
- **Actor / role:** platform-admin (nastaví A/B/shadow), system (exekuce)
- **Precondition:** ≥2 kandidátní modely active; A/B/shadow config v `Ai:Experiments` · **Trigger:** gateway volání pro feature s aktivním experimentem
- **Main flow:**
  1. **Swap:** config přepne default model pro tenant/feature bez deployu (`Ai:Routing:Overrides:{tenant}:{feature}`).
  2. **A/B:** % trafficu na kandidáta vs control; výsledky obou se logují (cost+latence+kvalita) pro srovnání.
  3. **Shadow:** kandidát běží na % trafficu PARALELNĚ k produkčnímu, ale jeho výstup se NIKDY neservíruje — jen se zaznamená pro srovnání.
  4. **LLM-judge:** offline/online judge srovná kvalitu kandidát vs control + cost → rozhodovací podklad PŘED flipnutím defaultu.
- **Postcondition / záruky:** model change je datově podložený (kvalita+cost), shadow nikdy neuteče do produkce · **Tenancy / permissions:** per-tenant/feature config; `AiManage` pro nastavení experimentu · **Reuse / canonical pattern:** config-driven (Options), žádný kód; LLM-judge přes `ILlmGateway` (vlastní feature tag) · **Data dotčena:** `ai_usage_ledger` (oba výstupy označené `experiment_arm`/`shadow`), volitelně `ai_experiment_results` · **Eventy:** žádný · **Priorita:** P2

### Edge cases UC-CORE-11
- **EC-CORE-11-01 — Shadow odpověď se omylem naservíruje do produkce** · Trigger: bug v routingu shadow · Očekávané chování: shadow výstup je STRIKTNĚ oddělen (jiná cesta, nikdy do response klienta); test pokrývá invariant · Mechanismus: shadow path nemá return-to-client větev · Severity: P0 · Test: shadow běží → klient dostane VŽDY control výstup.
- **EC-CORE-11-02 — Shadow zdvojnásobí cost bez user hodnoty** · Trigger: 100% shadow traffic · Očekávané chování: shadow jen na % trafficu (config cap), cost shadow oddělen ve ledgeru (`shadow=true`) aby nezkresloval produkční cost atribuci · Mechanismus: shadow % cap + oddělená atribuce · Severity: P1 · Test: shadow cost je v ledgeru odlišitelný a omezený.
- **EC-CORE-11-03 — Model swap bez reembed (embed drift)** · Trigger: swap EMBED modelu přes config · Očekávané chování: embed swap MUSÍ triggernout reembed flow u konzumentů (RAG); building-block varuje, že embed model nelze swapnout „za běhu" bez reindexace (cross-ref RAG 03/09, UC-CORE-02-02) · Mechanismus: embed model change = breaking, ne hot-swap · Severity: P0 · Test: embed swap → konzument upozorněn na nutnost reembed.
- **EC-CORE-11-04 — A/B split nekonzistentní per user (flapping)** · Trigger: user dostává jednou A jednou B · Očekávané chování: sticky assignment (hash z user_id) pro konzistentní zážitek v rámci experimentu · Mechanismus: deterministic bucketing dle user_id · Severity: P2 · Test: stejný user → stejné rameno napříč requesty.
- **EC-CORE-11-05 — LLM-judge bias / sám stojí hodně** · Trigger: judge na 100% trafficu · Očekávané chování: judge na vzorku (ne všem), cost judge oddělen; judge přes gateway s vlastním feature tagem (atribuovatelný) · Mechanismus: sampled judge + atribuce · Severity: P2 · Test: judge cost odlišitelný, na sample.
- **EC-CORE-11-06 — Flip defaultu uprostřed běžící konverzace** · Trigger: admin flipne model během multi-turn chatu · Očekávané chování: běžící session dokončí na původním modelu (konzistence kontextu), nové session na novém · Mechanismus: model pinning per session · Severity: P2 · Test: flip → aktivní session model nezměněn.
- **EC-CORE-11-07 — Experiment config nevalidní (rameno bez modelu)** · Trigger: A/B odkazuje na disabled model · Očekávané chování: validace config při startu/edit; nevalidní experiment ignorován + WARN, fallback na default · Mechanismus: experiment config validátor · Severity: P2 · Test: nevalidní experiment → default, ne crash.

---

## UC-CORE-12 — Streaming usage reconciliation (`include_usage`, čtení po disconnectu, multi-chunk akumulace)
- **Actor / role:** consuming-module (Marketing vibe streaming, budoucí RAG streaming)
- **Precondition:** streaming endpoint (SSE) konzumuje `ILlmGateway.CompleteStreamingAsync`; provider podporuje usage ve streamu · **Trigger:** streamovaný chat call
- **Main flow:**
  1. Gateway streamuje delty konzumentovi a SOUČASNĚ akumuluje usage z chunků (`include_usage` / final usage chunk).
  2. Usage typicky přijde v ZÁVĚREČNÉM chunku (nebo per-chunk inkrementálně) → akumulovat, NEPřepisovat dílčí hodnotami.
  3. Po dokončení streamu (i při disconnectu klienta) gateway dočte usage a zapíše ledger + dorovná budget bucket.
- **Postcondition / záruky:** i přerušený stream má zaúčtovanou reálnou spotřebu (žádný unbilled streaming cost); usage z multi-chunk korektně agregovaná · **Tenancy / permissions:** Scope z tokenu; ledger `IUserOwned` · **Reuse / canonical pattern:** disconnect-safe save jako Marketing vibe streaming (MEMORY: SSE delta/done, disconnect-safe) · **Data dotčena:** `ai_usage_ledger`, Redis budget bucket · **Eventy:** žádný · **Priorita:** P0

### Edge cases UC-CORE-12
- **EC-CORE-12-01 — Klient disconnect uprostřed streamu** · Trigger: browser zavře SSE · Očekávané chování: gateway dočte stream na pozadí (nebo z toho co dorazilo) a zaúčtuje usage; ledger zápis proběhne i bez klienta · Mechanismus: server-side stream completion nezávislé na klientovi · Severity: P0 · Test: disconnect v půlce → ledger má usage.
- **EC-CORE-12-02 — Usage chunk nepřišel vůbec** · Trigger: provider neposlal final usage / stream se utnul před usage · Očekávané chování: fallback odhad z lokálního tokenizeru (UC-CORE-14) + flag `usage_estimated=true`; reconciliation později opraví z provider reportu pokud dostupný · Mechanismus: estimate fallback + reconciliation · Severity: P1 · Test: chybějící usage chunk → odhad + flag.
- **EC-CORE-12-03 — Multi-chunk usage přepsán místo akumulace** · Trigger: provider posílá kumulativní vs inkrementální usage · Očekávané chování: znát semantiku (Anthropic kumulativní vs OpenAI final) — NEsčítat kumulativní dvakrát; per-provider adapter ví, jak číst · Mechanismus: per-provider usage parsing · Severity: P0 · Test: kumulativní usage → ledger = poslední hodnota, ne součet.
- **EC-CORE-12-04 — Stream error po částečném výstupu** · Trigger: provider error v půlce streamu · Očekávané chování: zaúčtovat co proběhlo (`finishReason=error`), částečný text vrátit konzumentovi, ne ztratit cost · Mechanismus: partial reconciliation · Severity: P1 · Test: mid-stream error → částečný výstup + částečný cost.
- **EC-CORE-12-05 — Dvojí ledger zápis při stream retry** · Trigger: stream se restartuje (UC-CORE-10-07) · Očekávané chování: každý attempt vlastní request_id, idempotency brání duplicitě finálního zápisu · Mechanismus: per-attempt idempotency · Severity: P1 · Test: stream restart → bez dvojího účtování téhož.
- **EC-CORE-12-06 — Budget dorovnání po streamu překročí limit zpětně** · Trigger: pre-check rezervoval maxTokens, reálná spotřeba vyšší (nemělo by, ale guard) · Očekávané chování: reconcile na actual; pokud actual > rezervace → tenant mírně přestřelil, další call 429 (bounded over-run, jako EC-CORE-06-03) · Mechanismus: estimate-then-reconcile · Severity: P2 · Test: stream actual > estimate → bucket dorovnán.
- **EC-CORE-12-07 — Cache hit ve streamu** · Trigger: exact cache hit, ale konzument čeká stream · Očekávané chování: cached odpověď „streamována" v jednom chunku (UX konzistence) s `cacheLayerHit`, cost≈0 · Mechanismus: cache→stream adapter · Severity: P3 · Test: cache hit přes streaming API → jednorázový chunk, cost 0.

---

## UC-CORE-13 — Prompt-cache layout helper (static prefix → dynamic suffix, breakpointy)
- **Actor / role:** consuming-module (skládá prompty), system (helper)
- **Precondition:** provider podporuje prefix-cache (Anthropic prompt caching) · **Trigger:** sestavování `LlmRequest` přes helper `PromptBuilder`/`CacheLayout`
- **Main flow:**
  1. Helper vede konzumenta k uspořádání: **statický prefix** (system prompt, tool definice, dlouhý kontext) NA ZAČÁTEK → **dynamický suffix** (user query, měnící se data) NA KONEC.
  2. Umístí cache breakpointy za statickou část → provider cachuje prefix → následná volání platí `cache_read` (levně) místo `cache_creation`.
  3. Varuje před anti-patterny: „DateTime/Guid/random ve STŘEDU promptu = cache killer" (rozbije prefix match).
- **Postcondition / záruky:** opakovaná volání se sdíleným prefixem těží z provider cache (nižší cost+latence); helper předchází cache-busting chybám · **Tenancy / permissions:** N/A (formátování) · **Reuse / canonical pattern:** prompt-cache breakpoint API provideru přes MEAI; cost se projeví v `cache_read_tokens` (UC-CORE-03) · **Data dotčena:** žádná (jen tvar requestu) · **Eventy:** žádný · **Priorita:** P2

### Edge cases UC-CORE-13
- **EC-CORE-13-01 — Dynamický obsah ve statickém prefixu (cache killer)** · Trigger: `DateTime.UtcNow` v system promptu · Očekávané chování: helper detekuje/varuje na volatilní tokeny v prefix zóně; doporučí přesun do suffixu · Mechanismus: layout linter (volitelný) + dokumentace · Severity: P1 · Test: volatilní token v prefixu → warning / 0% cache hit demonstrace.
- **EC-CORE-13-02 — Prefix kratší než provider min. cache práh** · Trigger: krátký system prompt pod minimem pro caching · Očekávané chování: helper neumístí breakpoint (necachovatelné), žádná chyba, jen plný cost · Mechanismus: min-length awareness · Severity: P3 · Test: krátký prefix → bez breakpointu.
- **EC-CORE-13-03 — Příliš mnoho breakpointů (provider limit)** · Trigger: konzument vloží 10 breakpointů · Očekávané chování: helper omezí na provider-povolený počet (Anthropic ~4) · Mechanismus: breakpoint count cap · Severity: P2 · Test: nadlimit → ořezáno na max.
- **EC-CORE-13-04 — Cache breakpoint u provideru bez prefix-cache** · Trigger: model je OpenAI bez explicitních breakpointů · Očekávané chování: helper no-op pro providery bez feature (graceful), žádný error · Mechanismus: provider capability check · Severity: P2 · Test: OpenAI model → breakpointy ignorovány.
- **EC-CORE-13-05 — Tenant-specific prefix zabraňuje sdílení** · Trigger: prefix nese tenant data · Očekávané chování: cache je per-tenant stejně (provider cache je per API klíč/org), prefix-cache neuniká mezi tenanty na úrovni provideru; pozor že cache-key UC-CORE-07 stále tenant-scoped · Mechanismus: provider cache izolace · Severity: P2 · Test: tenant prefix → provider cache nepřekříží tenanty.
- **EC-CORE-13-06 — Tool definice se mění mezi volání (busted prefix)** · Trigger: dynamicky generované tool listy · Očekávané chování: helper doporučí stabilní tool ordering/serializaci aby prefix zůstal identický · Mechanismus: deterministická serializace tools · Severity: P2 · Test: stejné tools → identický prefix hash.

---

## UC-CORE-14 — Structured-output validace (JsonSchema.Net) + tokenizer (Microsoft.ML.Tokenizers)
- **Actor / role:** consuming-module (žádá strukturovaný výstup), system
- **Precondition:** konzument předá `responseFormat` (JsonSchema); tokenizer balík dostupný · **Trigger:** volání s `responseFormat` / pre-flight token count
- **Main flow:**
  1. **Structured output:** gateway pošle schema provideru (response_format/tool), výstup validuje proti JsonSchema.Net; nevalidní → repair/retry policy.
  2. **Tokenizer:** `Microsoft.ML.Tokenizers` poskytuje lokální token count pro (a) pre-flight budget odhad (UC-CORE-06), (b) cache klíč normalizaci, (c) fallback usage odhad (UC-CORE-12-02).
- **Postcondition / záruky:** structured výstup je schema-valid nebo explicitně selže (ne tiše vrátit nevalidní JSON); tokenizer poskytuje deterministický lokální odhad · **Tenancy / permissions:** N/A · **Reuse / canonical pattern:** JsonSchema.Net (free), Microsoft.ML.Tokenizers (free) jako součást building-blocku; validace mimo handler (gateway krok) · **Data dotčena:** žádná · **Eventy:** žádný · **Priorita:** P1

### Edge cases UC-CORE-14
- **EC-CORE-14-01 — Structured-output nevalidní JSON** · Trigger: model vrátí malformed/neúplný JSON · Očekávané chování: pokus o repair (re-prompt s chybou) max N×, pak `ai.structured_output_invalid`; NIKDY nevrátit nevalidní data konzumentovi · Mechanismus: validate→repair→fail · Severity: P1 · Test: malformed → repair retry → po N selhání error.
- **EC-CORE-14-02 — JSON validní ale neodpovídá schématu** · Trigger: chybí required pole · Očekávané chování: JsonSchema.Net validace zachytí, repair/retry · Mechanismus: schema validace · Severity: P1 · Test: chybějící required → validační chyba.
- **EC-CORE-14-03 — Tokenizer mismatch model vs counter** · Trigger: lokální tokenizer neodpovídá provider tokenizaci · Očekávané chování: lokální odhad je JEN odhad (budget pre-check, cache); AUTORITA je provider usage (EC-CORE-03-05); nepoužívat lokální pro účtování · Mechanismus: provider-reported wins · Severity: P1 · Test: odhad ≠ skutečnost → ledger = skutečnost.
- **EC-CORE-14-04 — Tokenizer chybí pro daný model** · Trigger: nový model bez ML.Tokenizers encoderu · Očekávané chování: fallback na nejbližší/heuristický odhad (char/4) + flag; nikdy crash · Mechanismus: tokenizer fallback · Severity: P2 · Test: neznámý model → heuristický odhad.
- **EC-CORE-14-05 — Repair loop zvyšuje cost neúměrně** · Trigger: model opakovaně vrací nevalidní · Očekávané chování: bounded repair (max N), každý pokus účtován, pak fail; volitelně eskalace na frontier (UC-CORE-09) pro lepší schema compliance · Mechanismus: bounded repair + cost transparency · Severity: P1 · Test: trvale nevalidní → ≤N pokusů, cost zaúčtován.
- **EC-CORE-14-06 — Schema příliš složitá pro provider** · Trigger: provider odmítne schema · Očekávané chování: graceful error `ai.schema_unsupported`, doporučit zjednodušení; volitelně fallback na free-form + post-parse · Mechanismus: provider capability check · Severity: P2 · Test: nadlimitní schema → jasný error.
- **EC-CORE-14-07 — Tokenizer počítá pomalu na velkém vstupu** · Trigger: 1M token kontext pre-count · Očekávané chování: tokenizace je rychlá lokální operace; u extrémů cache výsledek nebo odhad · Mechanismus: cached tokenization · Severity: P3 · Test: velký vstup → akceptovatelná latence.

---

## UC-CORE-15 — Observability: OTel GenAI spany + `platform.ai.*` metriky + PII redakce před tracing
- **Actor / role:** system (interní), platform-admin/SRE (čte telemetrii)
- **Precondition:** OTel nakonfigurováno (`AddPlatformTelemetry`); `PlatformMetrics.Meter` registrován · **Trigger:** každé gateway volání
- **Main flow:**
  1. Per volání vytvořit GenAI semconv span: `gen_ai.operation.name` (chat/embed), `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `gen_ai.system` (provider).
  2. Emitovat metriky z `PlatformMetrics.Meter` (name `ModularPlatform`): `platform.ai.tokens`, `platform.ai.cost_usd`, `platform.ai.latency`, `platform.ai.cache_hit{layer}`, `platform.ai.budget_rejected`, `platform.ai.fallback_used`, `platform.ai.pricing_missing`.
  3. **PII redakce:** před zápisem promptu/výstupu do span/log atributů projít redakcí (žádné plné prompty s PII do tracing backendu) — buď neukládat obsah, nebo maskovat.
- **Postcondition / záruky:** každé volání pozorovatelné (cost/latence/model/cache), bez úniku PII do telemetrie; cost na metrikách odvozen z token counts (UC-CORE-03) · **Tenancy / permissions:** metriky tagovány tenant/feature (kardinalita pozor); čtení dle telemetrie infra · **Reuse / canonical pattern:** `PlatformMetrics.Meter` + `.AddMeter` (CONVENTIONS custom metrics); naming `platform.ai.*`; OTel GenAI semconv · **Data dotčena:** žádná persistovaná (telemetrie) · **Eventy:** žádný · **Priorita:** P0

### Edge cases UC-CORE-15
- **EC-CORE-15-01 — PII (prompt obsah) unikne do span/log** · Trigger: prompt s e-mailem/jménem logován naplno · Očekávané chování: redakce před tracing — defaultně NEukládat obsah promptu/výstupu, jen metadata (tokeny, model); pokud content sampling zapnut → maskovat PII · Mechanismus: PII redakční filtr před span atributy (analogie `[PersonalData]` filozofie) · Severity: P0 · Test: prompt s PII → span neobsahuje plný text.
- **EC-CORE-15-02 — Metrika nikdy `.AddMeter`-ована (tichý drop)** · Trigger: nový instrument mimo `PlatformMetrics.Meter` · Očekávané chování: VŠECHNY AI instrumenty z jediného `PlatformMetrics.Meter` (už exportovaného) · Mechanismus: CONVENTIONS „second Meter never AddMeter-ed" anti-pattern · Severity: P1 · Test: instrument viditelný v exportu.
- **EC-CORE-15-03 — Vysoká kardinalita tagů (user_id na metrice)** · Trigger: metrika tagovaná per-user · Očekávané chování: metriky tagovat per-tenant+feature+model (bounded), NE per-user (kardinalita exploze); per-user granularita jen v ledgeru · Mechanismus: bounded tag set · Severity: P1 · Test: metrika nemá user_id dimenzi.
- **EC-CORE-15-04 — Span chybí při fail/exception** · Trigger: provider call hodí · Očekávané chování: span se uzavře s error statusem + exception event, ne „zmizí"; cost co proběhl zaznamenán · Mechanismus: span scope kolem celé pipeline · Severity: P1 · Test: provider error → span se statusem error.
- **EC-CORE-15-05 — Cost metrika ≠ ledger cost** · Trigger: metrika počítá jinak než ledger · Očekávané chování: oba ze stejného `UsageCapture` (jeden zdroj pravdy), žádná divergence · Mechanismus: single cost source · Severity: P1 · Test: agregace metriky ≈ agregace ledgeru.
- **EC-CORE-15-06 — `budget_rejected` metrika chybí u 429** · Trigger: budget odmítne, ale nezaznamená · Očekávané chování: každé budget rejection inkrementuje `platform.ai.budget_rejected{tenant}` · Mechanismus: metrika v budget guardu · Severity: P2 · Test: nad-budget volání → counter +1.
- **EC-CORE-15-07 — Cache hit nezapočítán do latence/cost správně** · Trigger: cache hit má latenci~0, cost~0 · Očekávané chování: hit metriky odlišeny (`cache_hit{layer}`), latence i cost reflektují near-zero · Mechanismus: cache-aware metriky · Severity: P3 · Test: cache hit → latence « miss, cost ≈ 0.

---

## UC-CORE-16 — Secrets (provider API klíče) v Options, fail-fast mimo Dev, maskování v admin/effective výstupu
- **Actor / role:** platform-admin (konfiguruje), system (čte), nikdy klient
- **Precondition:** provider klíče v `IConfiguration` (env/secret store) mapované do `AiSecretsOptions` · **Trigger:** startup validace + jakýkoli admin „effective config" výstup
- **Main flow:**
  1. Provider klíče (`Ai:Anthropic:ApiKey`, `Ai:OpenAI:ApiKey`) čteny POUZE přes typovaný Options objekt.
  2. `AiOptionsValidator` při startu mimo Development fail-fast pokud klíč chybí/placeholder (analogie `JwtOptionsValidator`).
  3. Jakýkoli admin endpoint vracející „effective config" / model registry MASKUJE secret hodnoty (`sk-...****`), nikdy plný klíč.
- **Postcondition / záruky:** secret nikdy v response/log/admin výstupu; misconfig se chytí při startu, ne až za běhu · **Tenancy / permissions:** secrets jen system-level; admin „effective config" gated `AiManage`, ale i tak maskováno · **Reuse / canonical pattern:** Options + fail-fast (CONVENTIONS §8 secrets, `JwtOptionsValidator`/`RlsBootstrapper`); maskování jako u jiných secret výstupů · **Data dotčena:** žádná persistovaná · **Eventy:** žádný · **Priorita:** P0

### Edge cases UC-CORE-16
- **EC-CORE-16-01 — Secret leak přes admin/effective endpoint** · Trigger: admin volá „zobraz config" · Očekávané chování: klíče VŽDY maskované (`****`), i pro AiManage admina · Mechanismus: serializace s mask na secret fields · Severity: P0 · Test: effective config response neobsahuje plný klíč.
- **EC-CORE-16-02 — Klíč chybí v produkci** · Trigger: deploy bez `Ai:Anthropic:ApiKey` · Očekávané chování: fail-fast při startu (mimo Dev), aplikace nenaběhne s chybějícím secretem · Mechanismus: `AiOptionsValidator` · Severity: P0 · Test: prod bez klíče → startup throw.
- **EC-CORE-16-03 — Placeholder/dev klíč v produkci** · Trigger: `sk-dev-placeholder` v prod · Očekávané chování: validátor odmítne známé placeholdery mimo Dev · Mechanismus: placeholder detekce (jako RLS RuntimePassword) · Severity: P1 · Test: placeholder v prod → fail-fast.
- **EC-CORE-16-04 — Secret v exception message / stack trace** · Trigger: provider SDK vyhodí chybu s klíčem · Očekávané chování: gateway zabalí provider exceptions (anti-corruption layer), nepropustí secret do logu/response · Mechanismus: ACL kolem provider SDK · Severity: P0 · Test: provider auth error → log bez klíče.
- **EC-CORE-16-05 — Secret čten mimo Options (přímý `IConfiguration[...]`)** · Trigger: kód čte klíč napřímo · Očekávané chování: zakázáno — jen přes Options typ (CONVENTIONS „never read a secret outside its Options type") · Mechanismus: konvence + review · Severity: P1 · Test: grep/review na přímý config access.
- **EC-CORE-16-06 — Per-tenant provider klíč (BYOK)** · Trigger: tenant chce vlastní API klíč · Očekávané chování: pokud podporováno → klíč šifrovaný at-rest (`[Encrypted]` per-tenant), maskovaný ve výstupu; pokud ne → mimo scope, eskalovat (OTEVŘENÁ sub-decision) · Mechanismus: `[Encrypted]` PII-at-rest pattern (CONVENTIONS) · Severity: P2 · Test: BYOK klíč šifrován + maskován.

---

## UC-CORE-17 — Perzistence building-blocku (`AiDbContext` / platform tabulky, migrace) + OTEVŘENÁ sub-decision BB vs always-on modul
- **Actor / role:** platform (infra), system
- **Precondition:** rozhodnutí o tvaru perzistence; migrace přes MigrationService · **Trigger:** startup migrace / DB schema setup
- **Main flow:**
  1. Building-block vlastní tabulky `ai_usage_ledger`, `ai_models` (+ volitelně `ai_experiment_results`).
  2. Buď: (a) **čistý BB s perzistencí** — `AiDbContext : PlatformDbContext`, migrace přes building-block, DI registrace jako infra; NEBO (b) **tenký always-on platform modul `ModularPlatform.Ai`** s `IModule` impl (entity + admin endpointy nahrávají této variantě).
  3. RLS na `ai_usage_ledger` (`IUserOwned`); admin endpointy mapované pod `/v1/ai/admin/*`.
- **Postcondition / záruky:** AI data perzistovaná s RLS+audit kde dává smysl; migrace deterministická · **Tenancy / permissions:** ledger `ITenantScoped`+`IUserOwned` (RLS); modely system-scoped · **Reuse / canonical pattern:** `PlatformDbContext` + `IEntityTypeConfiguration` + `PlatformMigrator`; pokud modul → `IdentityModule` vzor · **Data dotčena:** `ai_usage_ledger`, `ai_models`, migrace · **Eventy:** žádný · **Priorita:** P1 (sub-decision P0 jako blocker architektury)

> ⚠️ **OTEVŘENÁ ARCHITEKTONICKÁ SUB-DECISION (NEIMPROVIZOVAT — Law 11, eskalovat uživateli):** Má `ModularPlatform.Ai` být **čistý building-block s perzistencí** (infra v `src/building-blocks/`, žádný `IModule`) NEBO **tenký always-on platform modul `ModularPlatform.Ai`** (`src/modules/`, `IModule`, vždy `Enabled`)? Argumenty PRO modul: nese entity (`AiUsageLedger`, `AiModel`) + migrace + admin HTTP endpointy + GDPR `IErasePersonalData` impl — to vše je „modulová" mašinérie, kterou building-blocky typicky nemají. Argumenty PRO building-block: musí být volán Z modulů (Marketing, RAG) → nesmí být peer-modul (reference graph by se zacyklil: modul→modul Core zakázán); cost+budgety jsou platform-wide chokepoint, ne produktová doména. **Tato volba určuje reference graf, migrace, DI wiring a host composition — rozhodnout PŘED stavbou.**

### Edge cases UC-CORE-17
- **EC-CORE-17-01 — Modul referencuje AI building-block Core (kdyby byl modul)** · Trigger: RAG/Marketing potřebuje `ILlmGateway` · Očekávané chování: pokud BB → modul→building-block reference OK; pokud modul → modul→modul Core ZAKÁZÁN (reference graph law) → port musí být v Abstractions/BB i tak · Mechanismus: reference graph (ArchUnitNET) — silný argument pro BB variantu · Severity: P0 · Test: ArchUnitNET ověří, že `ILlmGateway` je dosažitelný z modulů legálně.
- **EC-CORE-17-02 — Migrace AI tabulek v MigrationService** · Trigger: deploy aplikuje schema · Očekávané chování: `AiDbContext` migrace registrovány ve všech 4 hostech / migration service (jako modul) NEBO jako platform infra migrace; admin connection (DDL), ne `app_rls` role · Mechanismus: `PlatformMigrator` na admin connection (CONVENTIONS RLS) · Severity: P0 · Test: migrace proběhne na admin roli.
- **EC-CORE-17-03 — `ai_usage_ledger` RLS na `IUserOwned`** · Trigger: tenant čte svůj cost · Očekávané chování: RLS keyed `app.principal_id` jako ostatní `IUserOwned` tabulky → forgotten WHERE neunikne · Mechanismus: `RlsBootstrapper` na ledger tabulce · Severity: P0 · Test: RLS politika existuje na ledgeru.
- **EC-CORE-17-04 — Audit na `ai_models` přes který context** · Trigger: pricing edit má být auditován · Očekávané chování: `ai_models` přes audited context (`AuditInterceptor` → `ai_audit_entries`); ledger zápisy NEauditovat duplicitně (ledger JE audit) · Mechanismus: context separation (CONVENTIONS audit caveat) · Severity: P1 · Test: model edit → audit; ledger insert → ne dvojí audit.
- **EC-CORE-17-05 — Always-on modul disabled omylem** · Trigger: pokud modulová varianta a někdo dá `Enabled=false` · Očekávané chování: AI je platform prerekvizita → buď nemá Enabled flag (always-on) nebo disable shodí závislé moduly fail-fast s jasnou hláškou · Mechanismus: always-on invariant · Severity: P1 · Test: disable → fail-fast nebo no-op (dle rozhodnutí).
- **EC-CORE-17-06 — Boot test pro DI graf AI** · Trigger: nová závislost rozbije DI · Očekávané chování: host boot test (`Hosts.Tests` / `ValidateOnBuild`) validuje, že AI building-block/modul má splnitelný DI graf ve všech hostech · Mechanismus: boot test (CONVENTIONS host composition) · Severity: P1 · Test: boot test zelený.

---

## UC-CORE-18 — Migrace Marketing `IVibeAgentGateway`/`IMarketingAiGateway` → `ILlmGateway` (follow-up, jednotný cost capture)
- **Actor / role:** platform (refactor), system
- **Precondition:** `ModularPlatform.Ai` postaven (UC-CORE-01..17); Marketing modul má existující `IVibeAgentGateway` (MEMORY: Marketing vibe chat, tool-use loop, streaming) · **Trigger:** follow-up refactor po dokončení building-blocku
- **Main flow:**
  1. Identifikovat všechna místa, kde Marketing volá LLM napřímo (`IVibeAgentGateway`, `IMarketingAiGateway`, real/fake gatewaye, GA4/GSC AI cesty).
  2. Přepojit je na `ILlmGateway` (chat) / `IEmbeddingGenerator` s feature tagem `marketing.*`.
  3. Zachovat tool-use loop, streaming (`/messages/stream`), 202+worker+realtime semantiku — jen pod-volání LLM jde přes core chokepoint.
  4. Po migraci: Marketing cost se objeví v `ai_usage_ledger` (jednotná atribuce), staré Marketing-specifické usage tracking odstranit.
- **Postcondition / záruky:** VŠECHEN LLM cost platformy teče jedním ledgerem; žádné LLM volání mimo `ILlmGateway`; Marketing chování nezměněno (jen plumbing) · **Tenancy / permissions:** beze změny (identita z tokenu) · **Reuse / canonical pattern:** reference discovery všech Marketing LLM call sites; provider seam konsolidace · **Data dotčena:** Marketing kód (volání), `ai_usage_ledger` (nově nese Marketing) · **Eventy:** beze změny · **Priorita:** P1

### Edge cases UC-CORE-18
- **EC-CORE-18-01 — Marketing tool-use loop nekompatibilní s gateway API** · Trigger: vibe agent dělá multi-turn tool calls · Očekávané chování: `ILlmGateway` musí podporovat tool/function calling (předat tools, vrátit tool_use, pokračovat loop) — jinak Marketing nelze migrovat · Mechanismus: gateway API pokrývá tool-use (MEAI `ChatOptions.Tools`) · Severity: P0 · Test: vibe agent loop funguje přes gateway.
- **EC-CORE-18-02 — Streaming regrese (vibe `/messages/stream`)** · Trigger: migrace rozbije SSE streaming · Očekávané chování: `CompleteStreamingAsync` (UC-CORE-12) zachová delta/done + disconnect-safe save; e2e Marketing streaming testy zelené · Mechanismus: streaming parita · Severity: P0 · Test: Marketing streaming e2e prochází po migraci.
- **EC-CORE-18-03 — Dvojí usage tracking během přechodu** · Trigger: Marketing i gateway oba účtují · Očekávané chování: po migraci odstranit Marketing-specifický tracking; jeden ledger zápis per call · Mechanismus: odstranění duplicitní logiky (DRY, Law 4) · Severity: P1 · Test: Marketing call → 1 ledger řádek, ne 2.
- **EC-CORE-18-04 — Fake gateway nesoulad (Marketing fake vs Ai fake)** · Trigger: Marketing měl vlastní `FakeVibeAgentGateway` · Očekávané chování: testy přepnout na `Ai:UseFakeGateways`; Marketing fake odstranit (jeden fake seam) · Mechanismus: konsolidace fake pod jeden flag · Severity: P1 · Test: Marketing testy běží na Ai fake.
- **EC-CORE-18-05 — Reference discovery vynechá call site** · Trigger: zapomenuté přímé LLM volání v Marketing · Očekávané chování: ArchUnitNET rule (EC-CORE-01-01) chytí jakoukoli zbylou přímou referenci na provider SDK · Mechanismus: arch test jako safety net · Severity: P1 · Test: arch test po migraci zelený (žádná přímá provider reference v Marketing).
- **EC-CORE-18-06 — Feature atribuce Marketing chybí/nekonzistentní** · Trigger: migrované volání bez `marketing.*` tagu · Očekávané chování: každé migrované call site dostane konzistentní feature tag · Mechanismus: feature tag review · Severity: P2 · Test: Marketing ledger řádky mají `marketing.*` feature.
- **EC-CORE-18-07 — Budget nově aplikován na Marketing (dříve neměl)** · Trigger: Marketing volání teď podléhá per-tenant budgetu · Očekávané chování: úmyslné — Marketing cost se nově enforce-uje; ověřit, že existující Marketing flow nepřekročí default budget (jinak regrese 429) · Mechanismus: budget config pro Marketing feature · Severity: P1 · Test: Marketing flow nedostane neočekávané 429 při rozumném budgetu.
