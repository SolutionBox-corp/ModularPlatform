# Oblast 24 — Configuration, tuning & parameter registry

> Tato oblast je **řídicím nervovým systémem** modulu `HybridRag`. Cílem je, aby **každý** parametr RAG pipeline (velikost chunku, topK retrievalu, počet hopů grafu, rerank on/off, model embeddingu, …) byl **konfigurovatelný, laditelný, trasovatelný a auditovatelný** — od globálního defaultu přes per-tenant a per-collection override až po bezpečný per-query override z allowlistu. Jádrem je **kanonický parameter registry** (UC-24-01), nad ním stojí **scope-hierarchie / effective-config resolution** (UC-24-02) a **trasování effective configu** ke každé odpovědi (UC-24-12), aby šlo odpovědět na otázku „proč tahle odpověď vypadá takhle".
>
> Frozen invarianty: konfigurační namespace tunable knobů = `Rag:*` (bind do `RagTuningOptions` a podřízených Options tříd v rámci sekce `Modules:HybridRag`); feature flag modulu = `Modules:HybridRag:Enabled`; jobs cron = `Modules:HybridRag:Jobs:*`; route `/v1/hybridrag/...`; tabulky `hybridrag_*`; `HybridRagDbContext`. Identita VŽDY z tokenu (`ITenantContext.UserId`), NIKDY z body/LLM. Vše UTC (`IClock`). Errors = throw `ModularPlatformException` subclass + errorCode do `SharedResource.resx` (en+cs). Per-tenant/collection override = DB entita `RagSetting` (`ITenantScoped`, audit přes `AuditInterceptor`). Secrets (`OpenAi:ApiKey`, `Rerank:Cohere:ApiKey`) NIKDY v tunable registru ani v effective-config výstupu.

---

## UC-24-01 — Kanonický parameter registry (zdroj pravdy o všech knobech)

- **Actor / role:** platform-admin (čtenář dokumentace + runtime registru) · system (registr je deklarativní singleton načtený při startu)
- **Precondition:** Modul `HybridRag` enabled. Registr je **kód-first deklarace** (`RagParameterRegistry` — statická readonly mapa `RagParameterDescriptor[]`), nikoli DB tabulka; je jediným místem, kde je každý knob popsán (canonical key, scope, default, runtime-changeable vs restart-required, validační rozsah, trasovaný ano/ne, allowlist-for-query ano/ne, je-secret ano/ne).
- **Trigger:** interní (registr se konzultuje při: validaci PUT override UC-24-04/05, výpočtu effective-config UC-24-02, allowlist-checku per-query UC-24-07, maskování secrets UC-24-14, trasování UC-24-12) + `GET /v1/hybridrag/admin/config/registry` (read-only katalog pro UI ladění).
- **Main flow:**
  1. Při startu hostu se `RagParameterRegistry` materializuje a **provaliduje sám sebe** (každý descriptor má neprázdný canonical key, validní scope-set, default uvnitř vlastního rozsahu, konzistentní příznaky — viz EC-24-01-03).
  2. `RagParameterRegistry.All` je vystaven přes `GET /v1/hybridrag/admin/config/registry` (permission `Rag.Manage` / `PlatformPermissions.RagManage`) — vrací plný katalog (bez secrets) jako `ApiResponse<RagParameterDescriptor[]>`.
  3. Každá další služba (validátor PUT, effective-config resolver, per-query merge) konzultuje registr jako **single source of truth** — žádná z nich nemá vlastní hardcoded seznam knobů.
- **Postcondition / záruky:** Existuje právě jeden autoritativní seznam knobů; přidání knobu = přidání descriptoru (mechanicky vynuceno testem, který páruje registr ↔ Options properties, viz Test). Drift mezi názvy klíčů je vyřešen (UC-24-16) a registr drží **jen kanonické klíče**.
- **Tenancy / permissions:** Katalog registru je **platform-global** (ne tenant data) → endpoint vyžaduje `Rag.Manage`; nevrací žádné tenant-specifické hodnoty, jen metadata + globální default. RLS se neuplatní (statická data).
- **Reuse / canonical pattern:** read endpoint dle `GetProfileHandler.cs:12` (`IReadDbContextFactory` zde nepotřeba — čte z paměti); Options binding dle `JwtOptionsValidator`; descriptor jako `sealed record`. · **Data dotčena:** žádná tabulka (in-memory) · **Eventy:** — · **Priorita:** P0

### Kanonický registr (frozen — drift vyřešen, viz UC-24-16)

| Kanonický config key | Scope | Default | Runtime vs Restart | Validační rozsah | Trasovaný |
|---|---|---|---|---|---|
| `Rag:Chunk:Size` | Global·Tenant·Collection | 512 | Runtime (nová ingest) | int 64–4096; ≤ embedding context | ano |
| `Rag:Chunk:Overlap` | Global·Tenant·Collection | 64 | Runtime (nová ingest) | int 0–`Size`/2 | ano |
| `Rag:Chunk:MaxChunksPerDocument` | Global·Tenant·Collection | 5000 | Runtime | int 1–100000 | ano |
| `Rag:Contextualize:Required` | Global·Tenant·Collection | false | Runtime (nová ingest) | bool | ano |
| `Rag:Embedding:Model` | Global·Tenant·Collection | `text-embedding-3-large` | **Restart (provider klient)** + vynutí reembed | enum z `Rag:Embedding:AllowedModels` | ano |
| `Rag:Embedding:Dimensions` | Global·Tenant·Collection | 1536 | **Restart** (mění pgvector kolony) | int z fixní množiny {256,512,768,1024,1536,3072} | ano |
| `Rag:Embedding:BatchSize` | Global·Tenant | 96 | Runtime | int 1–2048 | ne |
| `Rag:Embedding:AllowedModels` | Global | `[text-embedding-3-large,text-embedding-3-small]` | **Restart** | neprázdné pole známých modelů | ne |
| `Rag:Dense:MaxTopK` | Global·Tenant | 200 | Runtime | int 1–1000 (platform cap) | ano |
| `Rag:Dense:EfSearch` | Global·Tenant·Collection | 100 | Runtime | int ≥ topK, 1–2000 | ano |
| `Rag:Dense:MinSimilarity` | Global·Tenant·Collection·**Query** | 0.20 | Runtime | float 0.0–1.0 | ano |
| `Rag:Dense:TruncateQuery` | Global·Tenant | true | Runtime | bool | ne |
| `Rag:Lexical:Provider` | Global·Tenant | `pg_search` | **Restart** (extension) | enum {`pg_search`,`ts_rank`} | ano |
| `Rag:Lexical:Bm25:K1` | Global·Tenant·Collection | 1.2 | Runtime | float 0.0–3.0 | ano |
| `Rag:Lexical:Bm25:B` | Global·Tenant·Collection | 0.75 | Runtime | float 0.0–1.0 | ano |
| `Rag:Lexical:DefaultLanguage` | Global·Tenant·Collection | `english` | Runtime | enum PG text-search configs | ano |
| `Rag:Lexical:MaxQueryChars` | Global·Tenant | 1024 | Runtime | int 16–8192 | ne |
| `Rag:Lexical:SlaWarnMs` | Global·Tenant | 250 | Runtime | int 1–10000 | ne |
| `Rag:Lexical:StatementTimeoutMs` | Global·Tenant | 2000 | Runtime | int 50–30000 | ne |
| `Rag:Fusion:Rrf:K` | Global·Tenant·Collection·**Query** | 60 | Runtime | int 1–1000 | ano |
| `Rag:Fusion:CandidateK` | Global·Tenant·Collection·**Query** | 100 | Runtime | int 1–`Dense:MaxTopK` | ano |
| `Rag:Fusion:TopN` | Global·Tenant·Collection·**Query** | 10 | Runtime | int 1–`CandidateK` | ano |
| `Rag:Fusion:MinScore` | Global·Tenant·Collection·**Query** | 0.0 | Runtime | float 0.0–1.0 | ano |
| `Rag:Rerank:Provider` | Global·Tenant | `cohere` | **Restart** (klient) | enum {`cohere`,`none`,`local`} | ano |
| `Rag:Rerank:Enabled` | Global·Tenant·Collection·**Query** | true | Runtime | bool | ano |
| `Rag:Rerank:RelevanceScoreThreshold` | Global·Tenant·Collection·**Query** | 0.5 | Runtime | float 0.0–1.0 | ano |
| `Rag:Rerank:LatencyBudgetMs` | Global·Tenant | 800 | Runtime | int 50–10000 | ano |
| `Rag:Rerank:TimeoutMs` | Global·Tenant | 1500 | Runtime | int 100–30000 | ne |
| `Rag:Rerank:MaxRetries` | Global·Tenant | 2 | Runtime | int 0–5 | ne |
| `Rag:Rerank:MaxDocChars` | Global·Tenant | 4096 | Runtime | int 256–32000 | ne |
| `Rag:Freshness:HalfLifeDays` | Global·Tenant·Collection | 180 | Runtime | float 1–3650 | ano |
| `Rag:Freshness:DecayWeight` | Global·Tenant·Collection·**Query** | 0.2 | Runtime | float 0.0–1.0 | ano |
| `Rag:Freshness:VersioningMode` | Global·Tenant·Collection | `latest_wins` | Runtime | enum {`latest_wins`,`append`,`supersede`} | ano |
| `Rag:Freshness:UndatedPolicy` | Global·Tenant·Collection | `neutral` | Runtime | enum {`neutral`,`penalize`,`as_oldest`} | ano |
| `Rag:Freshness:AppendMaxVersions` | Global·Tenant·Collection | 5 | Runtime | int 1–100 | ano |
| `Rag:Graph:Enabled` | Global·Tenant·Collection·**Query** | false | Runtime | bool | ano |
| `Rag:Graph:Required` | Global·Tenant·Collection | false | Runtime | bool | ano |
| `Rag:Graph:MaxNeighbors` | Global·Tenant·Collection·**Query** | 25 | Runtime | int 1–500 | ano |
| `Rag:Graph:MaxNodeDegree` | Global·Tenant | 1000 | Runtime | int 1–100000 | ne |
| `Rag:Graph:SupernodeThreshold` | Global·Tenant | 5000 | Runtime | int 1–1000000 | ne |
| `Rag:Graph:MaxHops` | Global·Tenant·Collection·**Query** | 2 | Runtime | int 0–5 (platform cap 5) | ano |
| `Rag:Extraction:Model` | Global·Tenant | `claude-haiku` | **Restart** (klient) | enum z povolených | ano |
| `Rag:Extraction:Ontology` | Global·Tenant·Collection | `default` | Runtime | enum registrovaných ontologií | ano |
| `Rag:Extraction:Relations` | Global·Tenant·Collection | `[…]` | Runtime | podmnožina ontologie | ano |
| `Rag:Extraction:MinConfidence` | Global·Tenant·Collection | 0.6 | Runtime | float 0.0–1.0 | ano |
| `Rag:Extraction:MaxEntitiesPerChunk` | Global·Tenant | 50 | Runtime | int 1–500 | ne |
| `Rag:Extraction:UnknownTypePolicy` | Global·Tenant·Collection | `drop` | Runtime | enum {`drop`,`coerce`,`keep_as_other`} | ano |
| `Rag:Extraction:DanglingEdgePolicy` | Global·Tenant·Collection | `drop` | Runtime | enum {`drop`,`create_stub`} | ano |
| `Rag:Prompt:MinCacheablePrefixTokens` | Global·Tenant | 1024 | Runtime | int 0–8192 | ne |
| `Rag:Mcp:MaxK` | Global·Tenant | 20 | Runtime | int 1–100 | ano |
| `Rag:Mcp:MaxResponseTokens` | Global·Tenant | 4000 | Runtime | int 256–32000 | ne |
| `Rag:Limits:MaxTenantCollections` | Global·Tenant | 100 | Runtime | int 1–100000 | ne |
| `Rag:Limits:MaxUserCollections` | Global·Tenant | 20 | Runtime | int 1–10000 | ne |
| `Rag:Ingest:MaxBytes` | Global·Tenant | 26214400 | Runtime | int 1024–209715200 | ne |
| `Rag:Ingest:SagaTimeout` | Global·Tenant | `00:30:00` | Runtime | TimeSpan 1m–24h | ne |
| `Rag:Ingest:StuckThreshold` | Global | `00:15:00` | Runtime | TimeSpan 1m–24h | ne |
| `Rag:Eval:JudgeModel` | Global·Tenant | `claude-sonnet` | **Restart** | enum povolených | ne |
| `Rag:Eval:Thresholds` | Global·Tenant | `{faithfulness:0.7,…}` | Runtime | každý 0.0–1.0 | ne |
| `Rag:Retrieval:MinScore` | Global·Tenant·Collection·**Query** | 0.0 | Runtime | float 0.0–1.0 | ano |
| `Rag:Retrieval:OverallTimeoutMs` | Global·Tenant | 8000 | Runtime | int 500–60000 | ano |
| `Rag:Bulk:MaxIds` | Global·Tenant | 1000 | Runtime | int 1–100000 | ne |
| `Rag:Reindex:MaxBatch` | Global·Tenant | 500 | Runtime | int 1–100000 | ne |
| `Rag:Reembed:MaxPerRun` | Global·Tenant | 2000 | Runtime | int 1–1000000 | ne |
| `Rag:Pricing:*` | Global | (per model) | Runtime | ≥ 0 | ne |
| `Rag:Providers:*` | Global | (per provider) | **Restart** | enum/url | ne |

> Poznámky k registru: sloupec **„Trasovaný"** = hodnota se připíná do `RagTrace` / OTel attribute (UC-24-12). Knoby s **velkým rozsahem hodnot bez bezpečnostního dopadu** (BatchSize, TimeoutMs, MaxRetries) jsou „ne" → nezahlcovat trace. **Secrets nejsou v registru** (samostatné Options, UC-24-14). Sloupec „Query" v scope = knob je v **per-query allowlistu** (UC-24-07) — ostatní query override jsou ignorovány/odmítnuty.

### Edge cases UC-24-01
- **EC-24-01-01 — Knob v Options ale chybí v registru** · Trigger: vývojář přidá property do `RagTuningOptions`, ale zapomene descriptor. · Očekávané chování: párovací unit test `RagRegistryParityTests` selže (build-blocking) — žádný „neviditelný" knob. · Mechanismus: reflexe nad Options strom ↔ `RagParameterRegistry.All` keys; zákon §4 (REUSE-FIRST, jeden zdroj pravdy) · Severity: P1 · Test: ArchUnit-style parity test červený když se sety liší.
- **EC-24-01-02 — Descriptor v registru bez Options property** · Trigger: descriptor pro neexistující klíč (překlep). · Očekávané chování: stejný parity test selže opačným směrem. · Mechanismus: parity test obousměrný · Severity: P1 · Test: red on orphan descriptor.
- **EC-24-01-03 — Default mimo vlastní validační rozsah** · Trigger: descriptor `Default=5000`, `Range=1..1000`. · Očekávané chování: self-validace registru při startu hodí `RagConfigurationException("rag.config.registry_invalid")` → host nenastartuje (fail-fast mimo Dev; v Dev hlasitý warn + start). · Mechanismus: `RagParameterRegistry` startup validator, vzor `JwtOptionsValidator` · Severity: P0 · Test: úmyslně vadný descriptor → startup throw.
- **EC-24-01-04 — Nekonzistentní příznaky (Query-scope knob není Runtime-changeable)** · Trigger: descriptor s `Scope` obsahuje `Query` ale `RestartRequired=true`. · Očekávané chování: self-validace odmítne — query override restart-required knobu je nesmysl. · Mechanismus: invariant „Query ⇒ Runtime" v registru validatoru · Severity: P1 · Test: vadná kombinace → startup throw.
- **EC-24-01-05 — Secret omylem označen jako tunable** · Trigger: někdo přidá `Rag:OpenAi:ApiKey` do registru. · Očekávané chování: self-validace odmítne klíče matchující secret-prefix patterny (`*ApiKey`,`*Secret`,`*Password`,`*ConnectionString`). · Mechanismus: deny-list patternů v registr validatoru; cross-ref UC-24-14 · Severity: P0 · Test: secret-like key → startup throw.
- **EC-24-01-06 — Závislé rozsahy (TopN ≤ CandidateK) nelze ověřit izolovaně** · Trigger: registr má rozsah „1..CandidateK" jako symbolickou referenci. · Očekávané chování: cross-field validace se neřeší v descriptoru, ale v `EffectiveConfigValidator` (UC-24-09) — registr drží jen statické meze; symbolické vazby jsou flaggované pole `DependsOn`. · Mechanismus: rozdělení statická mez vs cross-field invariant · Severity: P2 · Test: TopN=50, CandidateK=10 → odmítnuto v effective validatoru, ne v registru.
- **EC-24-01-07 — Registry endpoint volán bez permission** · Trigger: user bez `Rag.Manage` GET `/admin/config/registry`. · Očekávané chování: 403 `ForbiddenException("rag.forbidden")`. · Mechanismus: `.RequirePermission(PlatformPermissions.RagManage)` · Severity: P0 · Test: user token → 403.
- **EC-24-01-08 — Registry endpoint NEvrací tenant override hodnoty** · Trigger: tenant-admin čte registr a očekává své override. · Očekávané chování: registr vrací jen metadata + global default, NE override (ty má GET settings UC-24-06 / GET effective UC-24-03) — žádný leak jiných tenantů. · Mechanismus: registr je platform-static; oddělení od `RagSetting` query · Severity: P1 · Test: response neobsahuje `RagSetting` řádky.
- **EC-24-01-09 — Drift-alias klíč v configu (legacy `Rag:Rrf:K`)** · Trigger: appsettings stále obsahuje starý klíč. · Očekávané chování: startup migration shim (UC-24-16) přemapuje na canonical + WARN; registr nikdy nevystavuje alias. · Mechanismus: `RagConfigKeyMigrator` před bindingem · Severity: P1 · Test: legacy key → bound to canonical, warn logged.
- **EC-24-01-10 — Enum hodnota mimo množinu (`Rag:Lexical:Provider=elastic`)** · Trigger: neznámý provider v configu. · Očekávané chování: Options validator fail-fast (mimo Dev), errorCode `rag.config.invalid_enum`. · Mechanismus: UC-24-09 validator + registr enum množina · Severity: P0 · Test: invalid enum → startup throw.

---

## UC-24-02 — Scope-hierarchie a výpočet effective config

- **Actor / role:** system/worker (resolver běží uvnitř každého retrieval/ingest dotazu) · čte ho i admin přes GET effective (UC-24-03)
- **Precondition:** Registr (UC-24-01) inicializován. Existují vrstvy: (L0) Global default z `IConfiguration`/`Options`; (L1) per-Tenant override v `hybridrag_rag_settings` (`RagSetting` se `Scope=Tenant`); (L2) per-Collection override (`RagSetting` se `Scope=Collection`, FK `CollectionId`); (L3) per-Query override (ephemerální, z request body, jen allowlist UC-24-07); navíc **presety** (UC-24-08) se aplikují jako pojmenovaná sada těsně nad L0.
- **Trigger:** interní — `IEffectiveRagConfigResolver.Resolve(collectionId, queryOverride?)` voláno na začátku retrieval handleru, ingest handleru a MCP handleru.
- **Main flow:**
  1. Resolver vezme **registr** jako množinu klíčů. Pro každý klíč spočítá hodnotu **shora dolů s přepisem**: `effective = L0 default` → pokud existuje preset (collection/query) přepiš preset hodnotami → přepiš L1 tenant override → přepiš L2 collection override → přepiš L3 query override (jen pokud knob ∈ allowlist a hodnota prošla horní mezí).
  2. Pro každý knob resolver respektuje jeho **deklarovaný scope-set**: collection override pro knob, který není `Collection`-scoped, je **ignorován** (a při zápisu odmítnut, EC-24-04-x); query override pro non-`Query` knob je **odmítnut/ignorován** (UC-24-07).
  3. Výsledek = `EffectiveRagConfig` (immutable snapshot) + **provenance mapa** (`Dictionary<key, ResolvedLayer>`: odkud hodnota přišla — Default/Preset/Tenant/Collection/Query) → ta jde do trace (UC-24-12).
  4. Cross-field invarianty (TopN ≤ CandidateK ≤ Dense:MaxTopK; Overlap ≤ Size/2; Hops ≤ cap) ověří `EffectiveConfigValidator` (UC-24-09) na **finálním** snapshotu — chyba ⇒ `BusinessRuleException`.
- **Postcondition / záruky:** Determinismus — stejný (collection, tenant override, query override, config version) ⇒ stejný snapshot. Provenance je úplná (každý klíč má vrstvu). Cross-field konzistence garantována před použitím.
- **Tenancy / permissions:** L1/L2 čtou `RagSetting` přes `ITenantContext` global filter (`IsSystem ‖ TenantId == claim`) + RLS (`ITenantScoped`) → tenant NIKDY nevidí cizí override. Collection override je dále scoped na collection (RLS `IUserOwned`/tenant dle scope korpusu). Identita pro „kdo se ptá" z tokenu.
- **Reuse / canonical pattern:** read přes `IReadDbContextFactory` dle `GetProfileHandler.cs:12`; tenant filtr dle CLAUDE.md §4 „Tenant scoping"; cross-field validace jako FluentValidation na composed snapshotu. · **Data dotčena:** `hybridrag_rag_settings`, `hybridrag_knowledge_collections` (pro scope/preset collection) · **Eventy:** — · **Priorita:** P0

### Edge cases UC-24-02
- **EC-24-02-01 — Žádné override (čistý default)** · Trigger: tenant nikdy nenastavil override, collection bez presetu, query bez override. · Očekávané chování: snapshot == čistě L0; provenance všude `Default`. · Mechanismus: resolver fallback · Severity: P1 · Test: prázdné `RagSetting` → effective == registr defaults.
- **EC-24-02-02 — Collection override pro Tenant-only knob** · Trigger: v DB existuje `RagSetting(Scope=Collection, Key=Rag:Dense:MaxTopK)` (MaxTopK je jen Global·Tenant). · Očekávané chování: resolver tento řádek **ignoruje** (loguje warn `rag.config.scope_mismatch_ignored`); zápis takového řádku byl už odmítnut v UC-24-05 → defense-in-depth při čtení. · Mechanismus: scope-set check v resolveru · Severity: P2 · Test: ručně vložený mismatch řádek → nemá vliv na effective.
- **EC-24-02-03 — Query override knobu mimo allowlist** · Trigger: request `{"override":{"Rag:Embedding:Model":"x"}}`. · Očekávané chování: odmítnuto validací (400 `rag.config.query_override_not_allowed`) — model NENÍ query-scoped; pipeline se nespustí s podvrženým modelem. · Mechanismus: UC-24-07 allowlist · Severity: P0 (security) · Test: override mimo allowlist → 400, žádná změna pipeline.
- **EC-24-02-04 — Vícenásobné override stejného klíče (tenant + collection + query)** · Trigger: všechny tři vrstvy nastaví `Rag:Fusion:TopN`. · Očekávané chování: vyhrává nejníže (Query), provenance=`Query`; precedence Default<Preset<Tenant<Collection<Query. · Mechanismus: resolver pořadí · Severity: P1 · Test: tři vrstvy → effective == query hodnota, provenance correct.
- **EC-24-02-05 — Cross-field konflikt po složení (TopN > CandidateK)** · Trigger: tenant nastaví TopN=150, collection ponechá CandidateK=100. · Očekávané chování: `EffectiveConfigValidator` hodí `BusinessRuleException("rag.config.cross_field_invalid")` → dotaz 422, NE tichý ořez. · Mechanismus: UC-24-09 cross-field · Severity: P0 · Test: konflikt → 422 s detailem které pole.
- **EC-24-02-06 — Restart-required knob přepsán per-tenant za běhu** · Trigger: tenant uloží `Rag:Embedding:Dimensions=3072`. · Očekávané chování: zápis přijat ale označen `EffectiveAfterRestart=true` + WARN „neprojeví se do restartu / vynutí reembed" (cross-ref UC-24-10/UC-24-01-restart); resolver pro běžící proces stále vrací starou hodnotu dimenze (pgvector kolona se nezměnila). · Mechanismus: registr flag RestartRequired + UC-24-10 · Severity: P0 · Test: změna dimensions → settings uloženo, runtime snapshot beze změny, varování v response.
- **EC-24-02-07 — Preset i explicitní override současně** · Trigger: collection má preset `accurate` ALE i explicitní `Rag:Rerank:Enabled=false`. · Očekávané chování: preset se aplikuje jako baseline, explicitní override ho přebije pro daný klíč (provenance=`Collection`), zbytek presetu zůstává (provenance=`Preset`). · Mechanismus: preset = vrstva pod explicit override · Severity: P1 · Test: mixed → rerank off i u accurate presetu.
- **EC-24-02-08 — Tenant nemá záznam `RagSetting`, ale je multi-tenant** · Trigger: nový tenant. · Očekávané chování: čisté defaults; žádný leak z jiného tenanta (filter). · Mechanismus: tenant filter prázdný set · Severity: P1 · Test: tenant B čistý i když tenant A má override.
- **EC-24-02-09 — Config version se změní během dlouhého ingestu** · Trigger: admin změní chunk size uprostřed běžící IngestSaga. · Očekávané chování: saga zafixuje snapshot na svém startu (`RagConfigVersion` uložen v `IngestSaga`), nepřebírá change za běhu → konzistentní chunking jednoho dokumentu. · Mechanismus: snapshot pinning, cross-ref UC-24-11 + oblast 04 · Severity: P1 · Test: změna za běhu → saga doběhne starým snapshotem.
- **EC-24-02-10 — Resolver volán s neexistující collection** · Trigger: query nad smazanou/cizí collection. · Očekávané chování: 404 (RLS/filter → foreign id == 404, ne 403) ještě před resolverem; resolver se nespustí. · Mechanismus: zákon §10 (identita z tokenu), RLS 404 · Severity: P0 · Test: cizí collectionId → 404.
- **EC-24-02-11 — Neplatná hodnota uložená v DB (legacy/manuální zásah)** · Trigger: `RagSetting` má hodnotu mimo rozsah (např. ruční SQL). · Očekávané chování: resolver hodnotu **clampuje na default + WARN** `rag.config.persisted_value_invalid` (graceful, neshazuje dotaz), audit-flag pro admin review. · Mechanismus: defenzivní parse v resolveru · Severity: P1 · Test: vadný řádek → effective fallback default, warn metric `platform.rag.config.invalid_persisted`.
- **EC-24-02-12 — Provenance neúplná (knob přidán, override starý snapshot)** · Trigger: nový knob v registru, žádné override. · Očekávané chování: provenance=`Default`, žádný null v mapě. · Mechanismus: resolver iteruje registr (ne override) · Severity: P2 · Test: nový knob → provenance present.

---

## UC-24-03 — GET effective-config (ladící endpoint „proč tahle odpověď")

- **Actor / role:** tenant-admin (svůj tenant/collection) · platform-admin (cross-tenant s `Rag.Manage`)
- **Precondition:** Modul enabled, caller má `Rag.CollectionManage` (collection-level) nebo `Rag.Manage` (tenant-level).
- **Trigger:** `GET /v1/hybridrag/admin/config/effective?collectionId={id}&preset={name}&simulateQueryOverride={json}` (poslední dva volitelné — umožní „dry-run" jak by vypadal effective pro daný preset/query override BEZ skutečného dotazu).
- **Main flow:**
  1. Endpoint ověří permission + že collection patří callerovu tenantu (RLS/filter → cizí id = 404).
  2. Zavolá `IEffectiveRagConfigResolver.Resolve(collectionId, simulateQueryOverride)` (UC-24-02).
  3. Vrátí `ApiResponse<EffectiveRagConfigDto>` = pro každý knob: canonical key, effective value, **provenance** (vrstva), default value, je-li runtime/restart, je-li trasovaný, + `ConfigVersion`. **Secrets jsou vynechány úplně** (UC-24-14).
- **Postcondition / záruky:** Read-only, žádná mutace, žádný audit zápis. Determinismus s UC-24-02. Slouží jako primární ladicí nástroj („tahle odpověď použila TopN=10 z Collection override, preset=accurate").
- **Tenancy / permissions:** Scope = tenant/collection callera; **effective-config IDOR ochrana** (EC-24-03-02). Permission `Rag.CollectionManage` pro collection, `Rag.Manage` pro tenant-wide.
- **Reuse / canonical pattern:** read endpoint dle `GetProfileHandler.cs:12`; IDOR ochrana dle zákona §10 (RLS 404). · **Data dotčena:** čte `hybridrag_rag_settings`, `hybridrag_knowledge_collections` (read-only) · **Eventy:** — · **Priorita:** P0

### Edge cases UC-24-03
- **EC-24-03-01 — Effective bez collectionId (tenant-wide effective)** · Trigger: GET bez `collectionId`. · Očekávané chování: vrací tenant-level effective (L0+preset+L1), bez L2/L3; provenance bez Collection vrstvy. · Mechanismus: resolver bez collection · Severity: P2 · Test: no collection → tenant effective.
- **EC-24-03-02 — IDOR: cizí collectionId** · Trigger: tenant A požádá o effective collection tenanta B. · Očekávané chování: **404** (ne 403 — neprozradit existenci) přes RLS/filter. · Mechanismus: zákon §10, RLS 404 · Severity: P0 (security) · Test: cross-tenant id → 404.
- **EC-24-03-03 — Secret leak přes effective** · Trigger: caller doufá, že effective vrátí `OpenAi:ApiKey`. · Očekávané chování: secrets nejsou součástí effective DTO vůbec (ani maskované — prostě nejsou knoby v registru). · Mechanismus: UC-24-14 + registr neobsahuje secrets · Severity: P0 (security) · Test: response neobsahuje žádný `*ApiKey`/`*Secret`.
- **EC-24-03-04 — simulateQueryOverride mimo allowlist** · Trigger: dry-run s override `Rag:Embedding:Model`. · Očekávané chování: 400 `rag.config.query_override_not_allowed` (stejná validace jako reálný query path). · Mechanismus: UC-24-07 sdílená validace · Severity: P1 · Test: invalid simulate → 400.
- **EC-24-03-05 — Preset neexistuje v query stringu** · Trigger: `?preset=ultra`. · Očekávané chování: 400 `rag.config.preset_not_found` se seznamem dostupných (`fast|balanced|accurate` + custom). · Mechanismus: UC-24-08 preset lookup · Severity: P1 · Test: neznámý preset → 400.
- **EC-24-03-06 — Caller má jen CollectionManage, ptá se tenant-wide** · Trigger: collection-admin GET bez collectionId. · Očekávané chování: 403 (tenant-wide vyžaduje `Rag.Manage`). · Mechanismus: permission split · Severity: P1 · Test: collection-admin → 403 na tenant-wide.
- **EC-24-03-07 — Trasovaný vs netrasovaný flag v DTO** · Trigger: UI chce zvýraznit jen trasované knoby. · Očekávané chování: DTO nese `Traced` boolean z registru. · Mechanismus: registr metadata · Severity: P3 · Test: DTO má `Traced` shodný s registrem.
- **EC-24-03-08 — Cross-field violation v uloženém stavu** · Trigger: effective by porušil TopN≤CandidateK (admin uložil přes jiný kanál). · Očekávané chování: GET effective **stále vrátí** snapshot ALE označí `Warnings:[{cross_field}]` (read endpoint nehází 422 — jen reálný dotaz; ladící endpoint má warnings ukázat). · Mechanismus: resolver validate-but-report mode · Severity: P1 · Test: konflikt → 200 s warnings, ne 422.
- **EC-24-03-09 — Modul disabled** · Trigger: `Modules:HybridRag:Enabled=false`. · Očekávané chování: endpoint neexistuje (modul nemapuje routy) → 404. · Mechanismus: feature flag / module load · Severity: P2 · Test: disabled → route 404.
- **EC-24-03-10 — Velký registr (100+ knobů) výkon** · Trigger: full effective dump. · Očekávané chování: < 50 ms (in-memory + max 2 DB selecty na override); žádný N+1. · Mechanismus: jeden select per scope · Severity: P2 · Test: latence pod prahem.

---

## UC-24-04 — Admin PUT per-tenant nastavení (override knobu na úrovni tenanta)

- **Actor / role:** tenant-admin (svůj tenant) · platform-admin (cross-tenant)
- **Precondition:** caller má `Rag.Manage` / `PlatformPermissions.RagManage`. Knob existuje v registru a má `Tenant` ve svém scope-setu.
- **Trigger:** `PUT /v1/hybridrag/admin/config/tenant` s body `{ "settings": [ { "key": "Rag:Fusion:TopN", "value": 15 }, … ] }` (batch upsert) nebo `DELETE …/tenant/{key}` (reset na default).
- **Main flow:**
  1. Validátor (`UpsertTenantSettingsValidator`) pro každý řádek: knob ∈ registr (jinak `rag.config.unknown_key`); `Tenant` ∈ scope-set (jinak `rag.config.scope_not_allowed`); hodnota typuje a leží v rozsahu (jinak `rag.config.value_out_of_range`); hodnota ≤ platform cap (jinak `rag.config.exceeds_platform_cap`, EC-24-04-03); je-li secret-like → odmítnout (EC-24-04-08).
  2. Handler upsertuje `RagSetting(Scope=Tenant, TenantId=ctx.TenantId, Key, ValueJson, UpdatedBy=ctx.UserId, UpdatedAtUtc=clock.UtcNow)` — **idempotentně** přes UNIQUE `(TenantId, Scope, CollectionId, Key)`.
  3. Mutace tracked entity → **xmin optimistic concurrency** + `ConcurrencyRetryBehavior`; commit + audit přes `AuditInterceptor` (změněná pole → `hybridrag_audit_entries`); pokud knob je restart-required, do response `EffectiveAfterRestart=true` + warn.
  4. `IOptionsMonitor`-bound runtime knoby se projeví na příští `Resolve` (DB override je čten per-request, takže okamžitě); publikuje se `RagConfigChangedIntegrationEvent` (outbox) pro invalidaci případné cache effective configu a pro audit/observability.
- **Postcondition / záruky:** Override perzistován, auditován (kdo/kdy/co). Žádný double-write (UNIQUE). Cross-field konzistence se validuje na **kompozici** (UC-24-09) — PUT může uložit jednotlivý knob, ale dotaz se zablokuje 422, pokud výsledný snapshot poruší invariant (volitelně PUT taky dry-run validuje kompozici, EC-24-04-06).
- **Tenancy / permissions:** `Scope=Tenant` data, RLS + tenant filter; tenant-admin píše JEN svůj tenant (`TenantId` z tokenu, NIKDY z body — zákon §10). Platform-admin cross-tenant jen s elevated claim.
- **Reuse / canonical pattern:** write+outbox dle `RegisterUserHandler.cs:22` (`SaveChangesAndFlushMessagesAsync`); idempotency UNIQUE + catch `DbUpdateException` (zákon §2); concurrency xmin dle CLAUDE.md §4. · **Data dotčena:** `hybridrag_rag_settings`, `hybridrag_audit_entries` · **Eventy:** `RagConfigChangedIntegrationEvent` · **Priorita:** P0

### Edge cases UC-24-04
- **EC-24-04-01 — Neznámý knob v PUT** · Trigger: `{"key":"Rag:Foo:Bar"}`. · Očekávané chování: 400 `rag.config.unknown_key` (žádný řádek se neuloží — celý batch atomicky validován před zápisem). · Mechanismus: validátor vs registr · Severity: P0 · Test: unknown → 400, DB beze změny.
- **EC-24-04-02 — Hodnota mimo rozsah** · Trigger: `Rag:Fusion:Rrf:K = 100000`. · Očekávané chování: 400 `rag.config.value_out_of_range` s rozsahem v detailu. · Mechanismus: validátor rozsah z registru · Severity: P0 · Test: out-of-range → 400.
- **EC-24-04-03 — Tenant override překročí platform cap** · Trigger: tenant `Rag:Dense:MaxTopK=5000`, platform cap 1000. · Očekávané chování: 400 `rag.config.exceeds_platform_cap` — tenant nesmí zvednout cap nad platform-global maximum (anti-DoS). · Mechanismus: dvojí mez: registr static max + global cap z `IConfiguration` · Severity: P0 (security) · Test: nad cap → 400.
- **EC-24-04-04 — Scope mismatch (Collection-only knob přes tenant PUT)** · Trigger: knob jen `Collection`-scoped poslán na tenant endpoint. · Očekávané chování: 400 `rag.config.scope_not_allowed`. · Mechanismus: scope-set check · Severity: P1 · Test: scope mismatch → 400.
- **EC-24-04-05 — Concurrent PUT stejného klíče (race, xmin)** · Trigger: dva admini upsertují `TopN` současně. · Očekávané chování: jeden vyhraje, druhý retry přes `ConcurrencyRetryBehavior` (xmin), výsledek deterministicky poslední-zapsaný; oba auditovány. · Mechanismus: xmin + retry (CLAUDE.md §4) · Severity: P1 · Test: 2-way concurrent → no lost update, oba audit entries.
- **EC-24-04-06 — Uložení knobu vyrobí cross-field konflikt** · Trigger: tenant uloží TopN=200 (CandidateK zůstává 100). · Očekávané chování: PUT s `?validateComposition=true` vrací 422 `rag.config.cross_field_invalid` (preventivní); bez flagu uloží a dotaz selže 422 později (warn v response). · Mechanismus: UC-24-09 cross-field, volitelný preventivní check · Severity: P0 · Test: konflikt → 422 (preventive) / dotaz 422 (lazy).
- **EC-24-04-07 — TenantId z body (IDOR pokus)** · Trigger: body obsahuje `tenantId` jiného tenanta. · Očekávané chování: ignorováno — `TenantId` výhradně z `ITenantContext`; pokud platform-admin chce cross-tenant, jde přes explicitní `/admin/config/tenant/{tenantId}` s `Rag.Manage` elevated. · Mechanismus: zákon §10 · Severity: P0 (security) · Test: body tenantId → no effect, píše se vlastní tenant.
- **EC-24-04-08 — Pokus uložit secret jako knob** · Trigger: `{"key":"Rag:OpenAi:ApiKey","value":"sk-…"}`. · Očekávané chování: 400 `rag.config.secret_not_configurable`; nic se neuloží; navíc by takový klíč ani neprošel registr lookupem. · Mechanismus: UC-24-14 deny + registr · Severity: P0 (security) · Test: secret key → 400, žádný plaintext v DB ani logu.
- **EC-24-04-09 — Reset na default (DELETE)** · Trigger: `DELETE /tenant/Rag:Fusion:TopN`. · Očekávané chování: řádek `RagSetting` smazán (soft/hard dle entity) → effective spadne na L0; auditováno jako delete. · Mechanismus: delete + audit · Severity: P1 · Test: delete → effective == default, audit entry.
- **EC-24-04-10 — Batch s jedním vadným řádkem** · Trigger: 5 řádků, 1 out-of-range. · Očekávané chování: **celý batch odmítnut** (all-or-nothing), 400 s indexem vadného řádku; žádný částečný zápis. · Mechanismus: validace celého batche před zápisem · Severity: P1 · Test: 1 vadný → 0 uloženo.
- **EC-24-04-11 — Restart-required knob přes PUT** · Trigger: tenant uloží `Rag:Lexical:Provider=ts_rank`. · Očekávané chování: uloženo, response `EffectiveAfterRestart=true`, warn `rag.config.restart_required` — runtime stále `pg_search` do restartu. · Mechanismus: registr RestartRequired + UC-24-10 · Severity: P0 · Test: restart knob → uloženo + warn, runtime beze změny.
- **EC-24-04-12 — Embedding:Model změna vynutí reembed** · Trigger: tenant uloží `Rag:Embedding:Model=text-embedding-3-small`. · Očekávané chování: uloženo + response `RequiresReembed=true` + publikuje `RagEmbeddingModelChangedIntegrationEvent` → spustí reembed flow (cross-ref oblast 03/09); staré vektory zůstávají dokud reembed neproběhne (model-drift guard). · Mechanismus: UC-24-16 model-drift + reembed job · Severity: P0 · Test: model change → reembed event, dotazy nemíchají dimenze/modely.
- **EC-24-04-13 — Audit zápis změny knobu** · Trigger: jakýkoli úspěšný PUT. · Očekávané chování: `hybridrag_audit_entries` má entry s old→new value, UserId, UTC (cross-ref oblast 25). · Mechanismus: `AuditInterceptor` na SaveChanges · Severity: P1 · Test: PUT → audit entry s diff.

---

## UC-24-05 — Admin PUT per-collection nastavení

- **Actor / role:** tenant-admin / collection owner s `Rag.CollectionManage`
- **Precondition:** Collection existuje, patří callerovu tenantu/scope; knob ∈ registr a má `Collection` ve scope-setu.
- **Trigger:** `PUT /v1/hybridrag/admin/config/collections/{collectionId}` (batch upsert) / `DELETE …/{collectionId}/{key}`.
- **Main flow:** Stejná validace jako UC-24-04, navíc: (a) collection ownership check (RLS → cizí id = 404); (b) knob musí být `Collection`-scoped; (c) collection override **nesmí překročit tenant override ani platform cap** (multi-úrovňový cap, EC-24-05-03); (d) lze nastavit `Preset` na úrovni collection (UC-24-08). Upsert `RagSetting(Scope=Collection, CollectionId, …)` přes UNIQUE `(TenantId, Scope, CollectionId, Key)`.
- **Postcondition / záruky:** Collection-level override perzistován, auditován, respektuje hierarchii cap (platform ≥ tenant ≥ collection ≥ query). Idempotentní (UNIQUE).
- **Tenancy / permissions:** Collection scope: pokud korpus `Scope=User` → `IUserOwned` RLS (owner), pokud `Scope=Tenant` → tenant filter; permission `Rag.CollectionManage`. Identita z tokenu.
- **Reuse / canonical pattern:** write+outbox `RegisterUserHandler.cs:22`; idempotency UNIQUE; RLS 404 dle zákona §10. · **Data dotčena:** `hybridrag_rag_settings`, `hybridrag_knowledge_collections`, `hybridrag_audit_entries` · **Eventy:** `RagConfigChangedIntegrationEvent` (s `CollectionId`) · **Priorita:** P0

### Edge cases UC-24-05
- **EC-24-05-01 — Cizí collection (IDOR)** · Trigger: PUT na collection jiného tenanta/usera. · Očekávané chování: 404 (RLS). · Mechanismus: zákon §10 · Severity: P0 (security) · Test: cross-owner → 404.
- **EC-24-05-02 — Tenant-only knob přes collection PUT** · Trigger: `Rag:Dense:MaxTopK` (Global·Tenant) na collection. · Očekávané chování: 400 `rag.config.scope_not_allowed`. · Mechanismus: scope-set check · Severity: P1 · Test: tenant-only → 400.
- **EC-24-05-03 — Collection override > tenant override (cap hierarchie)** · Trigger: tenant nastavil `Graph:MaxHops=2`, collection chce 5. · Očekávané chování: pokud tenant hodnota je politickým capem (knob s `IsCapKnob`) → 400 `rag.config.exceeds_tenant_cap`; jinak collection legitimně přepisuje (override ≠ cap). Rozhodnutí dle příznaku knobu (hops je cap-knob → blok; rerank threshold ne). · Mechanismus: registr `CapSemantics` flag · Severity: P0 (security/policy) · Test: hops nad tenant cap → 400; threshold přepis OK.
- **EC-24-05-04 — Nastavení presetu na collection** · Trigger: `{"preset":"accurate"}`. · Očekávané chování: uloženo jako `RagSetting(Key=Rag:Preset, Value=accurate)`; resolver aplikuje preset baseline (UC-24-08). · Mechanismus: preset jako speciální klíč · Severity: P1 · Test: preset set → effective reflektuje preset.
- **EC-24-05-05 — Neexistující preset** · Trigger: `{"preset":"turbo"}`. · Očekávané chování: 400 `rag.config.preset_not_found`. · Mechanismus: UC-24-08 lookup · Severity: P1 · Test: bad preset → 400.
- **EC-24-05-06 — Concurrent collection PUT (xmin)** · Trigger: dva editoři. · Očekávané chování: xmin retry, no lost update. · Mechanismus: ConcurrencyRetryBehavior · Severity: P1 · Test: 2-way → konzistentní.
- **EC-24-05-07 — Collection smazána mezi validací a zápisem** · Trigger: collection deleted concurrently. · Očekávané chování: FK/RLS → upsert selže gracefully → 404/409 (`rag.config.collection_gone`). · Mechanismus: FK guard + RLS · Severity: P2 · Test: race delete → no orphan setting.
- **EC-24-05-08 — Dimensions/Model na collection (restart/reembed)** · Trigger: collection `Rag:Embedding:Model` změna. · Očekávané chování: stejně jako EC-24-04-12 ale scoped na collection → reembed jen této collection. · Mechanismus: reembed event s CollectionId · Severity: P0 · Test: per-collection reembed, ostatní collections beze změny.
- **EC-24-05-09 — Override knobu, který je per-collection nesmyslný (Ingest:SagaTimeout)** · Trigger: knob Global·Tenant-only poslán. · Očekávané chování: 400 scope_not_allowed (SagaTimeout není Collection). · Mechanismus: scope-set · Severity: P2 · Test: → 400.
- **EC-24-05-10 — Audit collection změny** · Trigger: úspěšný PUT. · Očekávané chování: audit entry s CollectionId v contextu. · Mechanismus: AuditInterceptor · Severity: P1 · Test: → audit s collection scope.
- **EC-24-05-11 — Reset (DELETE) collection knobu spadne na tenant ne na global** · Trigger: delete collection override, tenant override existuje. · Očekávané chování: effective spadne na **tenant** vrstvu (ne global). · Mechanismus: hierarchie resolveru · Severity: P1 · Test: delete L2 → effective == L1.

---

## UC-24-06 — GET per-tenant / per-collection nastavení (raw override, ne effective)

- **Actor / role:** tenant-admin (`Rag.Manage`) / collection-admin (`Rag.CollectionManage`)
- **Precondition:** caller scope dovoluje čtení.
- **Trigger:** `GET /v1/hybridrag/admin/config/tenant`, `GET …/collections/{collectionId}`.
- **Main flow:** Vrátí **jen explicitně nastavené** `RagSetting` řádky (raw override, NE composed effective — to je UC-24-03), s metadaty (UpdatedBy, UpdatedAtUtc, ConfigVersion). Pro UI „co jsem přepsal vs co je default".
- **Postcondition / záruky:** Read-only; rozlišuje „nenastaveno (=default)" od „nastaveno na hodnotu rovnou defaultu".
- **Tenancy / permissions:** RLS/tenant filter; cizí scope = 404.
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12` read factory. · **Data dotčena:** `hybridrag_rag_settings` (read) · **Eventy:** — · **Priorita:** P1

### Edge cases UC-24-06
- **EC-24-06-01 — Žádné override** · Trigger: čistý tenant. · Očekávané chování: prázdné pole (ne defaults — ty má effective). · Mechanismus: čte jen `RagSetting` · Severity: P2 · Test: empty → `[]`.
- **EC-24-06-02 — IDOR cizí collection** · Trigger: cizí id. · Očekávané chování: 404. · Mechanismus: RLS · Severity: P0 · Test: → 404.
- **EC-24-06-03 — Secret řádek nikdy nepřítomen** · Trigger: — · Očekávané chování: `RagSetting` fyzicky neobsahuje secrets (UC-24-14) → nemůže být ve výstupu. · Mechanismus: secrets mimo `RagSetting` · Severity: P0 · Test: žádný secret key.
- **EC-24-06-04 — Override hodnoty rovné defaultu** · Trigger: tenant nastavil TopN=10 (==default). · Očekávané chování: řádek se vrací s `IsExplicit=true` (rozliší od nenastaveno). · Mechanismus: explicit flag · Severity: P3 · Test: explicit default → present.
- **EC-24-06-05 — Stale value (knob odebrán z registru)** · Trigger: legacy `RagSetting` klíč už není v registru. · Očekávané chování: vrací se s `Orphaned=true` + warn, aby admin uklidil; resolver ho ignoruje. · Mechanismus: registr diff · Severity: P2 · Test: orphan → flagged.
- **EC-24-06-06 — Permission split** · Trigger: collection-admin čte tenant settings. · Očekávané chování: 403. · Mechanismus: permission · Severity: P1 · Test: → 403.
- **EC-24-06-07 — ConfigVersion v odpovědi** · Trigger: — · Očekávané chování: každý řádek nese `ConfigVersion` (UC-24-11) pro optimistic PUT. · Mechanismus: versioning · Severity: P2 · Test: version present.
- **EC-24-06-08 — Velký počet override** · Trigger: tenant override 60 knobů. · Očekávané chování: jeden select, žádný N+1, paging volitelný. · Mechanismus: single query · Severity: P3 · Test: latence OK.

---

## UC-24-07 — Per-query override (allowlist + horní meze, anti-DoS)

- **Actor / role:** user (běžný RAG dotaz) · system (MCP tool)
- **Precondition:** user má přístup k collection (RLS). Knoby v allowlistu: `Rag:Fusion:TopN`, `Rag:Fusion:CandidateK`, `Rag:Fusion:Rrf:K`, `Rag:Fusion:MinScore`, `Rag:Dense:MinSimilarity`, `Rag:Retrieval:MinScore`, `Rag:Graph:Enabled`, `Rag:Graph:MaxHops`, `Rag:Graph:MaxNeighbors`, `Rag:Rerank:Enabled`, `Rag:Rerank:RelevanceScoreThreshold`, `Rag:Freshness:DecayWeight` + výběr presetu (`preset`). **Vše ostatní mimo allowlist.**
- **Trigger:** retrieval/chat endpoint `POST /v1/hybridrag/collections/{id}/query` s `{"override": {...}, "preset": "..."}` (nepovinné).
- **Main flow:**
  1. `QueryOverrideValidator`: každý klíč v override musí být v allowlistu (jinak **odmítnout** 400 `rag.config.query_override_not_allowed` — striktní default; volitelně režim „ignore unknown" konfigurovatelný, ale bezpečnostně citlivé knoby vždy reject); typ + rozsah z registru; navíc **per-query horní mez (`QueryMax`)** ≤ effective (collection/tenant) hodnota a ≤ platform cap (např. `TopN ≤ min(effectiveTopN_cap, Dense:MaxTopK)`; `MaxHops ≤ cap 5`).
  2. Validní override jdou do `Resolve(...)` jako L3 vrstva (UC-24-02), provenance=`Query`.
  3. Effective snapshot prochází cross-field validací (UC-24-09); použije se pro pipeline; připne se do trace (UC-24-12).
- **Postcondition / záruky:** User NIKDY nemůže eskalovat nad cap (žádný `TopN=100000` DoS, žádný `MaxHops=50` graph explosion). Override je ephemerální (neperzistuje, neaudituje jako settings — jen do trace).
- **Tenancy / permissions:** Override neobchází RLS ani permission — jen ladí parametry v rámci povolených mezí. Identita z tokenu.
- **Reuse / canonical pattern:** validace dle `RegisterUserValidator` `.WithErrorCode(...)`; cap clamp logika lokální; cross-ref UC-24-01 allowlist flag. · **Data dotčena:** žádná (ephemerální), zapisuje se do `hybridrag_rag_traces` (UC-24-12) · **Eventy:** — · **Priorita:** P0

### Edge cases UC-24-07
- **EC-24-07-01 — Override mimo allowlist (Embedding:Model)** · Trigger: `{"override":{"Rag:Embedding:Model":"x"}}`. · Očekávané chování: 400 reject (security-citlivý knob NIKDY query). · Mechanismus: allowlist flag z registru · Severity: P0 (security) · Test: model override → 400.
- **EC-24-07-02 — TopK nad cap (DoS pokus)** · Trigger: `{"Rag:Fusion:TopN": 100000}`. · Očekávané chování: 400 `rag.config.query_override_exceeds_cap` (NE tichý clamp — explicitní odmítnutí, aby uživatel věděl); cap = min(effective, platform). · Mechanismus: per-query QueryMax · Severity: P0 (security/DoS) · Test: nad cap → 400, pipeline nespuštěna s velkým K.
- **EC-24-07-03 — MaxHops nad cap (graph explosion)** · Trigger: `{"Rag:Graph:MaxHops": 10}`. · Očekávané chování: 400 cap 5. · Mechanismus: hops cap · Severity: P0 · Test: → 400.
- **EC-24-07-04 — Graph:Enabled=true ale collection nemá graf** · Trigger: query zapne graf na collection bez `Graph:Required`/bez extrakce. · Očekávané chování: graf fáze se spustí ale vrátí prázdné (graceful) NEBO 422 `rag.graph.not_available` dle `Graph:Required` collection politiky; trasováno. · Mechanismus: feature toggle resolution (UC-24-15) · Severity: P1 · Test: graph on bez grafu → prázdná graph fáze, trace flag.
- **EC-24-07-05 — Rerank vypnut per-query přes collection-required** · Trigger: collection `Rag:Rerank:Enabled=true (policy)`, query chce false. · Očekávané chování: pokud rerank je u collection vynucený politikou (`RerankMandatory` flag) → query override ignorován + warn; jinak respektováno. · Mechanismus: mandatory-flag vs preference · Severity: P1 · Test: mandatory → ignore query off.
- **EC-24-07-06 — Neznámý klíč v override (typo)** · Trigger: `{"Rag:Fusion:Topn": 5}` (špatné casing). · Očekávané chování: 400 `rag.config.unknown_key` (klíče case-sensitive, kanonické). · Mechanismus: registr lookup exact · Severity: P1 · Test: typo → 400.
- **EC-24-07-07 — Cross-field konflikt z query (TopN > CandidateK effective)** · Trigger: query TopN=80, effective CandidateK=50. · Očekávané chování: 422 cross_field (UC-24-09) — query nesmí porušit invariant. · Mechanismus: cross-field na composed · Severity: P0 · Test: → 422.
- **EC-24-07-08 — Override + preset současně v query** · Trigger: `{"preset":"fast","override":{"Rag:Fusion:TopN":50}}`. · Očekávané chování: preset baseline, explicit override přebije (provenance=Query); ale stále pod cap. · Mechanismus: vrstvení preset<query-explicit · Severity: P1 · Test: mixed → override wins, capped.
- **EC-24-07-09 — Float vs int type mismatch** · Trigger: `{"Rag:Fusion:Rrf:K": 60.5}`. · Očekávané chování: 400 `rag.config.type_mismatch` (K je int). · Mechanismus: typovaný parse · Severity: P2 · Test: → 400.
- **EC-24-07-10 — Override při MCP volání (MaxK)** · Trigger: MCP tool s k > `Rag:Mcp:MaxK`. · Očekávané chování: clamp/odmítnutí dle MCP politiky (`Rag:Mcp:MaxK` cap), trasováno (cross-ref oblast 20). · Mechanismus: MCP cap · Severity: P1 · Test: MCP nad MaxK → cap/reject.
- **EC-24-07-11 — Prázdný override objekt** · Trigger: `{"override":{}}`. · Očekávané chování: no-op, effective == collection/tenant; provenance bez Query. · Mechanismus: prázdná L3 · Severity: P3 · Test: → effective beze změny.
- **EC-24-07-12 — Override poslán anonymem / bez collection access** · Trigger: user bez práva na collection. · Očekávané chování: 404 collection (RLS) PŘED zpracováním override. · Mechanismus: zákon §10 · Severity: P0 · Test: → 404.
- **EC-24-07-13 — Negativní / nulová hodnota** · Trigger: `{"Rag:Fusion:TopN": 0}`. · Očekávané chování: 400 (rozsah 1–…). · Mechanismus: rozsah · Severity: P2 · Test: → 400.

---

## UC-24-08 — Presety / profily (`fast` | `balanced` | `accurate` + custom)

- **Actor / role:** user (výběr per-query) · tenant/collection-admin (přiřazení per-collection, definice custom presetu) · platform-admin (platform presety)
- **Precondition:** Existují vestavěné presety (`fast`,`balanced`,`accurate`) jako kód-deklarace + volitelně custom presety v `hybridrag_rag_presets` (tenant-scoped).
- **Trigger:** výběr v query (`preset`), v collection settings (UC-24-05), nebo CRUD custom presetu `POST/PUT/DELETE /v1/hybridrag/admin/config/presets` (`Rag.Manage`).
- **Main flow:**
  1. Preset = pojmenovaná **dílčí** sada knobů (např. `fast`: Rerank off, CandidateK=40, TopN=5, Graph off, EfSearch=40; `accurate`: Rerank on threshold 0.6, CandidateK=200, TopN=12, Graph on hops=2, Freshness decay 0.3). Definuje jen podmnožinu knobů; zbytek dědí default.
  2. Resolver aplikuje preset jako vrstvu **těsně nad Global default, pod Tenant/Collection explicit override** (UC-24-02) → preset je „rozumný baseline", explicit override ho ladí.
  3. Custom preset: tenant-admin uloží pojmenovanou sadu (validovanou registrem, cap-checked); přiřaditelná per-collection/per-query.
- **Postcondition / záruky:** Presety jsou versionované, validní (každý knob v rozsahu/capu), nepřekročí platform cap. Vestavěné presety jsou immutable (custom jdou editovat).
- **Tenancy / permissions:** Custom presety `ITenantScoped` (RLS); built-in jsou platform-global read-only. Permission `Rag.Manage` pro CRUD.
- **Reuse / canonical pattern:** CRUD slice dle `Features/Users/RegisterUser/*`; write+outbox `RegisterUserHandler.cs:22`; validace registrem. · **Data dotčena:** `hybridrag_rag_presets`, `hybridrag_audit_entries` · **Eventy:** `RagConfigChangedIntegrationEvent` (preset změna invaliduje cache) · **Priorita:** P1

### Edge cases UC-24-08
- **EC-24-08-01 — Neexistující preset** · Trigger: `preset=ultra`. · Očekávané chování: 400 `rag.config.preset_not_found` + seznam dostupných. · Mechanismus: lookup · Severity: P1 · Test: → 400.
- **EC-24-08-02 — Custom preset překročí platform cap** · Trigger: custom preset `TopN=5000`. · Očekávané chování: 400 `exceeds_platform_cap` při uložení. · Mechanismus: cap validace presetu · Severity: P0 (security) · Test: → 400.
- **EC-24-08-03 — Custom preset obsahuje secret/neznámý/scope-mismatch knob** · Trigger: preset s `Embedding:Model` nebo `ApiKey`. · Očekávané chování: model OK pokud admin-allowed (preset může nastavit i restart-required knob → ale s reembed/restart důsledkem); secret reject; unknown reject. · Mechanismus: registr validace presetu · Severity: P0 · Test: secret in preset → 400.
- **EC-24-08-04 — Editace built-in presetu** · Trigger: PUT na `fast`. · Očekávané chování: 409 `rag.config.preset_readonly`. · Mechanismus: built-in immutable flag · Severity: P1 · Test: → 409.
- **EC-24-08-05 — Custom preset name kolize s built-in** · Trigger: vytvořit custom `fast`. · Očekávané chování: 409 `rag.config.preset_name_reserved`. · Mechanismus: reserved names · Severity: P2 · Test: → 409.
- **EC-24-08-06 — Smazání presetu používaného collection** · Trigger: DELETE presetu, který collection referencuje. · Očekávané chování: 409 `rag.config.preset_in_use` (s počtem collections) NEBO soft-delete + collections spadnou na default + warn (politika). · Mechanismus: FK / usage check · Severity: P1 · Test: in-use delete → blok/graceful.
- **EC-24-08-07 — Preset definuje restart-required knob** · Trigger: preset s `Lexical:Provider`. · Očekávané chování: výběr presetu za běhu → runtime část se aplikuje, restart-required část warn „after restart". · Mechanismus: UC-24-10 · Severity: P1 · Test: mixed preset → partial apply + warn.
- **EC-24-08-08 — Preset cross-field nekonzistentní** · Trigger: preset TopN=200, CandidateK=100. · Očekávané chování: 400 cross_field při uložení. · Mechanismus: UC-24-09 na presetu · Severity: P0 · Test: → 400.
- **EC-24-08-09 — IDOR custom preset cizího tenanta** · Trigger: GET/PUT cizí preset. · Očekávané chování: 404. · Mechanismus: RLS · Severity: P0 · Test: → 404.
- **EC-24-08-10 — Preset zachycen v trace** · Trigger: query s presetem. · Očekávané chování: trace nese `preset=accurate` + provenance knobů z presetu (UC-24-12). · Mechanismus: provenance · Severity: P1 · Test: trace má preset name.
- **EC-24-08-11 — Concurrent edit presetu (xmin)** · Trigger: 2 admini. · Očekávané chování: xmin retry. · Mechanismus: ConcurrencyRetryBehavior · Severity: P2 · Test: → no lost update.

---

## UC-24-09 — Validace, fail-fast a cross-field invarianty (Options validator + composed validator)

- **Actor / role:** system (startup + per-resolve)
- **Precondition:** Bind `RagTuningOptions` (a podřízené) z configu; registr.
- **Trigger:** (a) startup — `RagOptionsValidator : IValidateOptions<RagTuningOptions>` (fail-fast mimo Development); (b) per-resolve — `EffectiveConfigValidator` na composed snapshotu (UC-24-02); (c) PUT path — `Upsert*SettingsValidator` (UC-24-04/05).
- **Main flow:**
  1. **Startup**: každý knob z configu projde rozsahem/enum/typ; sensitive (Embedding key, RLS) fail-fast; secrets přítomny (UC-24-14); pokud cokoliv invalidní → host nenastartuje (mimo Dev) — vzor `JwtOptionsValidator`.
  2. **Cross-field invarianty** (na composed effective): `Overlap ≤ Size/2`; `TopN ≤ CandidateK ≤ Dense:MaxTopK`; `EfSearch ≥ topK`; `Graph:MaxHops ≤ platform cap`; `Rerank:LatencyBudgetMs ≤ Retrieval:OverallTimeoutMs`; `Fusion:MinScore ≤ 1`; `Freshness:AppendMaxVersions ≥ 1`. Porušení ⇒ `BusinessRuleException("rag.config.cross_field_invalid")` (422) na dotazu / 400 na PUT (preventive).
  3. **PUT validace**: registr (key/scope/range/cap) + secret deny.
- **Postcondition / záruky:** Žádný neplatný config se nedostane do běhu (startup) ani do dotazu (composed). Chyby mají errorCode v `SharedResource.resx` (en+cs).
- **Tenancy / permissions:** N/A (technický mechanismus); composed validace běží v kontextu dotazu.
- **Reuse / canonical pattern:** `JwtOptionsValidator` (fail-fast); FluentValidation `.WithErrorCode` dle `RegisterUserValidator`; `BusinessRuleException`. · **Data dotčena:** — · **Eventy:** — · **Priorita:** P0

### Edge cases UC-24-09
- **EC-24-09-01 — Invalidní config při startu (prod)** · Trigger: `Rag:Chunk:Size=-1` v appsettings. · Očekávané chování: host nenastartuje, `rag.config.invalid` v logu. · Mechanismus: IValidateOptions fail-fast · Severity: P0 · Test: bad config → startup throw.
- **EC-24-09-02 — Invalidní config v Dev** · Trigger: stejné v Development. · Očekávané chování: hlasitý WARN + clamp na default, start pokračuje (dev pohodlí). · Mechanismus: env-aware validator · Severity: P2 · Test: Dev → warn, no throw.
- **EC-24-09-03 — Overlap > Size/2** · Trigger: Size=512, Overlap=400. · Očekávané chování: cross_field 422/400. · Mechanismus: cross-field · Severity: P1 · Test: → reject.
- **EC-24-09-04 — EfSearch < topK** · Trigger: EfSearch=10, TopN=50. · Očekávané chování: cross_field reject (HNSW by nevrátil dost). · Mechanismus: cross-field · Severity: P1 · Test: → reject.
- **EC-24-09-05 — LatencyBudget > OverallTimeout** · Trigger: rerank budget 9000, overall 8000. · Očekávané chování: cross_field reject (rerank by nikdy nedoběhl v budgetu). · Mechanismus: cross-field · Severity: P1 · Test: → reject.
- **EC-24-09-06 — Enum mimo množinu** · Trigger: `Lexical:Provider=foo`. · Očekávané chování: startup fail-fast / PUT 400. · Mechanismus: enum check · Severity: P0 · Test: → reject.
- **EC-24-09-07 — Dimensions nekompatibilní s modelem** · Trigger: model `text-embedding-3-small` (max 1536) + Dimensions=3072. · Očekávané chování: cross_field `rag.config.dimensions_model_mismatch`. · Mechanismus: model-dimension matice · Severity: P0 · Test: → reject.
- **EC-24-09-08 — Chybějící en/cs překlad errorCode** · Trigger: nový errorCode bez resx. · Očekávané chování: test `SharedResourceParity` selže (build). · Mechanismus: zákon §8 i18n · Severity: P1 · Test: missing key → red.
- **EC-24-09-09 — Composed validace přidá latenci** · Trigger: per-request validace. · Očekávané chování: < 1 ms (in-memory, ~10 invariantů). · Mechanismus: levná validace · Severity: P3 · Test: latence OK.
- **EC-24-09-10 — Restart-required validace odložená** · Trigger: změna dimensions za běhu. · Očekávané chování: validuje se proti **uložené budoucí** i **aktivní runtime** hodnotě; runtime path používá starou (konzistentní). · Mechanismus: UC-24-10 dual-state · Severity: P1 · Test: dual validace.
- **EC-24-09-11 — Cap reference v rozsahu neexistuje** · Trigger: registr odkáže `CandidateK` jako mez pro `TopN`, ale CandidateK chybí. · Očekávané chování: startup self-check (EC-24-01-06) chytí. · Mechanismus: registr self-validace · Severity: P1 · Test: → startup throw.

---

## UC-24-10 — Runtime reload (`IOptionsMonitor`) vs restart-required knoby

- **Actor / role:** system · platform-admin (mění appsettings/secret store)
- **Precondition:** Global knoby bind přes `IOptionsMonitor<RagTuningOptions>`; DB override (`RagSetting`) čteno per-request (vždy live). Registr značí restart-required.
- **Trigger:** (a) změna `appsettings`/config provideru → `IOptionsMonitor.OnChange`; (b) PUT override runtime knobu → live; (c) PUT restart-required knobu → warn.
- **Main flow:**
  1. **Runtime knoby** (většina): změna v configu (OnChange) nebo DB override se projeví na **příští** `Resolve` bez restartu — resolver čte aktuální Options + live DB. Žádná stale cache, nebo cache invalidovaná `RagConfigChangedIntegrationEvent`.
  2. **Restart-required knoby** (`Embedding:Model`, `Embedding:Dimensions`, `Lexical:Provider`, `Rerank:Provider`, `Extraction:Model`, `Eval:JudgeModel`, `Providers:*`): jejich runtime objekty (provider klienti, pgvector kolony) se vytvářejí při startu; změna se **zapíše**, ale runtime běží starou hodnotou; response/log warn `rag.config.restart_required` (+ `RequiresReembed` u modelu/dimenze). Resolver pro tyto vrací **aktivní (startovní)** hodnotu, ne uloženou budoucí.
- **Postcondition / záruky:** Žádné „tiché" selhání (změna modelu za běhu by jinak mixovala dimenze) — restart-required jsou explicitně odděleny, varování viditelné, runtime konzistentní.
- **Tenancy / permissions:** Global config change = platform-admin (infra); DB override permission dle UC-24-04/05.
- **Reuse / canonical pattern:** `IOptionsMonitor` pattern; cache invalidation přes outbox event dle `RegisterUserHandler.cs:22`. · **Data dotčena:** in-memory Options cache; `hybridrag_rag_settings` · **Eventy:** `RagConfigChangedIntegrationEvent` · **Priorita:** P0

### Edge cases UC-24-10
- **EC-24-10-01 — Runtime knob change za běhu** · Trigger: změna `Rag:Fusion:TopN` v configu. · Očekávané chování: příští dotaz použije novou hodnotu, bez restartu. · Mechanismus: OnChange / live DB · Severity: P1 · Test: change → next query reflektuje.
- **EC-24-10-02 — Restart-required change za běhu (no effect + warn)** · Trigger: `Rag:Embedding:Dimensions` change. · Očekávané chování: runtime beze změny, warn, uloženo k restartu. · Mechanismus: registr flag · Severity: P0 · Test: change → runtime stejný, warn emitted.
- **EC-24-10-03 — Cache effective stale po PUT** · Trigger: PUT override, cache neinvaliduje. · Očekávané chování: `RagConfigChangedIntegrationEvent` invaliduje cache (Redis single-flight) → next resolve fresh. · Mechanismus: outbox event · Severity: P1 · Test: PUT → cache miss → fresh.
- **EC-24-10-04 — OnChange race (config reload uprostřed dotazu)** · Trigger: reload během retrievalu. · Očekávané chování: dotaz drží snapshot z počátku (immutable `EffectiveRagConfig`), nemíchá. · Mechanismus: snapshot capture · Severity: P1 · Test: reload mid-query → konzistentní snapshot.
- **EC-24-10-05 — Provider klient neexistuje pro novou hodnotu** · Trigger: restart-required model change na model bez klienta. · Očekávané chování: uloženo + warn; po restartu by validator fail-fast pokud klient chybí. · Mechanismus: startup validace · Severity: P1 · Test: bad provider → next-start throw.
- **EC-24-10-06 — Multi-instance: jedna instance restartovaná, druhá ne** · Trigger: rolling restart. · Očekávané chování: během rollu mohou instance dočasně lišit (drift); reembed gate (UC-24-16) zabraňuje míchání dimenzí (vektory tagované modelem). · Mechanismus: vektory nesou model tag, dotaz filtruje na aktivní model · Severity: P0 · Test: mixed instances → žádné cross-dimension query.
- **EC-24-10-07 — Warn ale admin si nevšimne** · Trigger: restart-required uloženo. · Očekávané chování: kromě response warn i metrika `platform.rag.config.restart_pending` gauge + audit, aby šlo alertovat. · Mechanismus: OTel metrika · Severity: P2 · Test: gauge > 0.
- **EC-24-10-08 — Reset restart-required na default za běhu** · Trigger: DELETE restart-required override. · Očekávané chování: stejné — projeví se až po restartu, warn. · Mechanismus: registr flag · Severity: P2 · Test: delete → pending restart.
- **EC-24-10-09 — IOptionsMonitor pro DB-only knob** · Trigger: knob existuje jen jako DB override (ne v appsettings). · Očekávané chování: live z DB každý request (žádný monitor potřeba). · Mechanismus: per-request DB read · Severity: P3 · Test: DB-only → live.
- **EC-24-10-10 — Config reload zavede invalidní hodnotu** · Trigger: admin opraví appsettings špatně, reload. · Očekávané chování: `IValidateOptions` na reloadu odmítne → ponechá poslední validní + ERROR log (neshodí běžící proces). · Mechanismus: validate-on-reload · Severity: P1 · Test: bad reload → keep last good.

---

## UC-24-11 — Config versioning (optimistic concurrency + historie + pinning)

- **Actor / role:** system · admin (čte verzi pro optimistic PUT)
- **Precondition:** `RagSetting` / preset nese `ConfigVersion` (monotónní per tenant nebo per řádek xmin). Globální „effective config version" = hash relevantních vrstev.
- **Trigger:** každý PUT (UC-24-04/05/08) inkrementuje verzi; každý Resolve (UC-24-02) razí `ConfigVersionStamp` do trace/saga.
- **Main flow:**
  1. PUT může nést `If-Match: {version}` (optimistic) → mismatch ⇒ 409 `rag.config.version_conflict` (cross-ref EC-24-04-05 xmin).
  2. Resolver spočítá deterministický `ConfigVersionStamp` (hash globálních defaults verze + tenant override verze + collection override verze + preset verze + query override). Tento stamp se uloží do `RagTrace`/`IngestSaga` → reprodukovatelnost.
  3. Změny knobů jsou auditovány (UC-24-04 audit) → historie kdo/kdy/co (cross-ref oblast 25); volitelný `hybridrag_rag_config_history` append-only pro time-travel ladění.
- **Postcondition / záruky:** Každá odpověď je vázána na konkrétní config verzi → A/B a debugging „proč jiná odpověď" je možný. Saga/ingest pinuje verzi (konzistence dlouhého běhu).
- **Tenancy / permissions:** Verze a historie tenant-scoped (RLS).
- **Reuse / canonical pattern:** xmin/ConcurrencyRetryBehavior; audit `AuditInterceptor`; append-only vzor dle billing ledger filozofie. · **Data dotčena:** `hybridrag_rag_settings.ConfigVersion`, `hybridrag_audit_entries`, (volitelně) `hybridrag_rag_config_history` · **Eventy:** `RagConfigChangedIntegrationEvent` · **Priorita:** P1

### Edge cases UC-24-11
- **EC-24-11-01 — Optimistic PUT konflikt** · Trigger: `If-Match` stará verze. · Očekávané chování: 409 version_conflict. · Mechanismus: xmin/version compare · Severity: P1 · Test: stale If-Match → 409.
- **EC-24-11-02 — PUT bez If-Match (last-write-wins)** · Trigger: PUT bez hlavičky. · Očekávané chování: poslední vyhraje (kompatibilní), audit zachytí oba. · Mechanismus: optional optimistic · Severity: P2 · Test: → last wins, 2 audit entries.
- **EC-24-11-03 — ConfigVersionStamp deterministický** · Trigger: stejné vrstvy 2× resolve. · Očekávané chování: stejný stamp. · Mechanismus: deterministický hash · Severity: P1 · Test: stamp stable.
- **EC-24-11-04 — Stamp se změní po PUT** · Trigger: override change. · Očekávané chování: nový stamp v dalším trace. · Mechanismus: hash zahrnuje verze · Severity: P1 · Test: change → new stamp.
- **EC-24-11-05 — Saga pinuje starou verzi** · Trigger: config change během ingestu. · Očekávané chování: saga doběhne s pinned stampem (EC-24-02-09). · Mechanismus: stamp v `IngestSaga` · Severity: P1 · Test: change mid-saga → pinned.
- **EC-24-11-06 — Historie tenant IDOR** · Trigger: čtení cizí historie. · Očekávané chování: 404 (RLS). · Mechanismus: RLS · Severity: P0 · Test: → 404.
- **EC-24-11-07 — Stamp kolize (různé configy stejný hash)** · Trigger: hash collision teoretická. · Očekávané chování: stamp obsahuje i seznam aktivních override verzí (ne jen hash) → kolize neztratí informaci pro debug. · Mechanismus: composite stamp · Severity: P3 · Test: různé configy → různé stampy.
- **EC-24-11-08 — Global default version (deployment) v stampu** · Trigger: nový deploy mění default. · Očekávané chování: stamp zahrnuje `RagDefaultsVersion` (z assembly/config) → odlišení „default se změnil deployem". · Mechanismus: defaults version · Severity: P2 · Test: deploy change → stamp shift.
- **EC-24-11-09 — History append-only nelze mazat** · Trigger: pokus smazat historii. · Očekávané chování: append-only (jako audit) — nemaže se (GDPR: anonymizace UserId přes crypto-shred, ne delete). · Mechanismus: append-only + GDPR pattern · Severity: P2 · Test: no physical delete.

---

## UC-24-12 — Trasování effective configu ke každé odpovědi/trace (debugging „proč tahle odpověď")

- **Actor / role:** system (zapisuje) · user/admin (čte trace) · platform-admin (A/B analýza)
- **Precondition:** Trace infrastruktura (`RagConversation/Turn/Trace`, oblast 19 OTel). Resolver produkuje effective snapshot + provenance (UC-24-02).
- **Trigger:** každý retrieval/chat/MCP dotaz — po `Resolve` se effective config připne k `RagTrace` a k OTel spanu.
- **Main flow:**
  1. Z effective snapshotu se vybere **jen trasovaná podmnožina** knobů (sloupec „Trasovaný=ano" v registru — neukládat BatchSize/TimeoutMs šum) + `preset` + `ConfigVersionStamp` + provenance vrstvy.
  2. Uloží se do `RagTrace.EffectiveConfigJson` (kompaktní JSON) a zrcadlí do OTel span attributes `rag.config.{key}` + `rag.config.version` + `rag.config.preset` (GenAI semconv, oblast 19) — sledovatelné v APM.
  3. Endpoint `GET /v1/hybridrag/conversations/{id}/turns/{turnId}/trace` (owner/admin) vrací effective config použitý pro tu odpověď → uživatel/admin ladí „použilo se TopN=12 z presetu accurate, rerank on, graph off (collection)".
  4. **Secrets nikdy v trace** (UC-24-14); trasují se jen tunable knoby, ne API klíče.
- **Postcondition / záruky:** Každá odpověď je plně reprodukovatelná z trace (config + version). Bez effective configu by trace byl „slepý" → proto chybějící effective je bug (EC-24-12-01).
- **Tenancy / permissions:** Trace owner-scoped (`IUserOwned`/RLS) → user vidí svůj; admin přes `Rag.Manage`/`AuditRead`; cizí trace = 404.
- **Reuse / canonical pattern:** trace ukládání jako oblast 19; metriky `PlatformMetrics.Meter` (`platform.rag.*`); read `GetProfileHandler.cs:12`; OTel GenAI span per stage. · **Data dotčena:** `hybridrag_rag_traces` (`EffectiveConfigJson`, `ConfigVersionStamp`) · **Eventy:** — · **Priorita:** P0

### Edge cases UC-24-12
- **EC-24-12-01 — Trace bez effective configu** · Trigger: bug — resolver neuložil snapshot. · Očekávané chování: považováno za defekt; integration test vynucuje, že každý dokončený turn má neprázdný `EffectiveConfigJson` + `ConfigVersionStamp`. · Mechanismus: invariant + test · Severity: P0 · Test: turn bez config → red.
- **EC-24-12-02 — Secret v trace (leak)** · Trigger: omylem serializován secret. · Očekávané chování: nemožné — trace bere jen registr-trasované knoby (žádné secrets v registru); navíc redaction guard. · Mechanismus: registr filter + redaction · Severity: P0 (security) · Test: trace neobsahuje `*ApiKey`.
- **EC-24-12-03 — Provenance ukazuje špatnou vrstvu** · Trigger: override z collection označen jako Default. · Očekávané chování: provenance přesná (test páruje resolver výstup ↔ trace). · Mechanismus: resolver provenance · Severity: P1 · Test: collection override → provenance=Collection.
- **EC-24-12-04 — Velký config nafoukne trace** · Trigger: 40 trasovaných knobů × miliony turnů. · Očekávané chování: jen trasovaná podmnožina + kompaktní JSON; volitelně diff-from-default (ukládat jen odchylky od defaultu) pro úsporu. · Mechanismus: diff-encoding · Severity: P2 · Test: trace size pod prahem.
- **EC-24-12-05 — A/B: dvě odpovědi, různý config** · Trigger: porovnání dvou turnů. · Očekávané chování: trace umožní diff configů (které knoby se lišily) → vysvětlí rozdíl kvality. · Mechanismus: ConfigVersionStamp + JSON diff · Severity: P1 · Test: diff endpoint vrací rozdíly.
- **EC-24-12-06 — Trace IDOR** · Trigger: čtení cizího trace. · Očekávané chování: 404 (RLS). · Mechanismus: zákon §10 · Severity: P0 · Test: → 404.
- **EC-24-12-07 — OTel span attribute cardinality** · Trigger: vysoká kardinalita (per-query config). · Očekávané chování: do span jdou jen low-cardinality klíče (preset, version, bool toggly); high-card (TopN konkrétní) do trace JSON ne do metrik. · Mechanismus: metrika vs trace split (oblast 19) · Severity: P2 · Test: žádná metrika label-explosion.
- **EC-24-12-08 — Query override v trace** · Trigger: per-query override. · Očekávané chování: trace ukáže override hodnoty + provenance=Query, aby šlo replikovat. · Mechanismus: L3 provenance · Severity: P1 · Test: override → in trace.
- **EC-24-12-09 — Config změna mezi turny konverzace** · Trigger: admin mění config mid-conversation. · Očekávané chování: každý turn nese svůj stamp (turny v jedné konverzaci mohou mít různé stampy) → vysvětlí drift v rámci konverzace. · Mechanismus: per-turn stamp · Severity: P1 · Test: turny různé stampy.
- **EC-24-12-10 — Trace pro selhaný dotaz** · Trigger: dotaz selže (timeout). · Očekávané chování: effective config se i tak zapíše (víme s jakým configem to selhalo) → ladění. · Mechanismus: capture před exekucí · Severity: P1 · Test: failed turn má config.

---

## UC-24-13 — A/B config experiment (řízené porovnání dvou konfigurací)

- **Actor / role:** platform-admin / tenant-admin (`Rag.Manage`) · system (deterministické přiřazení varianty)
- **Precondition:** Experiment = pojmenovaná dvojice (control, variant) config sad/presetů + alokace (% trafficu / deterministicky per-user-hash). Volitelný eval (oblast 22) měří kvalitu.
- **Trigger:** CRUD `POST/PUT/DELETE /v1/hybridrag/admin/config/experiments` + start/stop; runtime resolver konzultuje aktivní experiment.
- **Main flow:**
  1. Admin definuje experiment: control = baseline (preset/override), variant = alternativa (např. CandidateK 100 vs 200, rerank on/off), alokace 50/50, scope (collection/tenant), trvání.
  2. Resolver při dotazu: pokud collection/tenant má aktivní experiment, deterministicky přiřadí variantu (`hash(userId + experimentId) % 100 < allocation`) → aplikuje variant config jako vrstvu (provenance=`Experiment`), zapíše `experimentId`+`variant` do trace (UC-24-12).
  3. Eval/metriky agregují kvalitu per varianta (`platform.rag.experiment.{quality}` s label `variant`) → admin vyhodnotí, „promote variant" zkopíruje variant config do baseline override.
- **Postcondition / záruky:** Deterministické přiřazení (stejný user → stejná varianta po dobu experimentu), bez crossování mezi turny jedné konverzace. Žádné porušení capů (variant config taky cap-validovaný).
- **Tenancy / permissions:** Experiment tenant/collection scoped (RLS); `Rag.Manage`.
- **Reuse / canonical pattern:** CRUD slice; trace UC-24-12; metriky `PlatformMetrics.Meter`. · **Data dotčena:** `hybridrag_rag_experiments`, `hybridrag_rag_traces` (experiment label), `hybridrag_audit_entries` · **Eventy:** `RagConfigChangedIntegrationEvent` (start/stop) · **Priorita:** P2

### Edge cases UC-24-13
- **EC-24-13-01 — Variant config překročí cap** · Trigger: variant `TopN=5000`. · Očekávané chování: 400 při uložení experimentu. · Mechanismus: cap validace variantu · Severity: P0 · Test: → 400.
- **EC-24-13-02 — Deterministické přiřazení stabilní** · Trigger: user 10× dotaz. · Očekávané chování: stejná varianta pokaždé (per experiment). · Mechanismus: hash(user+exp) · Severity: P1 · Test: stable assignment.
- **EC-24-13-03 — Konzistence v rámci konverzace** · Trigger: multi-turn konverzace. · Očekávané chování: celá konverzace drží jednu variantu (přiřazení pinned na konverzaci). · Mechanismus: pin na conversationId · Severity: P1 · Test: turny stejná varianta.
- **EC-24-13-04 — Dva experimenty na stejné collection** · Trigger: překryv. · Očekávané chování: 409 `rag.config.experiment_overlap` nebo definovaná priorita/mutual-exclusion. · Mechanismus: overlap guard · Severity: P1 · Test: overlap → 409.
- **EC-24-13-05 — Experiment a explicit override konflikt** · Trigger: collection má explicit `Rag:Rerank:Enabled=false` i experiment na rerank. · Očekávané chování: definovaná precedence (experiment > collection override NEBO explicit > experiment) — dokumentovaná; trace ukáže skutečný zdroj. · Mechanismus: precedence rule + provenance · Severity: P1 · Test: trace provenance correct.
- **EC-24-13-06 — Experiment expirace** · Trigger: trvání uplyne. · Očekávané chování: resolver přestane aplikovat (read live stav), žádné staré přiřazení. · Mechanismus: time check (`IClock`) · Severity: P2 · Test: po expiraci → baseline.
- **EC-24-13-07 — Promote variant** · Trigger: admin promote. · Očekávané chování: variant config se zapíše jako tenant/collection override (UC-24-04/05) + experiment ukončen + audit. · Mechanismus: copy + stop · Severity: P2 · Test: promote → override set.
- **EC-24-13-08 — Trace bez experiment labelu při aktivním experimentu** · Trigger: bug. · Očekávané chování: test vynucuje experiment label v trace, když experiment aktivní. · Mechanismus: invariant · Severity: P1 · Test: → red on missing label.
- **EC-24-13-09 — IDOR experiment** · Trigger: cizí experiment. · Očekávané chování: 404. · Mechanismus: RLS · Severity: P0 · Test: → 404.
- **EC-24-13-10 — Alokace mimo 0–100** · Trigger: allocation=150. · Očekávané chování: 400 range. · Mechanismus: validace · Severity: P2 · Test: → 400.

---

## UC-24-14 — Secrets: oddělení, fail-fast, maskování (NIKDY v tunable registru ani effective-config)

- **Actor / role:** system · platform-admin (nastavuje secrets v secret store, ne přes API)
- **Precondition:** Secrets (`OpenAi:ApiKey`, `Rerank:Cohere:ApiKey`, případné provider klíče, RLS `RuntimePassword`) bind do **samostatných** Options tříd (`RagSecretsOptions`), nikdy do `RagTuningOptions`; načítané z `IConfiguration` (env/secret store), nikdy z DB `RagSetting`.
- **Trigger:** startup (fail-fast validace přítomnosti mimo Dev) + nikdy přes config API.
- **Main flow:**
  1. `RagSecretsOptionsValidator` (mimo Development): vyžaduje neprázdné klíče pro aktivní providery (např. pokud `Rerank:Provider=cohere` → `Rerank:Cohere:ApiKey` musí být) → jinak host nenastartuje (`rag.config.secret_missing`), vzor `JwtOptionsValidator`/`RlsBootstrapper`.
  2. Registr (UC-24-01) **fyzicky neobsahuje** žádný secret key (self-check odmítne secret-like, EC-24-01-05); PUT endpointy odmítají secret keys (EC-24-04-08); effective-config (UC-24-03) a trace (UC-24-12) secrets vůbec nevypisují (ne maskované — neexistují jako knoby).
  3. Pokud by se někde logovala config mapa, redaction middleware maskuje hodnoty matchující secret patterny (`****`), defense-in-depth.
- **Postcondition / záruky:** Žádný secret se nikdy nedostane do API odpovědi, trace, auditu hodnot, ani logu. Aktivace provideru bez jeho secretu = startup fail-fast.
- **Tenancy / permissions:** Secrets jsou platform-infra, ne tenant data — žádný tenant je nikdy nečte ani nemění přes API.
- **Reuse / canonical pattern:** `JwtOptionsValidator` fail-fast; redaction; secrets layering dle CLAUDE.md §8 „Secrets". · **Data dotčena:** žádná DB (jen `IConfiguration`) · **Eventy:** — · **Priorita:** P0

### Edge cases UC-24-14
- **EC-24-14-01 — Aktivní provider bez secretu (prod)** · Trigger: `Rerank:Provider=cohere`, klíč chybí. · Očekávané chování: startup fail-fast `rag.config.secret_missing`. · Mechanismus: secrets validator · Severity: P0 · Test: → startup throw.
- **EC-24-14-02 — Secret v PUT override** · Trigger: EC-24-04-08. · Očekávané chování: 400. · Mechanismus: deny · Severity: P0 · Test: → 400.
- **EC-24-14-03 — Secret v effective-config GET** · Trigger: očekávání leaku. · Očekávané chování: chybí úplně (ne maskováno). · Mechanismus: registr bez secrets · Severity: P0 · Test: žádný secret key.
- **EC-24-14-04 — Secret v trace** · Trigger: EC-24-12-02. · Očekávané chování: chybí. · Mechanismus: trasované knoby only · Severity: P0 · Test: clean trace.
- **EC-24-14-05 — Secret v audit hodnotě** · Trigger: změna secretu (přes config) — nikdy přes audited `RagSetting`. · Očekávané chování: secrets nejdou přes audited DB context → nikdy v `hybridrag_audit_entries`. · Mechanismus: secrets mimo DbContext · Severity: P0 · Test: audit neobsahuje secret.
- **EC-24-14-06 — Secret v logu** · Trigger: debug log config mapy. · Očekávané chování: redaction maskuje `****`. · Mechanismus: redaction middleware · Severity: P1 · Test: log scrub.
- **EC-24-14-07 — Dev placeholder secret** · Trigger: Development bez reálného klíče. · Očekávané chování: validator v Dev povolí placeholder (fake gateway), warn. · Mechanismus: env-aware · Severity: P2 · Test: Dev → start s fake.
- **EC-24-14-08 — Secret rotace za běhu** · Trigger: secret store rotuje klíč. · Očekávané chování: `IOptionsMonitor` reload secretu → příští provider volání nový klíč, bez restartu (pokud klient čte přes monitor) NEBO restart-required dle implementace klienta (dokumentováno). · Mechanismus: monitor / restart · Severity: P1 · Test: rotace → nové volání nový klíč.
- **EC-24-14-09 — Secret pattern false-positive (knob `Rag:Pricing:KeyRate`)** · Trigger: legitimní knob obsahuje „Key". · Očekávané chování: registr secret deny použije přesnější pattern (`*:ApiKey`,`*:Secret`,`*:Password`,`*ConnectionString`), ne substring „Key" → false-positive se nestane. · Mechanismus: přesné suffix patterny · Severity: P2 · Test: pricing knob OK.

---

## UC-24-15 — Feature toggles (Graph Enabled/Required, Rerank on/off, Contextualize Required, Lexical Provider switch)

- **Actor / role:** tenant/collection-admin (`Rag.Manage`/`Rag.CollectionManage`) · user (per-query toggly z allowlistu) · system
- **Precondition:** Knoby `Rag:Graph:Enabled/Required`, `Rag:Rerank:Enabled`, `Rag:Contextualize:Required`, `Rag:Lexical:Provider` v registru se svými scope-sety.
- **Trigger:** PUT settings (UC-24-04/05), per-query override (UC-24-07 pro `Graph:Enabled`/`Rerank:Enabled`), nebo preset (UC-24-08).
- **Main flow:**
  1. **Graph:Enabled** (runtime, query-allowed) zapíná graf fázi; **Graph:Required** (collection/tenant) vynutí graf — pokud graf nedostupný (žádná extrakce), dotaz 422 `rag.graph.required_unavailable` místo tichého degradace.
  2. **Rerank:Enabled** (runtime, query-allowed) zapíná rerank stage; vypnutí ho přeskočí (fusion výsledek přímo).
  3. **Contextualize:Required** (runtime, ovlivní novou ingest) vynutí kontextualizaci chunků při ingestu (cross-ref oblast 03).
  4. **Lexical:Provider** (restart-required) přepne BM25 backend (`pg_search` ↔ `ts_rank`) — restart-required (extension/index), warn při změně za běhu (UC-24-10).
- **Postcondition / záruky:** Toggly se promítnou do pipeline přes effective config + provenance v trace. Restart-required toggle (Lexical:Provider) explicitně oddělen. Required toggly fail-loud, ne silent-degrade.
- **Tenancy / permissions:** Dle scope knobu; query toggly jen z allowlistu pod capy.
- **Reuse / canonical pattern:** resolver UC-24-02; trace UC-24-12; feature toggle = knob, NE separátní mechanismus (zákon §4). · **Data dotčena:** `hybridrag_rag_settings` · **Eventy:** `RagConfigChangedIntegrationEvent` · **Priorita:** P1

### Edge cases UC-24-15
- **EC-24-15-01 — Graph:Required ale graf nedostupný** · Trigger: required=true, collection bez grafu. · Očekávané chování: 422 `rag.graph.required_unavailable` (fail-loud). · Mechanismus: required check · Severity: P0 · Test: → 422.
- **EC-24-15-02 — Graph:Enabled=false ale query chce true** · Trigger: collection vypnutý graf, query zapíná. · Očekávané chování: respektováno (query-allowed), pokud graf dostupný; jinak prázdná graph fáze + trace flag. · Mechanismus: UC-24-07 · Severity: P1 · Test: query graph on → ran/empty.
- **EC-24-15-03 — Rerank vynuceně off platform-wide** · Trigger: `Rerank:Provider=none` global. · Očekávané chování: `Rerank:Enabled` se ignoruje (provider none ⇒ nelze rerankovat), trace flag „rerank_skipped_no_provider". · Mechanismus: provider gate · Severity: P1 · Test: provider none → rerank skip.
- **EC-24-15-04 — Contextualize:Required změna neovlivní staré chunky** · Trigger: zapnutí required po ingestu. · Očekávané chování: jen nové ingesty kontextualizují; staré beze změny (nebo reembed/recontextualize job, cross-ref 03/09). · Mechanismus: ingest-time toggle · Severity: P1 · Test: staré chunky beze změny.
- **EC-24-15-05 — Lexical:Provider switch za běhu** · Trigger: `pg_search`→`ts_rank` runtime. · Očekávané chování: restart-required, runtime stále starý provider, warn (UC-24-10). · Mechanismus: registr flag · Severity: P0 · Test: → no runtime switch, warn.
- **EC-24-15-06 — Provider switch na nedostupnou extension** · Trigger: `pg_search` ale extension chybí v DB. · Očekávané chování: startup/health fail `rag.lexical.provider_unavailable`. · Mechanismus: extension check (carve-out BM25) · Severity: P0 · Test: missing ext → fail.
- **EC-24-15-07 — Toggle v trace** · Trigger: dotaz s graph on. · Očekávané chování: trace má `graph.enabled=true` + provenance. · Mechanismus: trasované toggly · Severity: P1 · Test: trace flag present.
- **EC-24-15-08 — Required toggle per-query downgrade pokus** · Trigger: collection `Graph:Required=true`, query `Graph:Enabled=false`. · Očekávané chování: query nesmí obejít required → ignorováno + warn (required > query). · Mechanismus: precedence required-over-query · Severity: P1 · Test: required honored.
- **EC-24-15-09 — Rerank on bez secretu (cohere)** · Trigger: `Rerank:Enabled=true`, `Rerank:Provider=cohere`, klíč chybí. · Očekávané chování: startup fail-fast (UC-24-14) — nelze aktivovat rerank bez secretu. · Mechanismus: secrets validator · Severity: P0 · Test: → startup throw.
- **EC-24-15-10 — Contextualize required ale model nedostupný** · Trigger: kontextualizační model chybí. · Očekávané chování: ingest fáze fail `rag.contextualize.model_unavailable` (saga retry/dead-letter, oblast 04). · Mechanismus: model gate · Severity: P1 · Test: → ingest fail handled.

---

## UC-24-16 — Drift resolution & config key migration (kanonizace, model-drift → reembed)

- **Actor / role:** system (startup migrace klíčů) · platform-admin (upgrade)
- **Precondition:** Existují legacy/drifting klíče (z různých částí katalogu): `Rag:Fusion:K` vs `Rag:Rrf:K`; `Rag:Chunk:Size` vs `Rag:Chunking:*`; `Rag:Hybrid:CandidateK` vs `Rag:Fusion:TopN`; `Rag:Retrieval:MinScore` vs `Rag:MinSimilarity` vs `Rag:Dense:MinSimilarity`; `Rag:Embed:Model` vs `Rag:Embedding:Model` vs `Rag:Embeddings:CurrentModel`; `Rag:Retrieval:TimeoutMs` vs `Rag:OverallTimeout` vs `Rag:Rerank:TimeoutMs`.
- **Trigger:** startup `RagConfigKeyMigrator` (před bindingem) + `RagSetting` upgrade migrace; model change publikuje reembed.
- **Main flow:**
  1. **Kanonizace (drift table):** migrator mapuje legacy → canonical (viz tabulka níže), WARN při nalezení aliasu, fail pokud legacy i canonical mají **rozdílné** hodnoty současně (`rag.config.drift_conflict`).
  2. **DB upgrade:** EF migrace přejmenuje legacy `RagSetting.Key` na canonical (idempotentně); registr od té chvíle zná jen canonical.
  3. **Model drift → reembed:** změna `Rag:Embedding:Model`/`Dimensions` (UC-24-04/05) publikuje `RagEmbeddingModelChangedIntegrationEvent` → reembed flow (oblast 03/09); vektory nesou `EmbeddingModel`+`Dimensions` tag; dotazy filtrují na **aktivní** model (žádné míchání dimenzí), dokud reembed nedoběhne; `Reembed` job (`Modules:HybridRag:Jobs:Reembed`) zpracovává po dávkách (`Rag:Reembed:MaxPerRun`).
- **Postcondition / záruky:** Po startu existují JEN kanonické klíče (drift eliminován). Žádné dvě interpretace téhož knobu. Model change nikdy nezpůsobí cross-dimension dotaz.

### Drift → kanonický mapping (frozen)

| Legacy klíč(e) | Kanonický klíč | Poznámka |
|---|---|---|
| `Rag:Fusion:K`, `Rag:Rrf:K` | `Rag:Fusion:Rrf:K` | sjednocení RRF k |
| `Rag:Chunk:Size`, `Rag:Chunking:Size` | `Rag:Chunk:Size` | + `Overlap`, `MaxChunksPerDocument` pod `Rag:Chunk:*` |
| `Rag:Hybrid:CandidateK` | `Rag:Fusion:CandidateK` | candidate pool |
| `Rag:Fusion:TopN` (jako candidate) | `Rag:Fusion:TopN` | final cut (oddělené od CandidateK) |
| `Rag:Retrieval:MinScore`, `Rag:MinSimilarity` | `Rag:Retrieval:MinScore` (post-pipeline) | + `Rag:Dense:MinSimilarity` (dense stage) + `Rag:Fusion:MinScore` (post-fusion) — tři DISTINKTNÍ prahy |
| `Rag:Dense:MinSimilarity` | `Rag:Dense:MinSimilarity` | dense-only |
| `Rag:Embed:Model`, `Rag:Embedding:Model`, `Rag:Embeddings:CurrentModel` | `Rag:Embedding:Model` | jeden model klíč |
| `Rag:Retrieval:TimeoutMs`, `Rag:OverallTimeout` | `Rag:Retrieval:OverallTimeoutMs` | celý pipeline timeout |
| `Rag:Rerank:TimeoutMs` | `Rag:Rerank:TimeoutMs` | jen rerank stage (oddělený) |

- **Tenancy / permissions:** Migrace je platform-system (běží při startu/MigrationService); reembed event tenant/collection scoped.
- **Reuse / canonical pattern:** EF migrace dle CLAUDE.md §7; outbox event `RegisterUserHandler.cs:22`; reembed job dle `Modules:HybridRag:Jobs:Reembed`. · **Data dotčena:** `hybridrag_rag_settings`, `hybridrag_chunks` (model tag), `hybridrag_documents` · **Eventy:** `RagEmbeddingModelChangedIntegrationEvent` · **Priorita:** P0

### Edge cases UC-24-16
- **EC-24-16-01 — Legacy i canonical klíč s rozdílnou hodnotou** · Trigger: `Rag:Fusion:K=60` i `Rag:Rrf:K=80`. · Očekávané chování: startup `rag.config.drift_conflict` fail-fast (mimo Dev) — nelze hádat. · Mechanismus: migrator conflict check · Severity: P0 · Test: konflikt → throw.
- **EC-24-16-02 — Legacy klíč osamoceně** · Trigger: jen `Rag:Rrf:K`. · Očekávané chování: přemapováno na canonical + WARN. · Mechanismus: migrator · Severity: P1 · Test: legacy → canonical, warn.
- **EC-24-16-03 — Model change vynutí reembed** · Trigger: EC-24-04-12. · Očekávané chování: reembed event, vektory tagované, žádné cross-dimension. · Mechanismus: model tag + reembed job · Severity: P0 · Test: change → reembed, dotazy konzistentní.
- **EC-24-16-04 — Dotaz během reembedu (částečně přemodelováno)** · Trigger: půlka chunků nový model. · Očekávané chování: dotaz použije JEN aktivní model vektory (filtr na model tag); staré ignoruje dokud nepřevedeno, nebo dual-read s clearly oddělenými indexy. · Mechanismus: model tag filtr (oblast 09) · Severity: P0 · Test: mixed state → no dimension mismatch error.
- **EC-24-16-05 — Dimensions change bez modelu** · Trigger: jen `Dimensions` (Matryoshka truncation). · Očekávané chování: restart-required + reembed/re-truncate; pgvector kolona realokována migrací. · Mechanismus: dimensions migrace · Severity: P0 · Test: → reembed.
- **EC-24-16-06 — DB migrace přejmenování klíče idempotentní** · Trigger: re-run migrace. · Očekávané chování: idempotentní (UNIQUE, žádný duplikát). · Mechanismus: idempotency UNIQUE + catch `DbUpdateException` · Severity: P1 · Test: 2× run → stejný stav.
- **EC-24-16-07 — Reembed job stuck/timeout** · Trigger: reembed nedoběhne. · Očekávané chování: `ReconcileStuckIngest`/dedicated reconcile označí stuck, alert; dotazy fungují na starém modelu dokud reembed kompletní. · Mechanismus: reconcile job (oblast 04) · Severity: P1 · Test: stuck → alert, queries OK.
- **EC-24-16-08 — Tři distinktní MinScore zaměněny** · Trigger: někdo bere `Dense:MinSimilarity` jako post-fusion. · Očekávané chování: registr + dokumentace jasně oddělují tři prahy (dense / fusion / retrieval); validace nezaměňuje. · Mechanismus: tři samostatné descriptory · Severity: P1 · Test: tři klíče nezávislé.
- **EC-24-16-09 — Reembed event bez collection scope (fan-out celý tenant)** · Trigger: tenant-level model change. · Očekávané chování: reembed všech collections tenanta, po dávkách, idempotentně. · Mechanismus: per-collection fan-out · Severity: P1 · Test: tenant change → všechny collections reembed.
- **EC-24-16-10 — Migrator běží v Dev s konfliktem** · Trigger: drift conflict v Development. · Očekávané chování: WARN + preferuj canonical (dev pohodlí), neshazuje. · Mechanismus: env-aware · Severity: P2 · Test: Dev → warn, canonical wins.
- **EC-24-16-11 — Legacy klíč v DB `RagSetting` (ne jen config)** · Trigger: tenant override pod legacy klíčem. · Očekávané chování: EF data migrace přemapuje i DB override řádky na canonical. · Mechanismus: data migrace · Severity: P1 · Test: legacy `RagSetting` → canonical key.
- **EC-24-16-12 — Reembed během aktivního A/B na modelu** · Trigger: experiment varianta jiný model + reembed. · Očekávané chování: experiment varianta s jiným modelem vyžaduje vlastní reembed sadu (paralelní indexy per model) NEBO je model zakázán jako experiment knob (restart-required → není query-scoped). · Mechanismus: model není v query allowlistu → experiment na modelu jen jako infra, ne traffic-split · Severity: P1 · Test: model experiment → oddělené indexy / zakázáno.
