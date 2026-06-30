# Oblast 19 — Observability & cost
Tato oblast pokrývá kompletní pozorovatelnost a nákladovou atribuci HybridRag pipeline: OpenTelemetry GenAI semantic-convention spany pro každou fázi (rewrite → embed → vector-search → graph → rerank → generate), `platform.rag.*` metriky na sdíleném `PlatformMetrics.Meter`, per-tenant/per-user/per-session korelaci na každém trace, sledování token-nákladů (input/output/cache_read) a jejich převod na peníze, povinnou PII redakci PŘED odesláním čehokoli do tracing/3rd-party backendu, a dva produktové dotazy — „kolik nás stojí klient X" a „proč klientovi Y vyšla divná odpověď". Mapuje se na build fázi **Fáze 6 — Observability & cost hardening** (poslední fáze, vrství se nad hotovou retrieval+generate pipeline z oblastí 11–18). Telemetrie je cross-cutting: instrumentuje existující handlery, NEpřidává nové business flow. Vše přes `PlatformMetrics.Meter` (`PlatformMetrics.cs:19`, name `ModularPlatform`, naming `platform.rag.{thing}`) a `ActivitySource` zaregistrovaný v `AddPlatformTelemetry` přes `.AddMeter`/`.AddSource`.

## UC-19-01 — GenAI semconv spany pro každou fázi RAG pipeline
- **Actor / role:** system/worker (instrumentace běží uvnitř query/ingest handlerů)
- **Precondition:** HybridRag modul povolen; `AddPlatformTelemetry` zaregistroval `ActivitySource("ModularPlatform.Rag")` přes `.AddSource`; OTel exporter (OTLP) nakonfigurován.
- **Trigger:** jakýkoli RAG dotaz — `POST /v1/rag/query` (nebo `/rag/ask` streaming) → `HybridSearchQueryHandler` / `GenerateAnswerHandler`.
- **Main flow:**
  1. Endpoint → `IDispatcher.Query` → `TelemetryBehavior` otevře kořenový span dotazu (`platform.cqrs.query`).
  2. Handler otevře child span per fázi přes `ActivitySource.StartActivity` s názvem dle GenAI semconv: `rewrite_query`, `embeddings`, `vector_search` (`retrieval`), `graph_expand` (`execute_tool` nebo `retrieval`), `rerank`, `generate` (`chat`).
  3. Každý span dostane atributy: `gen_ai.operation.name` (`embeddings`/`chat`/`execute_tool`), `gen_ai.system` (`openai`/`cohere`/`anthropic`), `gen_ai.request.model`, `gen_ai.response.model`, a fázově specifické (`db.system=postgresql` + `rag.retrieval.top_k` u vektoru, `rag.rerank.candidate_count` u reranku).
  4. Token-usage atributy (`gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `gen_ai.usage.cache_read_input_tokens`) se nasadí AŽ po návratu od providera, ne predikčně.
  5. Spany se uzavřou v `finally`; výjimka → `activity.SetStatus(ActivityStatusCode.Error)` + `activity.AddException(ex)` (bez PII v message).
- **Postcondition / záruky:** jeden trace = jeden RAG dotaz se 6+ child spany ve správné hierarchii; každá fáze měřitelná samostatně; trace exportován do OTLP; žádná mutace DB (query path).
- **Tenancy / permissions:** spany nesou `tenant_id`/`user_id`/`session_id` (UC-19-09); query path nemá vlastní permission nad rámec retrievalu; instrumentace nikdy nemění scope.
- **Reuse / canonical pattern:** `TelemetryBehavior` (Telemetry building-block) jako vzor pro span lifecycle; `PlatformMetrics.cs:19` pro Meter; GenAI semconv atributy dle OTel spec. Streaming generate kopíruje `StreamMessageEndpoint.cs:34` (span se uzavře v disconnect-safe `finally`).
- **Data dotčená:** žádná tabulka (jen Activity/trace export) · **Eventy:** žádné (telemetrie ≠ integration event)
- **Priorita:** P0

### Edge cases UC-19-01
- **EC-19-01-01 — Provider selže uprostřed fáze (embed 500/timeout)** · Trigger: OpenAI embeddings vrátí 500 nebo vyprší timeout · Očekávané chování: span `embeddings` dostane `ActivityStatusCode.Error` + exception event, `error.type` atribut, ALE parent span pokračuje pokud fáze degraduje (UC-19-08); span se VŽDY uzavře · Mechanismus: `try/finally` kolem `StartActivity`; chyby překládá `GlobalExceptionMiddleware` jen pokud probublají; degradace přes explicit flag (Zákon: nikdy tichá půlka) · Severity: P0 · Test: integration — fake gateway hodí výjimku, assert span má status Error + parent trace existuje.
- **EC-19-01-02 — `ActivitySource` nemá listenera (sampling off / žádný exporter)** · Trigger: `StartActivity` vrátí `null` protože nikdo neposlouchá · Očekávané chování: handler NESMÍ spadnout na NRE; všechny `activity?.SetTag(...)` přes null-conditional; business logika běží beze změny · Mechanismus: null-conditional operátor na každém přístupu k Activity; vzor z `TelemetryBehavior` · Severity: P0 · Test: unit — bez registrovaného listeneru zavolat handler, assert žádná výjimka.
- **EC-19-01-03 — Span hierarchie se rozpadne přes Wolverine hranici (durable generate ve workeru)** · Trigger: `generate` běží v jiném procesu (Worker) než `vector_search` (Api), trace context se nepropaguje · Očekávané chování: trace context (traceparent) se serializuje do Wolverine envelope headers a obnoví ve worker handleru → jeden spojitý trace přes proces · Mechanismus: Wolverine OTel integrace propaguje `traceparent`/`tracestate` v message headers; instrumentace v `ProcessVibeTurnCommand.cs:84` vzor pro AFTER-commit realtime · Severity: P1 · Test: integration — durable async ask, assert worker span má stejný `trace_id` jako Api span.
- **EC-19-01-04 — `gen_ai.operation.name` použit špatný pro graf fázi** · Trigger: graph_expand je LINQ join (ne LLM volání), ale dostane `gen_ai.operation.name=chat` · Očekávané chování: graf traverz NENÍ GenAI operace — buď `execute_tool` (když volá MCP/agentic tool) nebo čistě `db.*` retrieval span bez `gen_ai.*`; nesmí znečistit GenAI cost agregaci falešnými tokeny · Mechanismus: konvence — jen skutečná LLM volání (embed/rerank/chat) dostanou `gen_ai.usage.*`; DB fáze dostanou `db.system`/`db.operation` · Severity: P1 · Test: unit — assert graph span nemá `gen_ai.usage.input_tokens`.
- **EC-19-01-05 — Token counts chybí v provider response (starší API verze / fake gateway)** · Trigger: response nemá usage objekt · Očekávané chování: `gen_ai.usage.*` atributy se NEnasadí (žádná nula-lež), místo toho `rag.usage.unavailable=true` flag → cost agregace ví, že chybí data, nepočítá to jako 0 Kč · Mechanismus: podmíněné `SetTag` jen když usage != null; fallback flag · Severity: P1 · Test: integration — fake bez usage, assert flag + cost report označí dotaz jako „usage neznámé".
- **EC-19-01-06 — Příliš dlouhý prompt/dokument v span atributu (cardinality + size blowup)** · Trigger: někdo nasadí celý chunk text nebo query jako span atribut · Očekávané chování: span atributy nesmí obsahovat plný obsah dokumentů/chunků/promptu (PII + size); jen metadata (délka, počet, model, top_k) · Mechanismus: konvence + lint review; PII redakce UC-19-04 platí i na spany · Severity: P0 (PII) · Test: review-assert — žádný `SetTag` s `chunk.Content`/`query.Text`.
- **EC-19-01-07 — Streaming generate disconnect — span se neuzavře** · Trigger: klient zavře SSE spojení uprostřed generování · Očekávané chování: `generate` span se uzavře v `finally` i při `OperationCanceledException`; status `Error` jen pokud to byla skutečná chyba, jinak normální uzavření s `rag.generate.client_disconnected=true` · Mechanismus: disconnect-safe `CancellationToken.None` na uzávěru, vzor `StreamMessageEndpoint.cs:34` · Severity: P1 · Test: integration — disconnect mid-stream, assert span uzavřen + flag.
- **EC-19-01-08 — Concurrent dotazy promíchají Activity.Current (async context leak)** · Trigger: paralelní dotazy ve stejném procesu · Očekávané chování: každý dotaz má vlastní `Activity.Current` přes `AsyncLocal`; spany se nepřekrývají mezi dotazy · Mechanismus: .NET `Activity` je `AsyncLocal`-based; neukládat Activity do statiky/singletonu · Severity: P0 · Test: integration — 20 paralelních dotazů, assert každý trace má přesně své child spany.

## UC-19-02 — `platform.rag.*` metriky na PlatformMetrics.Meter
- **Actor / role:** system/worker
- **Precondition:** `PlatformMetrics.Meter` existuje (`PlatformMetrics.cs:19`); HybridRag instrumenty vytvořené při startu modulu (ne per-request); `.AddMeter("ModularPlatform")` v `AddPlatformTelemetry`.
- **Trigger:** každá fáze pipeline emituje metriku (po dokončení embed/retrieval/rerank/generate).
- **Main flow:**
  1. Při startu modulu se z `PlatformMetrics.Meter` vytvoří statické instrumenty: `Histogram<long> platform.rag.embed_tokens`, `Histogram<double> platform.rag.retrieval_latency` (ms), `Histogram<double> platform.rag.rerank_latency` (ms), `Histogram<double> platform.rag.generate_latency`, `ObservableGauge<long> platform.rag.index_size` (počet chunků), `Counter<long> platform.rag.degraded` (degradované dotazy), `Counter<long> platform.rag.queries`, `Histogram<long> platform.rag.retrieved_count`.
  2. Handler po fázi zavolá `histogram.Record(value, tags)` s tagy `tenant_id`, `gen_ai.system`, `gen_ai.request.model`, `rag.stage`.
  3. `index_size` gauge čte aktuální počet `IsCurrent` chunků per tenant přes lehký count query (cached, ne per-scrape DB hit).
  4. Exporter periodicky scrapuje (OTLP/Prometheus).
- **Postcondition / záruky:** metriky exportované pod jediným Meterem `ModularPlatform`; naming `platform.rag.*`; nízká kardinalita tagů (žádné user_id/session_id na metrikách — jen na trace).
- **Tenancy / permissions:** `tenant_id` je legitimní tag (low-cardinality, ohraničený počtem tenantů); `user_id`/`session_id` NEjsou metric tagy (cardinality exploze) — patří na trace.
- **Reuse / canonical pattern:** `PlatformMetrics.cs:19` (Meter), `platform.{area}.{thing}` naming; observable gauge vzor jako `MessagingHealthJob` gauges (`platform.messaging.*`).
- **Data dotčená:** `chunks` (jen count pro index_size gauge) · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-19-02
- **EC-19-02-01 — Druhý Meter, který není `.AddMeter`-ed (tiše neexportovaný)** · Trigger: někdo vytvoří `new Meter("Rag")` místo `PlatformMetrics.Meter` · Očekávané chování: VŠECHNY rag instrumenty MUSÍ viset na `PlatformMetrics.Meter`, jinak se nikdy neexportují · Mechanismus: CLAUDE.md §4 „custom metrics" zákon; review · Severity: P1 · Test: arch/unit — reflexí ověřit, že rag instrumenty patří `PlatformMetrics.Meter`.
- **EC-19-02-02 — Vysoká kardinalita tagů (user_id/query_text na metrice)** · Trigger: někdo přidá `user_id` nebo `session_id` jako metric tag · Očekávané chování: metriky mají JEN low-cardinality tagy (tenant_id, model, system, stage); high-cardinality (user/session/query) jde na trace, ne metriku · Mechanismus: konvence; OTel cardinality limit by jinak začal dropovat řady · Severity: P1 · Test: review-assert — žádný `user_id` tag na histogramu/counteru.
- **EC-19-02-03 — `index_size` gauge dělá těžký COUNT per scrape** · Trigger: Prometheus scrape každých 15s spustí `SELECT count(*)` přes všechny chunky · Očekávané chování: gauge čte z cache (TTL ~60s) nebo z udržovaného počítadla, ne plný table scan per scrape · Mechanismus: cached observable callback; nebo materializovaný per-tenant count aktualizovaný při ingest/delete · Severity: P2 · Test: integration — 10 scrape v řadě, assert ≤1 DB count query.
- **EC-19-02-04 — `degraded` counter se inkrementuje, ale dotaz uspěl normálně (false positive)** · Trigger: chybná logika značí degradaci i u plného výsledku · Očekávané chování: `platform.rag.degraded` se zvýší JEN když je výsledek skutečně Partial/Degraded (UC-19-08) — provider down, rerank skip, zero-retrieval fallback · Mechanismus: counter vázán na stejný `Degraded` flag jako response · Severity: P1 · Test: integration — happy path, assert degraded counter beze změny.
- **EC-19-02-05 — Latency histogram zahrnuje retry/backoff čas (zkresluje p99)** · Trigger: 429 retry s backoffem započítán do `retrieval_latency` · Očekávané chování: měřit i celkovou latenci (user-perceived) i samostatně provider-call latenci; rozlišit tagem `rag.attempt` nebo separátní histogram `platform.rag.provider_retry_count` · Mechanismus: dva instrumenty — wall-clock fáze vs. čistý provider call; counter retry · Severity: P2 · Test: integration — vynutit 429+retry, assert retry_count counter >0 a latency tag rozlišuje.
- **EC-19-02-06 — Instrument vytvořen per-request místo statického (memory/perf)** · Trigger: handler volá `Meter.CreateHistogram` při každém dotazu · Očekávané chování: instrumenty se vytvoří JEDNOU při startu (static/singleton), `Record` per request · Mechanismus: statická pole, vzor `PlatformMetrics` · Severity: P2 · Test: unit — instrument je stejná instance napříč dvěma dotazy.
- **EC-19-02-07 — `embed_tokens` se zaznamená i pro cache-hit embed (dvojí počítání)** · Trigger: embedding vzatý z cache, ale stejně `Record(tokens)` · Očekávané chování: token metrika odráží JEN skutečné provider volání; cache-hit → `platform.rag.embed_cache_hit` counter, ne embed_tokens · Mechanismus: oddělené instrumenty pro cache hit/miss · Severity: P2 · Test: integration — opakovaný stejný text, assert druhý nezvýší embed_tokens.

## UC-19-03 — Per-tenant nákladová atribuce z token counts
- **Actor / role:** system/worker (agregace) → tenant-admin (čtení reportu)
- **Precondition:** spany/metriky nesou `gen_ai.usage.input_tokens`/`output_tokens`/`cache_read_input_tokens` + `tenant_id` + `gen_ai.request.model`; cena per model/token je v configu (`Rag:Pricing:{model}:InputPerMTok` atd.).
- **Trigger:** každý dotaz/ingest emituje usage; agregace běží buď v OTel backendu (dotaz nad spany) nebo lokálně přes `Counter<long> platform.rag.tokens` s tagem model+kind.
- **Main flow:**
  1. Po každém LLM volání (embed/rerank/generate) handler zaznamená `platform.rag.tokens` counter s tagy `tenant_id`, `gen_ai.request.model`, `gen_ai.token.type` (`input`/`output`/`cache_read`).
  2. Cena = tokeny × sazba z `Rag:Pricing` (config-driven, model-specific, UTC-dated pro historickou přesnost).
  3. Náklad se buď agreguje v backendu (Prometheus recording rule / sumarizace) nebo persistuje do `rag_usage_daily` rollup tabulky (per tenant/model/den) přes denní cron job.
  4. Tenant-admin čte přes `GET /v1/rag/admin/cost` (permission `rag.cost.read`).
- **Postcondition / záruky:** každý token přiřazen tenantovi a modelu; cena deterministická z configu; cache_read se počítá levnější sazbou (Anthropic cache_read ~10 % ceny).
- **Tenancy / permissions:** Scope Tenant; čtení cost reportu gated `rag.cost.read` (tenant-admin); cross-tenant agregace jen platform-admin přes `admin.` rovinu.
- **Reuse / canonical pattern:** `PlatformMetrics.cs:19` counter; config-driven pricing jako `Billing:Subscriptions:Plans`; rollup cron jako `BillingExpireCreditsJob`; report query jako `GetProfileHandler.cs:12` (read factory).
- **Data dotčená:** volitelně `rag_usage_daily` (rollup) · **Eventy:** volitelně `RagUsageRolledUpIntegrationEvent` (pokud billing modul chce účtovat spotřebu)
- **Priorita:** P0

### Edge cases UC-19-03
- **EC-19-03-01 — `cache_read_input_tokens` účtováno plnou input sazbou (přefakturace)** · Trigger: cache_read tokeny sečteny s input_tokens · Očekávané chování: cache_read má SAMOSTATNOU (nižší) sazbu; nesmí se míchat s čerstvými input tokeny · Mechanismus: tři token kindy v ceníku, `gen_ai.token.type=cache_read` → `Rag:Pricing:{model}:CacheReadPerMTok` · Severity: P0 · Test: unit — cost kalkulace cache_read vs input rozdílná.
- **EC-19-03-02 — Embed a rerank tokeny smíchány s chat tokeny (špatný model ceník)** · Trigger: embed tokeny účtovány chat sazbou · Očekávané chování: cena per model — `text-embedding-3-large`, `rerank-3.5`, `claude-*` mají vlastní sazby; tag `gen_ai.request.model` rozlišuje · Mechanismus: model-keyed ceník · Severity: P0 · Test: unit — tři modely, tři sazby, správný součet.
- **EC-19-03-03 — Model bez ceníkové položky (nový model nasazen)** · Trigger: provider vrátí model, který není v `Rag:Pricing` · Očekávané chování: NEspadnout, ne počítat jako 0 Kč tiše — `platform.rag.unpriced_usage` counter + WARN log + náklad označen `unpriced`; cost report ukáže „neoceněno: N tokenů" · Mechanismus: lookup miss → flag, ne exception (degradace observability ≠ blok dotazu) · Severity: P1 · Test: integration — neznámý model, assert unpriced counter + report flag.
- **EC-19-03-04 — Cross-tenant leak v cost reportu (tenant-admin vidí cizí náklady)** · Trigger: tenant-admin A dotáže `/rag/admin/cost` bez tenant filtru · Očekávané chování: report scoped na `ITenantContext` tenant; cizí tenant data nikdy nevidí; jen platform-admin přes `admin.` rovinu vidí napříč · Mechanismus: tenant z tokenu (ne z query param), RLS/EF tenant filtr na `rag_usage_daily` · Severity: P0 · Test: integration — tenant A vs B, assert A nevidí B náklady; manipulace query param → ignorováno.
- **EC-19-03-05 — Token usage z degradovaného/selhaného volání (částečně účtováno)** · Trigger: generate selže po spotřebě input tokenů, ale bez output · Očekávané chování: účtuje se to, co provider skutečně vrátil v usage (input ano, output 0); pokud usage chybí → `unavailable` (EC-19-01-05), ne odhad · Mechanismus: jen reálné usage z response · Severity: P1 · Test: integration — generate fail po prvním chunku, assert input účtováno, output 0.
- **EC-19-03-06 — Sazba se změní v polovině měsíce (historická přesnost)** · Trigger: provider zdraží model · Očekávané chování: rollup ukládá náklad spočtený sazbou platnou v době volání (UTC-dated ceník), ne aktuální sazbou zpětně · Mechanismus: ceník versioned/effective-dated; `IClock.UtcNow` určuje, která sazba platí · Severity: P2 · Test: unit — dotaz s timestampem před/po změně sazby, různý náklad.
- **EC-19-03-07 — Duplicitní zápis do `rag_usage_daily` při retry cronu (dvojí náklad)** · Trigger: rollup cron běží 2× / restart uprostřed · Očekávané chování: rollup idempotentní — UNIQUE (tenant, model, day, token_type) + upsert nebo plný recompute za den; opakování nezdvojí · Mechanismus: UNIQUE key + catch `DbUpdateException` (vzor `RegisterUserHandler.cs:22`) nebo deterministický recompute · Severity: P1 · Test: integration — cron 2× za stejný den, assert jeden řádek se správnou sumou.
- **EC-19-03-08 — Náklad počítán z metrik (sampled) místo z přesných spanů (podhodnocení)** · Trigger: trace sampling 10 % → cost z trace dat = 10× míň · Očekávané chování: cost MUSÍ vycházet z NEsamplovaných metrik/counterů (každé volání zaznamenáno), ne ze samplovaných trace · Mechanismus: token counter je metrika (vždy zaznamenáno), nezávislá na trace samplingu · Severity: P0 · Test: integration — sampling 0 %, assert token counter stále plný.

## UC-19-04 — PII redakce PŘED odesláním do tracing/3rd-party
- **Actor / role:** system/worker
- **Precondition:** chunky obsahují `[Encrypted][PersonalData] Content`; dokumenty mohou nést PII; query může obsahovat osobní údaje; OTLP exporter posílá data do externího backendu (Honeycomb/Grafana Cloud/Datadog).
- **Trigger:** každý span/log/metrika před exportem; každé volání 3rd-party providera (OpenAI/Cohere/Anthropic) je samo o sobě únik, takže redakce platí pro tracing payload, ne pro nutný provider payload.
- **Main flow:**
  1. Žádný span atribut, log message ani metric tag NESMÍ obsahovat plný query text, chunk Content, dokument bytes, jména/emaily/osobní data.
  2. Span atributy nesou JEN metadata: délky, počty, modely, latence, skóre, hash/id.
  3. Pokud je potřeba korelovat konkrétní dokument/chunk, nasadí se ID (Guid), ne obsah.
  4. OTel `Processor`/`Enrichment` vrstva navíc filtruje známé citlivé klíče jako defence-in-depth před exportem.
- **Postcondition / záruky:** trace export neobsahuje PII; ani při zapnutém debug logu neuniknou osobní data do 3rd-party APM.
- **Tenancy / permissions:** redakce nezávislá na tenant/scope — platí absolutně; GDPR požadavek.
- **Reuse / canonical pattern:** `[PersonalData]`/`[Encrypted]` model (CLAUDE.md §4 PII); audit IP minimization (`Audit:IpStorage`) jako vzor konfigurovatelné redakce; PII redakce před tracing je nový seam (oblast nemá hotový vzor — drž se konvence).
- **Data dotčená:** žádná (jen export filtr) · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-19-04
- **EC-19-04-01 — Query text nasazen jako span atribut „pro debugging"** · Trigger: vývojář přidá `activity.SetTag("rag.query", queryText)` · Očekávané chování: ZAKÁZÁNO; query může obsahovat PII („najdi smlouvu Jana Nováka, RČ ...") → únik do APM · Mechanismus: konvence + OTel processor blocklist klíčů; review · Severity: P0 · Test: integration — dotaz s PII, scrape exportovaných spanů, assert query text nikde.
- **EC-19-04-02 — Chunk Content v exception message při generate chybě** · Trigger: handler hodí `Exception($"failed on chunk {chunk.Content}")` · Očekávané chování: exception message bez obsahu; jen chunk Id; `activity.AddException` nesmí nést PII · Mechanismus: exception messages jen s ID/kódy; sanitace před `AddException` · Severity: P0 · Test: unit — chyba u chunku, assert message obsahuje Id ne Content.
- **EC-19-04-03 — Decrypted chunk Content unikne do logu na DEBUG úrovni** · Trigger: structured log `LogDebug("retrieved {chunk}", chunk)` serializuje celý objekt vč. dešifrovaného Content · Očekávané chování: log scope vylučuje `[PersonalData]`/`[Encrypted]` properties; custom log serializer redaktuje · Mechanismus: log enrichment/destructuring policy ignoruje označené property · Severity: P0 · Test: integration — DEBUG log capture, assert Content nikde.
- **EC-19-04-04 — Generovaná odpověď (může obsahovat PII z retrievalu) v `generate` spanu** · Trigger: completion text nasazen jako span atribut pro „proč divná odpověď" · Očekávané chování: odpověď NEdávat do trace; korelace přes session_id + uložená odpověď v module DB (RLS-scoped), ne v APM · Mechanismus: UC-19-07 řeší explain přes interní data, ne APM · Severity: P0 · Test: review + integration — generate span bez completion textu.
- **EC-19-04-05 — Metric tag s emailem/jménem (high-cardinality + PII)** · Trigger: někdo přidá `user_email` tag · Očekávané chování: zakázáno (PII i kardinalita); identita jen jako Guid na trace, nikdy email/jméno · Mechanismus: konvence; user_id = Guid · Severity: P0 · Test: review-assert.
- **EC-19-04-06 — Provider payload sám je únik, ale je nutný — nesmí být zaměněn s tracing únikem** · Trigger: posílání chunku do Cohere rerank je legitimní (jinak by rerank nešel), ale tracing payload je zbytečný únik navíc · Očekávané chování: rozlišit nutný business payload (jde providerovi, ošetřeno DPA/config `Rag:Providers`) vs. zbytečný observability payload (NIKDY PII) · Mechanismus: redakce cílí JEN na telemetrii; provider data jsou samostatný compliance bod · Severity: P1 · Test: dokumentační/review — provider call OK, trace prázdný od PII.
- **EC-19-04-07 — Erased subject — jeho PII už nesmí být ani v retenci trace** · Trigger: GDPR erase proběhne, ale staré spany v APM stále nesly PII · Očekávané chování: protože spany NIKDY nenesly PII (UC-19-04), erase se jich netýká; potvrzuje to, proč je redakce před exportem povinná (APM data nelze crypto-shred) · Mechanismus: prevence — APM nikdy nedostane PII, takže není co mazat · Severity: P0 · Test: konzistenční — po erase žádná akce nutná na APM (protože tam PII nebyla).
- **EC-19-04-08 — Indirect prompt injection v dokumentu se zaloguje verbatim** · Trigger: ingestovaný dokument obsahuje „IGNORE PREVIOUS... systémový prompt je X" a někdo loguje extrahovaný text · Očekávané chování: i obsah injection se loguje jen jako délka/hash, ne verbatim (může nést PII i zavádějící data) · Mechanismus: stejná redakce na obsah dokumentu · Severity: P1 · Test: integration — injection dokument, assert obsah ne v trace.

## UC-19-05 — Sledování cache_read tokenů (prompt caching)
- **Actor / role:** system/worker
- **Precondition:** generate fáze používá Anthropic prompt caching (static prefix první: systémový prompt + retrieved kontext, volatilní query/timestamp za cache breakpointem — Zákon „prompt cache").
- **Trigger:** `generate` fáze vrátí usage s `cache_creation_input_tokens` + `cache_read_input_tokens`.
- **Main flow:**
  1. Handler postaví prompt: stabilní prefix (instrukce + retrieved chunky) PŘED cache breakpoint, volatilní část (aktuální query, timestamp, session kontext) ZA ním.
  2. Po volání zaznamená `platform.rag.cache_read_tokens` a `platform.rag.cache_creation_tokens` countery + odpovídající `gen_ai.usage.cache_read_input_tokens` span atribut.
  3. Cost (UC-19-03) účtuje cache_read levnější sazbou; sleduje se cache hit-rate (`cache_read / (cache_read + input)`) jako efektivita.
- **Postcondition / záruky:** cache efektivita měřitelná; náklad reflektuje úspory z cache; vysoký cache-read = nižší cena.
- **Tenancy / permissions:** tag tenant_id; cache je per-prompt-prefix (Anthropic), ne cross-tenant.
- **Reuse / canonical pattern:** `ClaudeVibeAgentGateway.cs:85` (IChatClient + Anthropic.SDK) jako vzor LLM volání; cache breakpoint dle Anthropic SDK; token counter z UC-19-03.
- **Data dotčená:** žádná · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-19-05
- **EC-19-05-01 — Timestamp/query uprostřed cachovaného prefixu (cache invalidace každý dotaz)** · Trigger: prompt má dynamický timestamp PŘED retrieved kontextem → každý dotaz jiný prefix → cache nikdy nezafunguje · Očekávané chování: static prefix první (instrukce + kontext), volatilní (query/timestamp) AŽ za breakpointem; `cache_read` má být >0 u podobných dotazů · Mechanismus: Zákon „prompt cache = static prefix první, volatilní za breakpointem" · Severity: P1 · Test: integration — dva dotazy se stejným kontextem, assert druhý má cache_read >0.
- **EC-19-05-02 — Retrieved kontext se mění mezi dotazy (legitimní cache miss)** · Trigger: jiný retrieval set → jiný prefix → cache miss je správně · Očekávané chování: cache miss u jiného kontextu NENÍ bug; `cache_creation` se zaznamená, hit-rate metrika to odráží · Mechanismus: cache je obsahově závislá; metrika rozlišuje creation vs read · Severity: P2 · Test: integration — různý kontext, assert cache_creation >0, ne chyba.
- **EC-19-05-03 — Cache_read tokeny chybí (provider/SDK je nevrací)** · Trigger: starší SDK / model bez caching · Očekávané chování: graceful — cache metriky 0/nevyplněné, normální input účtování; ne crash · Mechanismus: podmíněné `SetTag` (EC-19-01-05) · Severity: P2 · Test: integration — fake bez cache usage, assert normální chování.
- **EC-19-05-04 — Cache hit-rate počítán jako cache_read/total bez ošetření dělení nulou** · Trigger: dotaz bez žádných tokenů (chyba před generate) · Očekávané chování: hit-rate guard na nulový jmenovatel → NaN se neexportuje; metrika přeskočena · Mechanismus: guard `if total > 0` · Severity: P2 · Test: unit — nula tokenů, assert žádný NaN export.
- **EC-19-05-05 — Cross-session cache leak (cache prefix sdílí PII jiného uživatele)** · Trigger: stabilní prefix obsahuje user-specific PII a cachuje se napříč usery · Očekávané chování: cachovaný prefix obsahuje JEN neosobní instrukce + tenant/user-scoped retrieved kontext téhož usera; cache je per-prefix-content, takže různý user = různý prefix = žádný leak · Mechanismus: retrieved kontext je už RLS-scoped na usera; prefix nikdy nemíchá usery · Severity: P0 · Test: integration — user A a B, assert B nedostane cache_read z A prefixu.

## UC-19-06 — „Kolik nás stojí klient X" — nákladový report
- **Actor / role:** tenant-admin (vlastní tenant) | platform-admin (napříč tenanty)
- **Precondition:** `rag_usage_daily` rollup naplněn (UC-19-03) nebo OTel backend dotazovatelný; ceník v configu.
- **Trigger:** `GET /v1/rag/admin/cost?from=&to=&groupBy=model|day|stage` → `GetRagCostReportQueryHandler`.
- **Main flow:**
  1. Endpoint → `IDispatcher.Query` → handler čte přes `IReadDbContextFactory` z `rag_usage_daily` filtrováno tenantem (z tokenu) + datumovým rozsahem.
  2. Agreguje tokeny × sazby → náklad per model/den/fáze; vrací breakdown + total.
  3. Platform-admin (přes `admin.` rovinu) může `groupBy=tenant` pro cross-tenant přehled.
  4. Vrací `ApiResponse<RagCostReport>` (200).
- **Postcondition / záruky:** read-only; deterministický náklad z persistovaných rollupů; tenant izolace.
- **Tenancy / permissions:** Scope Tenant; `rag.cost.read` (tenant-admin) pro vlastní tenant; cross-tenant jen platform-admin permission `platform.rag.cost.read`.
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12` (read query, IReadDbContextFactory); `.RequirePermission(PlatformPermissions.X)`; ApiResponse envelope.
- **Data dotčená:** `rag_usage_daily` (read) · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-19-06
- **EC-19-06-01 — Datumový rozsah obrácený (from > to) / příliš velký** · Trigger: `from=2026-12-01&to=2026-01-01` nebo rozsah 5 let · Očekávané chování: `ValidationException` (`rag.cost.invalid_range`) přes validátor; max rozsah cap (např. 366 dní) · Mechanismus: `GetRagCostReportValidator` s `.WithErrorCode`; vzor `RegisterUserValidator` · Severity: P2 · Test: unit — obrácený rozsah → 400 s errorCode.
- **EC-19-06-02 — Tenant-admin zkusí `groupBy=tenant` (cross-tenant únik)** · Trigger: tenant-admin pošle `groupBy=tenant` · Očekávané chování: bez platform-admin permission se `groupBy=tenant` odmítne `ForbiddenException` nebo se silně scope-nuje na vlastní tenant (jediný řádek) · Mechanismus: permission check + tenant z tokenu · Severity: P0 · Test: integration — tenant-admin groupBy=tenant → 403/jen vlastní.
- **EC-19-06-03 — Report za období bez dat (žádný rollup)** · Trigger: nový tenant, žádné dotazy · Očekávané chování: prázdný report (total 0, empty breakdown), 200 — ne 404, ne chyba · Mechanismus: prázdná agregace = legitimní výsledek · Severity: P2 · Test: integration — nový tenant, assert 200 + total 0.
- **EC-19-06-04 — Report obsahuje neoceněné tokeny (unpriced model)** · Trigger: část tokenů z modelu bez ceníku (EC-19-03-03) · Očekávané chování: report ukáže oceněnou část + separátně „neoceněno: N tokenů (M dotazů)" — nesmí tiše vykázat nižší total · Mechanismus: unpriced bucket v reportu · Severity: P1 · Test: integration — smíšený oceněný/neoceněný, assert oba bloky.
- **EC-19-06-05 — Náklad z dnešního (neuzavřeného) dne — částečný rollup** · Trigger: dotaz včetně `to=dnes`, ale rollup cron poběží až v noci · Očekávané chování: buď live agregace z metrik pro neuzavřený den, nebo jasný flag „dnešek = předběžné" — ne tichá nula za dnešek · Mechanismus: rollup pro uzavřené dny + live dopočet pro dnešek (označený `partial=true`) · Severity: P2 · Test: integration — dotaz vč. dneška, assert partial flag.
- **EC-19-06-06 — Velký report (366 dní × 4 modely × 6 fází) — výkon/payload** · Trigger: maximální rozsah s jemnou granularitou · Očekávané chování: paginace/agregace na serveru; rozumný cap na velikost odpovědi; ne neomezený JSON · Mechanismus: server-side agregace, default groupBy hrubší · Severity: P2 · Test: integration — max rozsah, assert ohraničený response.

## UC-19-07 — „Proč klientovi Y vyšla divná odpověď" — diagnostika dotazu
- **Actor / role:** tenant-admin | platform-admin
- **Precondition:** každý RAG dotaz persistuje diagnostický záznam `rag_query_trace` (RLS-scoped, `IUserOwned`/tenant): session_id, trace_id, použité chunk Ids + skóre, graf cesty (node Ids), rerank pořadí, model, latence per fáze, degraded flag, citace; SAMOTNÝ obsah/odpověď jen pokud retence povolena a vždy přes RLS.
- **Trigger:** `GET /v1/rag/admin/queries/{queryId}/explain` → `ExplainRagQueryHandler`.
- **Main flow:**
  1. Admin najde dotaz dle session/queryId → handler čte `rag_query_trace` (RLS: cizí id → 404).
  2. Vrací: jaký rewrite proběhl, jaké chunky byly retrievovány s vektor/BM25/RRF skóre, jak graf rozšířil, jak rerank přeřadil, jaké citace odpověď nesla, byl-li degraded a proč (provider down / rerank skip / zero-retrieval fallback), trace_id pro APM korelaci.
  3. Umožní pochopit „divnou odpověď": špatné retrieval kandidáty, chybějící citaci, degradaci, supernode v grafu.
- **Postcondition / záruky:** read-only; plná rekonstrukce rozhodnutí pipeline BEZ úniku PII do APM (PII zůstává v RLS-scoped module DB).
- **Tenancy / permissions:** Scope Tenant/User; `rag.query.explain` permission; RLS — admin vidí jen vlastní tenant, platform-admin přes `admin.` rovinu.
- **Reuse / canonical pattern:** `GetOperationStatusEndpoint` (RLS-scoped, cizí id → 404); `StartDemoOperationHandler.cs:17` pro status vzor; read přes `IReadDbContextFactory`.
- **Data dotčená:** `rag_query_trace` · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-19-07
- **EC-19-07-01 — Explain cizího dotazu (IDOR)** · Trigger: admin A žádá queryId tenanta B · Očekávané chování: RLS → 404 (ne 403, neprozradit existenci) · Mechanismus: `rag_query_trace` tenant/user-scoped, RLS keyed `app.principal_id`/tenant filtr; vzor `GetOperationStatusEndpoint` · Severity: P0 · Test: integration — cross-tenant explain → 404.
- **EC-19-07-02 — Diagnostický záznam obsahuje plný query text / odpověď (PII retence)** · Trigger: persistence celé odpovědi vč. PII · Očekávané chování: pokud se obsah uchovává, je `[Encrypted]`/RLS-scoped + podléhá GDPR erase; defaultně se ukládají Ids+skóre+metadata, plný text jen pokud `Rag:Diagnostics:RetainContent=true` a vždy šifrovaně · Mechanismus: `[Encrypted][PersonalData]` na content poli; GDPR eraser maže/anonymizuje · Severity: P0 · Test: integration — explain po erase, assert content `[erased]`, metadata zůstávají.
- **EC-19-07-03 — Dotaz starší než retence (záznam smazán/expirován)** · Trigger: explain dotazu z doby mimo retenci · Očekávané chování: 404 nebo „diagnostika vypršela" — ne crash; retence diagnostik je TTL-bounded (jako durable envelope bound) · Mechanismus: retention sweep job pro `rag_query_trace`; vzor `GdprRetentionSweepJob` seam · Severity: P2 · Test: integration — expirovaný záznam → 404.
- **EC-19-07-04 — Degraded dotaz bez uvedení důvodu (tichá půlka)** · Trigger: dotaz degradoval, ale explain neukáže proč · Očekávané chování: explain MUSÍ ukázat degradation reason (rerank_skipped/provider_down/zero_retrieval) — Zákon „nikdy tichá půlka" · Mechanismus: degraded flag + reason enum persistován do trace · Severity: P0 · Test: integration — vynucená degradace, assert explain nese reason.
- **EC-19-07-05 — Citace v odpovědi neodpovídají retrievovaným chunkům (halucinace)** · Trigger: odpověď cituje chunk, který nebyl v retrieval setu · Očekávané chování: explain umožní odhalit citation-missing/mismatch — záznam drží jak retrieved chunky, tak citace, takže nesoulad je vidět · Mechanismus: persistovat oba sety; citation guard z generate fáze · Severity: P1 · Test: integration — assert explain odhalí citaci mimo retrieval set.
- **EC-19-07-06 — Trace_id v záznamu, ale APM ho už nemá (sampling/retence APM)** · Trigger: dotaz nebyl samplován do APM · Očekávané chování: explain funguje i bez APM (interní diagnostika je kompletní zdroj); trace_id je bonus pro deep-dive, ne závislost · Mechanismus: `rag_query_trace` je self-contained, APM je doplněk · Severity: P2 · Test: integration — sampling 0 %, assert explain stále plný.
- **EC-19-07-07 — Supernode/over-merge způsobil divnou odpověď** · Trigger: graf expand zatáhl supernode (entita s tisíci hran) → irelevantní kontext · Očekávané chování: explain ukáže graf cestu + degradation flag pokud byl supernode capnut/přeskočen · Mechanismus: persistovat graph traversal + supernode-cap flag · Severity: P2 · Test: integration — supernode dotaz, assert explain ukáže cap.

## UC-19-08 — Observability grácní degradace (Degraded/Partial counter + flag)
- **Actor / role:** system/worker
- **Precondition:** pipeline fáze mohou selhat (provider down, 429, rerank timeout, zero-retrieval); Zákon „graceful degradation = NIKDY tichá půlka, explicit Partial/Degraded flag".
- **Trigger:** kterákoli fáze degraduje místo selhání celého dotazu.
- **Main flow:**
  1. Fáze selže recoverable způsobem (rerank provider down → skip rerank, použít vektor pořadí; embed down → BM25-only; graf timeout → vektor-only).
  2. Handler nastaví `response.Degraded=true` + `DegradationReason`, zvýší `platform.rag.degraded` counter s tagem `rag.degradation.reason`, span dostane `rag.degraded=true`.
  3. Odpověď se vrátí s explicitním flagem (klient/UI ví, že je částečná), trace + metrika to zachytí.
- **Postcondition / záruky:** degradace nikdy tichá; měřitelná per reason; uživatel informován.
- **Tenancy / permissions:** beze změny scope; degradace nezhoršuje izolaci.
- **Reuse / canonical pattern:** `platform.rag.degraded` counter (`PlatformMetrics.cs:19`); explicit flag vzor (Zákon graceful degradation); messaging retry/DLQ pro durable části.
- **Data dotčená:** žádná (volitelně degraded flag do `rag_query_trace`) · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-19-08
- **EC-19-08-01 — Rerank provider down → tichý fallback bez flagu** · Trigger: Cohere 503, handler přeskočí rerank, ale nenastaví Degraded · Očekávané chování: `Degraded=true` + reason `rerank_unavailable` + counter; UI/klient ví, že pořadí je jen vektor · Mechanismus: explicit flag, Zákon · Severity: P0 · Test: integration — Cohere down, assert flag+reason+counter.
- **EC-19-08-02 — Zero-retrieval / low-similarity, ale model halucinuje odpověď** · Trigger: žádný kandidát nad prahem similarity · Očekávané chování: `Degraded=true` reason `zero_retrieval`; odpověď buď „nenalezeno" nebo jasně označená low-confidence; counter `platform.rag.degraded{reason=zero_retrieval}` · Mechanismus: similarity threshold guard + flag · Severity: P1 · Test: integration — dotaz mimo korpus, assert degraded + ne halucinace bez flagu.
- **EC-19-08-03 — Embed provider down → BM25-only, ale metrika to nezaznamená** · Trigger: OpenAI embeddings 503 · Očekávané chování: degraded counter{reason=embed_unavailable} + dotaz běží lexikálně; explain to ukáže · Mechanismus: counter + fallback · Severity: P1 · Test: integration — embed down, assert BM25-only + counter.
- **EC-19-08-04 — Degraded counter inkrementován vícekrát pro jeden dotaz (dvě fáze degradovaly)** · Trigger: embed i rerank oba down · Očekávané chování: counter zvýšen per reason (dva reasony = dva inkrementy s různým tagem) NEBO jednou s primárním reasonem — definovaná sémantika; explain drží všechny reasony · Mechanismus: konzistentní pravidlo (doporučeno: counter per reason, dotaz počítán 1× v `queries`) · Severity: P2 · Test: integration — dvě degradace, assert definované počítání.
- **EC-19-08-05 — Degradace u durable ingest (ne query) — saga musí pokračovat nebo selhat hlasitě** · Trigger: embed během ingest selže · Očekávané chování: ingest NEdegraduje tiše na „dokument bez embeddingů" — buď retry (Wolverine DLQ) nebo `IngestSaga` přejde do Failed se stavem; nikdy „indexováno" bez vektorů · Mechanismus: saga terminal-state guard, `CreditPurchaseSaga.cs:30` vzor; messaging retry/DLQ · Severity: P0 · Test: integration — embed fail při ingest, assert saga Failed ne tichý částečný index.
- **EC-19-08-06 — Degraded flag chybí na streaming odpovědi (už odeslán první chunk)** · Trigger: degradace nastane po začátku streamu · Očekávané chování: degraded signál se pošle jako SSE metadata event (`event: degraded`) i uprostřed streamu; ne ztracen · Mechanismus: SSE delta/done + degraded event, vzor `StreamMessageEndpoint.cs:34` · Severity: P1 · Test: integration — mid-stream degradace, assert degraded SSE event.

## UC-19-09 — tenant_id / user_id / session_id korelace na každém trace
- **Actor / role:** system/worker
- **Precondition:** `ITenantContext` poskytuje tenant + user z tokenu; každý RAG dotaz/konverzace má session_id (generovaný nebo z requestu).
- **Trigger:** start každého kořenového RAG spanu.
- **Main flow:**
  1. `TelemetryBehavior`/RAG instrumentace nasadí na kořenový span baggage/atributy: `tenant.id`, `enduser.id` (Guid), `session.id`.
  2. Baggage se propaguje do všech child spanů + přes Wolverine envelope do worker spanů (EC-19-01-03).
  3. Logy v rámci dotazu nesou stejné korelační id (log scope).
- **Postcondition / záruky:** každý span/log dohledatelný dle tenant/user/session; umožňuje UC-19-06/07.
- **Tenancy / permissions:** id z tokenu (NIKDY z route/body/LLM argumentu — Zákon); user_id jako Guid (ne email, PII).
- **Reuse / canonical pattern:** `ITenantContext.UserId`; OTel baggage propagace; Wolverine trace context propagace.
- **Data dotčená:** žádná · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-19-09
- **EC-19-09-01 — tenant_id vzat z request body/LLM argumentu místo tokenu** · Trigger: MCP tool nebo request nese `tenant_id` parametr · Očekávané chování: VŽDY z `ITenantContext` (token); argument ignorován — jinak cross-tenant leak/spoof · Mechanismus: Zákon „tenant id z tokenu"; MCP trust-boundary · Severity: P0 · Test: integration — body s cizím tenant_id, assert trace nese token tenant.
- **EC-19-09-02 — Background worker handler bez HttpContext (tenant = SYSTEM)** · Trigger: durable generate ve workeru, žádný HttpContext · Očekávané chování: tenant/user se propaguje přes Wolverine envelope (baggage), ne z `HttpTenantContext`; worker span nese původní tenant, ne SYSTEM · Mechanismus: baggage v message headers; `SystemTenantContext` jen pro skutečně systémové joby · Severity: P0 · Test: integration — durable ask, assert worker span má původní tenant_id.
- **EC-19-09-03 — session_id chybí (první dotaz / stateless)** · Trigger: request bez session · Očekávané chování: server vygeneruje `session.id` (Guid v7) a vrátí klientovi; trace ho nese; ne null/prázdný · Mechanismus: server-generated session jako correlation · Severity: P2 · Test: integration — bez session, assert vygenerovaný + vrácený.
- **EC-19-09-04 — user_id = email/jméno na spanu (PII)** · Trigger: nasazení `enduser.id = email` · Očekávané chování: jen Guid; email nikdy (PII + UC-19-04) · Mechanismus: konvence; user_id = `ITenantContext.UserId` (Guid) · Severity: P0 · Test: review-assert.
- **EC-19-09-05 — session_id spoofnutý napříč usery (correlation hijack)** · Trigger: klient pošle cizí session_id · Očekávané chování: session_id je korelační, ne autorizační — autorizace VŽDY z tokenu (tenant/user); session jen seskupuje trace, nikdy nezpřístupní cizí data · Mechanismus: session ≠ auth; data scope z tokenu · Severity: P1 · Test: integration — cizí session_id, assert žádný přístup k cizím datům.
- **EC-19-09-06 — Anonymní/pre-auth dotaz (login flow) — žádný user_id** · Trigger: RAG dostupný anonymně (pokud konfigurováno) · Očekávané chování: trace nese tenant (pokud znám) + `enduser.id` prázdný/`anonymous`, ne falešný Guid; metriky tagged `anonymous` low-cardinality · Mechanismus: definovaná hodnota pro anonymní · Severity: P2 · Test: integration — anon dotaz, assert trace bez user Guid.

## UC-19-10 — Per-tenant cost budget alert (cron)
- **Actor / role:** system (Jobs host) → notifikace tenant-admin
- **Precondition:** tenant má volitelný měsíční budget `Rag:Budget:{tenant}:MonthlyCapUsd` nebo per-tenant entitlement; `rag_usage_daily` rollup naplněn; Jobs host běží.
- **Trigger:** cron `Modules:Rag:Jobs:BudgetCheckCron` → `RagBudgetCheckJob` → dispatch `CheckRagBudgetCommand`.
- **Main flow:**
  1. Job dispatchne command (job je tenký, logika v handleru — CLAUDE.md cron pravidlo).
  2. Handler agreguje měsíční náklad per tenant z `rag_usage_daily`, porovná s budgetem.
  3. Při překročení prahu (80 %/100 %) publikuje `RagBudgetThresholdReachedIntegrationEvent` přes outbox → Notifications modul pošle in-app/email tenant-adminovi; emituje `platform.rag.budget_exceeded` counter.
  4. Volitelně: při 100 % cap přepne tenant do degraded/read-only RAG módu (config `EnforceCap`).
- **Postcondition / záruky:** překročení budgetu nahlášeno; idempotentní (jeden alert per práh per období); event přes outbox.
- **Tenancy / permissions:** systémový job (cross-tenant agregace legitimně jako SYSTEM); notifikace cílí tenant-admina.
- **Reuse / canonical pattern:** `BillingExpireCreditsJob` (cron job → dispatch command); outbox publish `RegisterUserHandler.cs:22`; pure publisher job (Jobs host).
- **Data dotčená:** `rag_usage_daily` (read), volitelně `rag_budget_alert_state` (dedup prahů) · **Eventy:** `RagBudgetThresholdReachedIntegrationEvent`
- **Priorita:** P2

### Edge cases UC-19-10
- **EC-19-10-01 — Duplicitní alert při každém běhu cronu (spam)** · Trigger: cron běží hodinově, budget stále překročen · Očekávané chování: alert per (tenant, období, práh) jen JEDNOU — dedup stav; opakovaný běh nealertuje znovu · Mechanismus: UNIQUE (tenant, period, threshold) + catch `DbUpdateException`; idempotence · Severity: P1 · Test: integration — cron 3×, assert jeden event.
- **EC-19-10-02 — Logika v jobu místo handleru** · Trigger: agregace + porovnání přímo v `IJob` · Očekávané chování: job jen `dispatcher.Send(CheckRagBudgetCommand)`; logika v handleru (CLAUDE.md cron zákon) · Mechanismus: tenký IJob vzor `BillingExpireCreditsJob` · Severity: P1 · Test: arch — job nemá business logiku.
- **EC-19-10-03 — Reset prahů na začátku nového měsíce** · Trigger: nový měsíc, budget se obnoví · Očekávané chování: dedup stav je per-období → nový měsíc = nová okna, alerty znovu možné · Mechanismus: období = (tenant, YYYY-MM) v dedup klíči; UTC měsíc dle `IClock` · Severity: P2 · Test: integration — přechod měsíce, assert nový alert možný.
- **EC-19-10-04 — Tenant bez budgetu (žádný cap)** · Trigger: tenant nemá `MonthlyCapUsd` · Očekávané chování: žádný alert, ne crash; budget check přeskočí tenanty bez capu · Mechanismus: null cap → skip · Severity: P2 · Test: integration — tenant bez capu, assert žádný event.
- **EC-19-10-05 — Job běží v více Jobs instancích (Quartz) — dvojí spuštění** · Trigger: multi-instance Jobs host · Očekávané chování: Quartz clustering/job dedup nebo idempotentní handler zajistí jeden efektivní alert · Mechanismus: idempotence (EC-19-10-01) je primární obrana; Quartz misfire policy · Severity: P1 · Test: integration — dva běhy paralelně, assert jeden alert.
- **EC-19-10-06 — `EnforceCap` přepne tenant do read-only uprostřed aktivních dotazů** · Trigger: cap dosažen během běžících dotazů · Očekávané chování: probíhající dotazy dokončí; nové dotazy degradují/odmítnou s jasnou chybou `rag.budget_exceeded` (ne tichý fail); reason v explain · Mechanismus: cap check na vstupu nového dotazu + degraded flag · Severity: P2 · Test: integration — cap při enforce, assert nový dotaz 402/degraded, běžící doběhne.
- **EC-19-10-07 — Náklad za neuzavřený den nezapočten → alert pozdě** · Trigger: prudký dnešní nárůst, ale rollup jen do včerejška · Očekávané chování: budget check zahrne live dnešní dopočet (EC-19-06-05), aby alert nepřišel s 24h zpožděním · Mechanismus: rollup + live dnešní metriky · Severity: P2 · Test: integration — velký dnešní spike, assert alert dnes.

## UC-19-11 — execute_tool spany pro agentic/MCP a graf nástroje
- **Actor / role:** system/worker | MCP klient
- **Precondition:** RAG má agentic režim (`ClaudeVibeAgentGateway` vzor) s user-scoped tools (graf dotaz, retrieval, citace) a/nebo MCP server expozici; tool loop dle GenAI semconv.
- **Trigger:** LLM tool-call uvnitř agentic turnu / MCP tool invocation.
- **Main flow:**
  1. Agentic turn (`RunVibeAgentTurnHandler` vzor) otevře `chat` span; každý tool-call → child span `execute_tool` s `gen_ai.operation.name=execute_tool`, `gen_ai.tool.name`, `gen_ai.tool.call.id`.
  2. Tool span nese argumenty jen jako neosobní metadata (počet, typ), VÝSLEDEK jen velikost/počet (ne obsah — PII).
  3. Latence + úspěch/chyba tool callu měřeno; `platform.rag.tool_calls` counter s tagem `tool.name`.
- **Postcondition / záruky:** každý tool-call viditelný v trace; tool loop diagnostikovatelný; bez PII.
- **Tenancy / permissions:** tool scope VŽDY z `ITenantContext` (ne z LLM argumentu — MCP trust-boundary); user-scoped tools jako `VibeAgentTools`.
- **Reuse / canonical pattern:** `ClaudeVibeAgentGateway.cs:149,155` (user-scoped tools), `ProcessVibeTurnCommand.cs:84` (durable turn + realtime AFTER commit); `execute_tool` GenAI semconv.
- **Data dotčená:** žádná (jen trace) · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-19-11
- **EC-19-11-01 — Tenant/user z tool argumentu (MCP trust-boundary leak)** · Trigger: LLM/MCP klient pošle `tenant_id`/`user_id` jako tool argument · Očekávané chování: IGNOROVAT; scope z `ITenantContext`; trace zaznamená skutečný (token) tenant, ne argument · Mechanismus: Zákon „tenant z tokenu, ne z LLM arg"; tool resolver bere identitu ze serveru · Severity: P0 · Test: integration — tool s podvrženým tenant arg, assert přístup jen k token tenant.
- **EC-19-11-02 — Tool argumenty (mohou nést PII/query) nasazeny verbatim na span** · Trigger: `gen_ai.tool.call.arguments` = celý JSON s osobními daty · Očekávané chování: jen metadata (názvy parametrů, počty), ne hodnoty s PII · Mechanismus: redakce UC-19-04 · Severity: P0 · Test: integration — tool s PII argem, assert span bez hodnoty.
- **EC-19-11-03 — Nekonečná tool loop (model volá tool stále dokola)** · Trigger: model nekonverguje · Očekávané chování: cap na počet tool iterací; `platform.rag.tool_loop_capped` counter + degraded; trace ukáže počet iterací · Mechanismus: max-iterations guard v tool loopu; degraded flag · Severity: P1 · Test: integration — fake model loopuje, assert cap + counter.
- **EC-19-11-04 — Tool call selže (graf query timeout) uprostřed turnu** · Trigger: graf LINQ join timeout · Očekávané chování: `execute_tool` span Error + tool vrátí modelu chybu (ne crash celého turnu); model může degradovat odpověď · Mechanismus: tool error handling, span status · Severity: P1 · Test: integration — graf timeout tool, assert span Error + turn dokončí.
- **EC-19-11-05 — Tool span chybí (tool volán mimo instrumentaci)** · Trigger: nějaký tool nemá span · Očekávané chování: VŠECHNY tool cally instrumentovány jednotně; chybějící span = mezera v cost/diagnostice · Mechanismus: centrální tool-call wrapper otevírá span · Severity: P2 · Test: integration — každý tool má span.
- **EC-19-11-06 — Tool výsledek (retrieved chunky) nasazen do span jako obsah** · Trigger: tool vrátí chunky a ty se logují · Očekávané chování: jen počet + Ids; obsah nikdy · Mechanismus: redakce · Severity: P0 · Test: review-assert.

## UC-19-12 — Provider latency, error a rate-limit metriky (429/down)
- **Actor / role:** system/worker
- **Precondition:** RAG volá OpenAI (embed), Cohere (rerank), Anthropic (chat); každý může vrátit 429/5xx/timeout; retry s `Retry-After`.
- **Trigger:** každé HTTP volání providera.
- **Main flow:**
  1. Per provider volání: `platform.rag.provider_latency` histogram (tag `gen_ai.system`, `gen_ai.request.model`, `http.response.status_code`), `platform.rag.provider_errors` counter (tag `error.type`: `rate_limit`/`timeout`/`server_error`/`auth`), `platform.rag.provider_retries` counter.
  2. 429 → zaznamená `Retry-After`, retry s backoffem; span má `error.type=rate_limit`.
  3. Provider down → degradace (UC-19-08) + error counter.
- **Postcondition / záruky:** zdraví providerů měřitelné; 429/down viditelné; umožní kapacitní plánování + alert.
- **Tenancy / permissions:** tag tenant_id (kdo generuje provider zátěž); low-cardinality.
- **Reuse / canonical pattern:** `platform.rag.*` Meter; retry/backoff battle-tested (Polly) ne vlastní smyčka; `MessagingHealthJob` gauge vzor pro health.
- **Data dotčená:** žádná · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-19-12
- **EC-19-12-01 — 429 retry-after ignorován (retry storm)** · Trigger: Cohere 429 s `Retry-After: 30` · Očekávané chování: respektovat `Retry-After`; ne okamžitý retry; `provider_retries` counter; backoff přes Polly · Mechanismus: battle-tested retry (Polly), čtení `Retry-After` · Severity: P1 · Test: integration — fake 429+Retry-After, assert backoff dodržen.
- **EC-19-12-02 — Retry čas zkresluje fázovou latenci (EC-19-02-05 související)** · Trigger: 3 retry × 30s započteno do retrieval_latency · Očekávané chování: separátní `provider_latency` (per attempt) vs. user-perceived fáze latence; retry viditelný samostatně · Mechanismus: dva instrumenty · Severity: P2 · Test: integration — retry, assert oba histogramy.
- **EC-19-12-03 — Provider auth chyba (401, špatný klíč) maskovaná jako degradace** · Trigger: neplatný API klíč · Očekávané chování: 401 = konfigurace/secret problém, ne přechodná degradace; error counter `error.type=auth` + WARN/ERROR log + fail-fast/alert, ne tiché BM25-only navždy · Mechanismus: rozlišit recoverable (429/5xx) vs. non-recoverable (401/403); auth → alert · Severity: P1 · Test: integration — 401, assert auth error counter + ne tichá trvalá degradace.
- **EC-19-12-04 — Timeout vs. server-error nerozlišeny** · Trigger: provider visí (timeout) vs. vrací 500 · Očekávané chování: různý `error.type` tag (`timeout` vs `server_error`) pro správnou diagnostiku · Mechanismus: kategorie chyb · Severity: P2 · Test: unit — timeout vs 500, různý tag.
- **EC-19-12-05 — Status code tag = vysoká kardinalita (každý unikátní kód)** · Trigger: tag `http.response.status_code` jako string s detaily · Očekávané chování: jen standardní kódy (200/429/500/503/timeout); ne unikátní message · Mechanismus: bucketing statusů · Severity: P2 · Test: review-assert.
- **EC-19-12-06 — Fake gateway (`Rag:UseFakeGateways`) nezaznamenává provider metriky (testy nereprezentativní)** · Trigger: testy běží na fake · Očekávané chování: fake gateway emituje stejné metriky (s `gen_ai.system=fake`) aby instrumentace byla testovatelná; nebo testy explicitně ověří instrumentaci na fake · Mechanismus: fake pod flagem `MarketingModule.cs:51` vzor; instrumentace na seam, ne v gateway · Severity: P2 · Test: integration — fake dotaz, assert metriky emitovány.
- **EC-19-12-07 — Provider-down trvale → každý dotaz degraduje, ale žádný agregátní alert** · Trigger: OpenAI výpadek 1h · Očekávané chování: `provider_errors` counter + degraded counter rostou → infrastrukturní alert (mimo modul) na základě metrik; modul jen emituje, nealertuje sám (alerting = infrastruktura, jako `MessagingHealthJob`) · Mechanismus: metriky → externí alert pravidlo · Severity: P2 · Test: integration — opakované down, assert counter roste konzistentně.
- **EC-19-12-08 — Embedding dimension/model drift detekce z metrik** · Trigger: OpenAI vrátí jiný počet dimenzí / model než očekáváno (3072) · Očekávané chování: `platform.rag.embed_dimension_mismatch` counter + ingest/query fail-fast (nesmí uložit nekompatibilní vektor) · Mechanismus: dimension guard při embed; metrika · Severity: P1 · Test: integration — fake vrátí 1536 dim, assert mismatch counter + odmítnutí.


---

## Doplňky z completeness review
- **EC-19-04-09 — Auto Npgsql/EF SQL instrumentace zachytí `db.statement` s uživatelským dotazem (PII) — zvlášť BM25 raw-SQL carve-out** · Trigger: OTel `AddNpgsql()`/EF command instrumentace automaticky nasadí `db.statement`/`db.query.parameters`; lexikální retrieval používá raw-SQL carve-out (`FromSqlInterpolated`) s uživatelským dotazem jako parametrem → text dotazu (potenciální PII „smlouva Jana Nováka") odejde do APM MIMO manuální span redakci (UC-19-04, která cílí jen na `SetTag`). · Očekávané chování: SQL instrumentace NESMÍ zachytávat hodnoty parametrů ani plný statement s vyhledávacím textem — jen parametrizovaný tvar bez hodnot, nebo db instrumentace pro RAG dotazy filtrovaná processorem. · Mechanismus: `SetDbStatementForText=false`/bez parameter capture na Npgsql instrumentaci + processor blocklist; redakce UC-19-04 rozšířená na AUTO-instrumentaci, ne jen ruční `SetTag` · Severity: P0 · Test: integrační — dotaz s PII přes BM25 carve-out, scrape spanů, assert text dotazu ani parametr nikde v `db.*` atributech.
