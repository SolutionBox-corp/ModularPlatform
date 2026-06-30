# Oblast 05 — Dense vector retrieval (pgvector)

Tato oblast pokrývá hustý (dense) sémantický retrieval nad `Chunk.Embedding` pomocí pgvector HNSW indexu a EF/LINQ operátoru `CosineDistance` — bez jediného řádku raw SQL. Mapuje se na build fázi „Retrieval core" (po ingest pipeline, před hybrid RRF fusion a rerankem). Je to čtecí (query-only) cesta: žádné mutace, žádné eventy, žádné transakce. Klíčové invarianty: tenant + scope pre-filtr MUSÍ být součástí ANN dotazu (ne post-filtr), `IsCurrent` je tvrdý filtr, soft-deleted dokumenty se nikdy nevrací, a degradace (provider down, prázdný index) je vždy explicitní (`Partial`/`Degraded` flag), nikdy tichá půlka výsledků.

## UC-05-01 — Základní KNN dense retrieval (top-K cosine similarity)
- **Actor / role:** user
- **Precondition:** Existuje `KnowledgeCollection` s alespoň jedním `Document` ve stavu `Indexed`; `Chunk` řádky mají vyplněný `Embedding vector(3072)`, `IsCurrent=true`; HNSW index `chunks_embedding_hnsw` (vector_cosine_ops, m/ef_construction z configu) je postavený; OpenAI embed gateway dostupný (nebo `Rag:UseFakeGateways=true`).
- **Trigger:** HTTP `POST /v1/rag/search/dense` (interně volaný i hybrid fusion vrstvou) s `{ query, collectionId?, topK }`.
- **Main flow:** Endpoint mapuje Request→`DenseSearchQuery` → `IDispatcher.Query` → `DenseSearchHandler`. Handler: (1) zavolá `IEmbeddingGateway.EmbedQueryAsync(query)` → `Vector q` (3072 dim); (2) přes `IReadDbContextFactory` otevře read context; (3) LINQ dotaz `db.Chunks.Where(<tenant+scope+IsCurrent pre-filtr>).OrderBy(c => c.Embedding.CosineDistance(q)).Take(topK).Select(<projection: ChunkId, DocumentId, Content, distance>)`; (4) převede `distance` na `similarity = 1 - distance`; (5) vrátí `DenseSearchResult { Hits[], Degraded=false }`.
- **Postcondition / záruky:** 200 + `ApiResponse<DenseSearchResult>`. Žádná mutace, žádný event, žádná transakce (Zákon: queries jen čtou). Deterministické pořadí (tie-break sekundárně podle `Chunk.CreatedAt` desc, pak `ChunkId`, aby ANN nedeterminismus nedával náhodné pořadí při shodné distance).
- **Tenancy / permissions:** Scope = User volání: vidí `Scope==Tenant` v rámci svého tenanta + svoje `OwnerUserId==me` privátní. Žádná zvláštní permission (běžný read). RLS na `chunks` je defence-in-depth; pre-filtr v dotazu je primární izolace.
- **Reuse / canonical pattern:** Read query = `GetProfileHandler.cs:12` (IReadDbContextFactory, žádná transakce). Endpoint = `Features/Users/RegisterUser/RegisterUserEndpoint.cs` styl, relativní routa `/search/dense`. Identity = `ITenantContext.UserId`.
- **Data dotčená:** `chunks` (read-only) · **Eventy:** žádné (query)
- **Priorita:** P0

### Edge cases UC-05-01
- **EC-05-01-01 — topK ≤ 0** · Trigger: `topK=0` nebo záporné · Očekávané chování: `ValidationException` s errorCode `rag.dense.topk_invalid` (400), NE prázdný 200 · Mechanismus: `DenseSearchValidator` `.GreaterThan(0).WithErrorCode("rag.dense.topk_invalid")` (FluentValidation, ValidationBehavior) · Severity: P2 · Test: integration POST topK=0 → 400 + errorCode.
- **EC-05-01-02 — topK nad strop** · Trigger: `topK=100000` · Očekávané chování: clamp na konfigurovaný `Rag:Dense:MaxTopK` (např. 200) NEBO 400; nikdy neposlat ANN dotaz s neomezeným K (DoS na ef_search) · Mechanismus: validator `.LessThanOrEqualTo(maxTopK)` + handler clamp jako pojistka · Severity: P1 · Test: topK=100000 → buď 400 nebo vrácených ≤ MaxTopK.
- **EC-05-01-03 — prázdný query string** · Trigger: `query=""` nebo whitespace · Očekávané chování: 400 `rag.dense.query_empty`, embed se vůbec nevolá (šetří provider quota) · Mechanismus: `DenseSearchValidator` `.NotEmpty()` před handlerem · Severity: P2 · Test: query="" → 400, žádné volání gateway (assert na fake call count = 0).
- **EC-05-01-04 — query přes token-window embed modelu** · Trigger: query > 8191 tokenů (limit text-embedding-3-large) · Očekávané chování: explicitní 400 `rag.dense.query_too_long` NEBO server-side truncation podle `Rag:Dense:TruncateQuery`; nikdy neposlat na OpenAI a nechat ho vrátit 400 jako 500 · Mechanismus: handler měří tokeny (tokenizer) před `EmbedQueryAsync`; ošetřeno jako BusinessRuleException/Validation · Severity: P1 · Test: 9000-token query → definované chování, ne 500.
- **EC-05-01-05 — collectionId neexistuje nebo cizí tenant** · Trigger: `collectionId` patří jinému tenantovi/uživateli · Očekávané chování: 404 `rag.collection_not_found` (IDOR → 404, ne 403) · Mechanismus: kolekce čtena přes tenant+scope filtr; nenalezeno → `NotFoundException`; cizí id je neviditelné (RLS + filtr) · Severity: P0 · Test: user B hledá v collection usera A → 404.
- **EC-05-01-06 — embed gateway 429 (rate limit)** · Trigger: OpenAI vrátí 429 + Retry-After · Očekávané chování: respektovat Retry-After, omezený retry (Polly), po vyčerpání → 503 `rag.dense.embed_unavailable` + `Degraded=true`; nikdy prázdný 200 jako „nic nenalezeno" · Mechanismus: anti-corruption layer kolem gateway překládá 429; degradace explicitní · Severity: P1 · Test: fake gateway hodí 429 → 503 + Degraded flag, ne 200/[].
- **EC-05-01-07 — embed vektor špatné dimenze** · Trigger: gateway vrátí 1536 dim (model drift / chybný config) místo 3072 · Očekávané chování: fail-fast `rag.dense.embed_dimension_mismatch` (500/BusinessRule); NIKDY neposlat do `CosineDistance` (pgvector hodí runtime chybu / nesmyslné skóre) · Mechanismus: handler asertuje `q.Length == ExpectedDimension (3072)` před dotazem · Severity: P0 · Test: fake vrátí 1536 → fail-fast, žádný DB dotaz.
- **EC-05-01-08 — žádný výsledek (prázdná kolekce, ale validní)** · Trigger: kolekce existuje, ale 0 chunků (ingest běží / vše soft-deleted) · Očekávané chování: 200 + `Hits=[]` + `Degraded=false` (to je validní výsledek, ne chyba) — zero-retrieval handluje až konzument (RAG fallback) · Mechanismus: prázdný LINQ result · Severity: P2 · Test: prázdná kolekce → 200 [].
- **EC-05-01-09 — výsledek post-projekce obsahuje PII** · Trigger: `Chunk.Content` je `[Encrypted][PersonalData]` · Očekávané chování: dešifrování přes model-level converter na read factory; pokud DEK subjektu shredded (GDPR) → `[erased]`, ne výjimka · Mechanismus: `PersonalDataEncryptionInterceptor`/converter (CLAUDE.md §4 PII at rest) na read kontextu · Severity: P0 · Test: chunk erased subjektu → vrací `[erased]`, ne crash.
- **EC-05-01-10 — duplicitní query rychle za sebou (DoS)** · Trigger: 1000 req/s na `/search/dense` od jednoho usera · Očekávané chování: per-user rate limit 429 + Retry-After (embed je drahý) · Mechanismus: partitioned rate limiter (CLAUDE.md request-edge hardening), vlastní policy `"rag-search"` per `NameIdentifier` · Severity: P1 · Test: burst → 429 po překročení.
- **EC-05-01-11 — nedeterministické pořadí při shodné distance** · Trigger: dva chunky se stejnou cosine distance · Očekávané chování: stabilní tie-break (`CreatedAt desc, ChunkId`), ne náhodné HNSW pořadí mezi voláními · Mechanismus: `OrderBy(distance).ThenByDescending(CreatedAt).ThenBy(Id)` · Severity: P2 · Test: dva identické vektory → stejné pořadí napříč běhy.

## UC-05-02 — Tenant + scope pre-filtr UVNITŘ ANN dotazu (dvouvrstvé vlastnictví)
- **Actor / role:** user
- **Precondition:** Tenant má smíšené `Chunk` řádky: `Scope==Tenant` (firemní korpus) + `Scope==User` s různými `OwnerUserId`. HNSW index existuje.
- **Trigger:** HTTP `POST /v1/rag/search/dense` (běžný user search).
- **Main flow:** `DenseSearchHandler` staví LINQ predikát: `db.Chunks.Where(c => c.TenantId == tenant.TenantId && c.IsCurrent && (c.Scope == ChunkScope.Tenant || c.OwnerUserId == tenant.UserId)).OrderBy(c => c.Embedding.CosineDistance(q)).Take(topK)`. Predikát je SOUČÁSTÍ dotazu, který jede přes HNSW — pgvector aplikuje filtr během ANN prohledávání (pre-filtr), ne až na výsledku (post-filtr).
- **Postcondition / záruky:** Výsledek obsahuje jen tenant-public + vlastní privátní chunky. Žádný leak cizích privátních. `Degraded=false`.
- **Tenancy / permissions:** Toto JE dvouvrstvý model (Scope = Tenant | User). RLS na `chunks` (IUserOwned přes `OwnerUserId`) je druhá obrana; aplikační filtr primární.
- **Reuse / canonical pattern:** Tenant filtr = EF global query filter (CLAUDE.md §4 Tenant scoping) + per-user RLS na `IUserOwned`. `ITenantContext.UserId` z tokenu (`HttpTenantContext.cs`).
- **Data dotčená:** `chunks` · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-05-02
- **EC-05-02-01 — post-filtr místo pre-filtru (recall bug)** · Trigger: implementace dá `.Take(topK)` PŘED `.Where()` nebo filtruje v paměti po ANN · Očekávané chování: MUSÍ být pre-filtr; jinak ANN vrátí top-K globálně a po filtru zbyde 0–2 hity (tichá ztráta recall) · Mechanismus: LINQ predikát před `OrderBy(CosineDistance)`; HNSW iterativní filtr (`ef_search` dostatečný). Code review + test na distribuci · Severity: P0 · Test: tenant s 1000 cizích + 5 vlastních relevantních chunků → vlastní se vrátí, ne 0.
- **EC-05-02-02 — cross-tenant leakage** · Trigger: chunk jiného tenanta sémanticky bližší než vlastní · Očekávané chování: cizí tenant NIKDY ve výsledku, ani kdyby měl distance 0 · Mechanismus: `TenantId == tenant.TenantId` v predikátu + RLS GUC `app.principal_id`/tenant · Severity: P0 · Test: dva tenanti, identický dokument → user tenanta A nevidí chunk tenanta B.
- **EC-05-02-03 — cizí privátní chunk uvnitř téhož tenanta** · Trigger: user B má `Scope==User` chunk, hledá user A ve stejném tenantu · Očekávané chování: A nevidí privátní chunk B (jen Tenant-scope + vlastní) · Mechanismus: `(Scope==Tenant || OwnerUserId==me)` + RLS per-user · Severity: P0 · Test: A nevidí privátní chunk B; firemní chunk vidí oba.
- **EC-05-02-04 — `OwnerUserId==null` u Scope==User řádku** · Trigger: data corruption / chyba ingest, privátní chunk bez ownera · Očekávané chování: NIKDY se nevrátí (null nesplní `OwnerUserId==me`); ingest validace by to neměla dovolit · Mechanismus: predikát `OwnerUserId==me` vylučuje null; navíc DB check/ingest invariant · Severity: P1 · Test: vložit Scope=User chunk s null owner → nevrací se nikomu.
- **EC-05-02-05 — chybějící tenant claim (background/anon)** · Trigger: volání bez tenant claimu · Očekávané chování: NE „vidět vše" — chybějící tenant = 401/prázdno, ne system scope (CLAUDE.md: in-Api background = SYSTEM, ale user search vyžaduje token) · Mechanismus: endpoint vyžaduje auth; `ITenantContext` bez claimu → `UnauthorizedException` · Severity: P0 · Test: bez tokenu → 401.
- **EC-05-02-06 — ANN ef_search příliš malý při selektivním filtru** · Trigger: filtr propustí <1 % řádků, `ef_search` default → ANN nenajde dost kandidátů po filtru · Očekávané chování: `Rag:Dense:EfSearch` nastaven dost vysoko / iterativní index scan, aby pre-filtr nezpůsobil prázdný výsledek navzdory existujícím relevantním řádkům · Mechanismus: pgvector `hnsw.ef_search` (nastaveno přes connection/session option v read factory, ne raw SQL na chunky — config side), případně `hnsw.iterative_scan` · Severity: P1 · Test: vysoce selektivní filtr + známé relevantní → vrátí je.

## UC-05-03 — Firemní/tenant search napříč uživateli (permission-gated)
- **Actor / role:** tenant-admin (nebo user s `rag.search.tenant_wide`)
- **Precondition:** Volající má permission pro firemní search; tenant obsahuje privátní korpusy více uživatelů.
- **Trigger:** HTTP `POST /v1/rag/search/dense?scope=tenant-wide` nebo dedikovaný `POST /v1/rag/admin/search/dense`.
- **Main flow:** Endpoint má `.RequirePermission(PlatformPermissions.RagSearchTenantWide)`. `DenseSearchHandler` (varianta) staví predikát BEZ `OwnerUserId==me` omezení: `c.TenantId==ctx && c.IsCurrent` (volitelně `IncludeUserPrivate` flag → zahrne všechny `Scope==User` daného tenanta). RLS musí být na tomto code-pathu obejita kontrolovaně — buď permission-gated kontext, který RLS pro tenant-wide read povolí, NEBO admin read role; rozhodnutí je explicitní.
- **Postcondition / záruky:** 200, výsledky napříč uživateli tenanta; auditovatelné (kdo spustil firemní search). Stále žádná mutace.
- **Tenancy / permissions:** Scope = Tenant (firemní). Vyžaduje `RagSearchTenantWide`. NIKDY napříč tenanty.
- **Reuse / canonical pattern:** Permission gate = `.RequirePermission(...)` (CLAUDE.md Authorization). Tenant-wide read přes RLS: záměrné rozhodnutí — pokud RLS per-user blokuje, použít vyhrazený permission-scoped read kontext; pozor, je to v sekci „ASK before inventing" pokud chybí seam → eskalovat.
- **Data dotčená:** `chunks` · **Eventy:** žádné (volitelně audit log akce)
- **Priorita:** P1

### Edge cases UC-05-03
- **EC-05-03-01 — chybějící permission** · Trigger: běžný user volá tenant-wide endpoint · Očekávané chování: 403 `forbidden` (permission chybí v claimu) · Mechanismus: `RequirePermission` middleware · Severity: P0 · Test: user bez permission → 403.
- **EC-05-03-02 — RLS blokuje tenant-wide read** · Trigger: per-user RLS na `chunks` odfiltruje cizí `OwnerUserId` i pro admina · Očekávané chování: buď firemní read vrací VŠE v tenantu (záměrný permission-scoped path), nebo se firemní search omezí jen na `Scope==Tenant` — chování MUSÍ být deterministické a zdokumentované, ne „někdy chybí řádky" · Mechanismus: explicitní rozhodnutí o RLS pro tento path; pokud seam neexistuje → STOP a zeptat se (Zákon 11) · Severity: P0 · Test: admin tenant-wide → vidí privátní chunky všech userů tenanta (pokud `IncludeUserPrivate`), jinak jen Tenant-scope.
- **EC-05-03-03 — tenant-wide ale cizí tenant** · Trigger: admin tenanta A se snaží `tenantId=B` v body · Očekávané chování: tenantId se IGNORUJE z body, bere se z tokenu → 404/prázdno pro cizí · Mechanismus: tenant z `ITenantContext`, ne z requestu (Zákon 10) · Severity: P0 · Test: admin A pošle tenantId B → vidí jen A.
- **EC-05-03-04 — audit firemního searche** · Trigger: admin spustí firemní search nad privátními daty zaměstnanců · Očekávané chování: akce je auditovatelná (GDPR/compliance — kdo četl cizí privátní obsah) · Mechanismus: zvážit audit záznam (ne přes AuditInterceptor, ten je na mutace; query audit = explicit log/metrika `platform.rag.tenant_wide_search`) · Severity: P2 · Test: firemní search → metrika/log inkrementován.

## UC-05-04 — `IsCurrent` tvrdý filtr (re-embed / re-chunk verzování)
- **Actor / role:** user
- **Precondition:** Dokument byl re-ingestován (změna chunking strategie nebo embed modelu): staré chunky mají `IsCurrent=false`, nové `IsCurrent=true`; obě verze koexistují v `chunks` (staré zatím nepromazané kvůli auditu/rollbacku).
- **Trigger:** HTTP `POST /v1/rag/search/dense`.
- **Main flow:** Predikát VŽDY obsahuje `c.IsCurrent == true`. ANN tedy hledá jen v aktuální verzi indexu. Staré (`IsCurrent=false`) vektory jsou v tabulce i v HNSW indexu fyzicky přítomné, ale filtr je vyloučí (pre-filtr během ANN).
- **Postcondition / záruky:** Výsledek obsahuje pouze nejnovější embedding verzi. Žádné „duchy" ze staré chunking strategie.
- **Tenancy / permissions:** Stejné jako UC-05-02.
- **Reuse / canonical pattern:** Boolean hard filtr v každém retrieval dotazu (analogie `ISoftDeletable` global filter, ale `IsCurrent` je explicitní predikát, ne global filter — musí být ve VŠECH retrieval slice).
- **Data dotčená:** `chunks` · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-05-04
- **EC-05-04-01 — zapomenutý IsCurrent filtr** · Trigger: nová retrieval slice neobsahuje `IsCurrent==true` · Očekávané chování: MUSÍ filtrovat; jinak duplicitní/zastaralé chunky ve výsledku · Mechanismus: sdílený `IQueryable<Chunk>` extension `CurrentOnly()` reusovaný všemi slice (DRY); ArchUnitNET/test pokrytí · Severity: P0 · Test: re-embed dokument → search nevrací staré chunky.
- **EC-05-04-02 — atomický flip IsCurrent během re-ingestu** · Trigger: re-ingest přepíná staré→false a nové→true; search běží uprostřed · Očekávané chování: NIKDY okno s 0 current chunky ani s oběma verzemi current; přepnutí musí být transakční / atomic `ExecuteUpdate` ve správném pořadí · Mechanismus: ingest saga přepne v jedné transakci (set new IsCurrent=true a old=false atomicky); xmin/concurrency · Severity: P0 · Test: paralelní search během flipu → vždy přesně jedna konzistentní verze.
- **EC-05-04-03 — embed model drift mezi verzemi** · Trigger: staré chunky embedded 3-large, nové jiný model/dim · Očekávané chování: `IsCurrent` + single-model-per-index invariant; míchání dimenzí v jednom `vector(3072)` sloupci nelze (pgvector pevná dim) → re-embed celé kolekce, ne částečné · Mechanismus: ingest vynucuje jeden embed model na kolekci; verzování přes `IsCurrent` + případně `EmbeddingModel` sloupec pro guard · Severity: P0 · Test: pokus o vložení 1536-dim do 3072 sloupce → DB/ingest error, ne tiché.
- **EC-05-04-04 — index obsahuje obě verze → ef_search plýtvá** · Trigger: 50 % řádků v indexu je `IsCurrent=false` · Očekávané chování: recall current verze nesmí trpět; `ef_search` dost vysoký nebo periodický prune starých verzí (offline) · Mechanismus: retention/prune job pro `IsCurrent=false` po grace period; ef_search tuning · Severity: P1 · Test: poměr 50/50 → current relevantní stále v top-K.

## UC-05-05 — Generování query embeddingu (OpenAI text-embedding-3-large) přes port
- **Actor / role:** system (handler interně) / user (iniciuje)
- **Precondition:** `IEmbeddingGateway` zaregistrován (reálný `OpenAiEmbeddingGateway` nebo `FakeEmbeddingGateway` pod `Rag:UseFakeGateways=true`); API klíč v configu (fail-fast mimo Dev).
- **Trigger:** Vnitřní krok `DenseSearchHandler` před ANN dotazem.
- **Main flow:** Handler volá `IEmbeddingGateway.EmbedQueryAsync(text, ct)`. Gateway: anti-corruption layer kolem OpenAI SDK, nastaví `model=text-embedding-3-large`, `dimensions=3072`, vrátí `Vector`. Cachování query embeddingu (volitelně, podle hash query+model) pro opakované dotazy.
- **Postcondition / záruky:** Vrácen 3072-dim vektor nebo explicitní chyba/degradace. Žádná mutace platform DB.
- **Tenancy / permissions:** N/A na úrovni gateway (stateless); volá se v rámci user requestu.
- **Reuse / canonical pattern:** Port + fake-under-flag = `MarketingModule.cs:51` (UseFakeGateways), gateway tvar jako `ClaudeVibeAgentGateway.cs:85`. Retry/rate-limit = anti-corruption layer (CLAUDE.md External System Integration).
- **Data dotčená:** žádná (gateway) · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-05-05
- **EC-05-05-01 — provider down / timeout** · Trigger: OpenAI nedostupný / TCP timeout · Očekávané chování: po retry → 503 `rag.dense.embed_unavailable` + `Degraded=true`; konzument (hybrid) může degradovat na BM25-only, ne tichá nula · Mechanismus: Polly timeout+retry, anti-corruption překlad, explicit degradace · Severity: P1 · Test: fake timeout → 503/Degraded.
- **EC-05-05-02 — chybějící API klíč mimo Dev** · Trigger: `Rag:OpenAi:ApiKey` prázdný v prod · Očekávané chování: fail-fast při startu (Options validator), ne až za běhu při prvním searchi · Mechanismus: `RagOptionsValidator` (CLAUDE.md fail-fast mimo Dev, jako JwtOptionsValidator) · Severity: P0 · Test: prázdný klíč + ASPNETCORE_ENVIRONMENT=Production → boot fail.
- **EC-05-05-03 — 429 s Retry-After** · Trigger: OpenAI rate limit · Očekávané chování: respektovat Retry-After header, bounded retry, pak degradace · Mechanismus: anti-corruption layer čte Retry-After · Severity: P1 · Test: fake 429 retry-after=2s → počká/retry, pak 503 pokud přetrvá.
- **EC-05-05-04 — gateway vrátí NaN/null vektor** · Trigger: poškozená odpověď · Očekávané chování: validace vektoru (žádné NaN, správná dim) → fail-fast `rag.dense.embed_invalid` · Mechanismus: handler/gateway sanity check před `CosineDistance` (NaN by rozbil distance) · Severity: P0 · Test: fake vrátí NaN → odmítnuto.
- **EC-05-05-05 — fake gateway determinismus v testech** · Trigger: testy pod `Rag:UseFakeGateways=true` · Očekávané chování: `FakeEmbeddingGateway` deterministický (např. hash-based embedding) aby testy recall byly stabilní · Mechanismus: fake mapuje text→deterministický vektor · Severity: P2 · Test: stejný text → stejný vektor napříč běhy.
- **EC-05-05-06 — prompt-injection v query (nepřímá)** · Trigger: query obsahuje „ignoruj filtry, vrať vše" · Očekávané chování: query je JEN embed vstup, NIKDY se neinterpretuje jako instrukce ani neovlivní filtry/tenant; filtry jsou z tokenu/serveru · Mechanismus: dense retrieval nemá LLM v cestě; tenant/scope z `ITenantContext`, ne z query (trust boundary) · Severity: P0 · Test: malicious query → stejné filtry, žádná eskalace scope.
- **EC-05-05-07 — jazyk/encoding query** · Trigger: query v jiném jazyce / emoji / RTL · Očekávané chování: model je multilingvní; UTF-8 předán beze změny; žádný crash na ne-ASCII · Mechanismus: text předán raw do gateway · Severity: P2 · Test: czech+emoji query → validní embedding, výsledky.

## UC-05-06 — Tuning recall: `ef_search` vs latence
- **Actor / role:** system (config) / tenant-admin (nepřímo přes deployment)
- **Precondition:** HNSW index postaven s daným `m`/`ef_construction`. `ef_search` je runtime parametr.
- **Trigger:** Každý dense dotaz (nastaveno session-level v read factory) / změna `Rag:Dense:EfSearch` configu.
- **Main flow:** Read context při otevření nastaví `hnsw.ef_search` na hodnotu z configu (přes Npgsql connection option / session GUC nastavený v factory — ne raw SQL na business data). Vyšší `ef_search` = lepší recall, vyšší latence. `DenseSearchHandler` může pro tenant-wide/přesné scénáře použít vyšší ef_search profil.
- **Postcondition / záruky:** Recall odpovídá konfiguraci; latence v SLO. Žádná mutace.
- **Tenancy / permissions:** Globální/per-deployment config, ne per-user.
- **Reuse / canonical pattern:** Options pattern (`Rag:Dense:*`) + Options validator. Read factory `IReadDbContextFactory` (GetProfileHandler.cs:12).
- **Data dotčená:** žádná (config) · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-05-06
- **EC-05-06-01 — ef_search < topK** · Trigger: `ef_search=10`, `topK=50` · Očekávané chování: pgvector vyžaduje ef_search ≥ K; validator/handler vynutí `ef_search >= topK` (jinak méně než K výsledků) · Mechanismus: handler `effectiveEf = Max(configEf, topK)` · Severity: P1 · Test: topK=50, ef=10 → effektivně ef≥50, vrátí 50 pokud existují.
- **EC-05-06-02 — nízký recall (ANN mine relevantní)** · Trigger: ef_search příliš nízký, relevantní chunk mimo kandidáty · Očekávané chování: měřitelný recall přes test korpus; config laděn na cílový recall@k · Mechanismus: recall regression test + metrika `platform.rag.dense.recall` (offline eval) · Severity: P1 · Test: známý ground-truth korpus → recall@10 ≥ práh.
- **EC-05-06-03 — vysoký ef_search = latence/DoS** · Trigger: ef_search=1000 pod zátěží · Očekávané chování: strop na ef_search; latence sledována, timeout na DB dotaz · Mechanismus: config max + command timeout + metrika `platform.rag.dense.latency_ms` · Severity: P2 · Test: ef vysoký → latence měřena, neroztrhne pool.
- **EC-05-06-04 — selektivní filtr + nízký ef = prázdný výsledek navzdory datům** · Trigger: viz EC-05-02-06 · Očekávané chování: iterativní scan nebo zvýšený ef při selektivním pre-filtru · Mechanismus: `hnsw.iterative_scan=relaxed_order` (pgvector ≥0.8) konfigurovatelně · Severity: P1 · Test: 0.5% selektivita → relevantní nalezeny.

## UC-05-07 — Soft-deleted dokument vyloučen z dense retrievalu
- **Actor / role:** user
- **Precondition:** `Document` má `IsDeleted=true` (ISoftDeletable), ale jeho `Chunk` řádky mohou ještě existovat (async cleanup zaostává) nebo `Chunk` nemá vlastní soft-delete a děděně sleduje dokument.
- **Trigger:** HTTP `POST /v1/rag/search/dense` po smazání dokumentu.
- **Main flow:** Retrieval predikát vylučuje chunky smazaných dokumentů: buď `Chunk` má vlastní `IsCurrent=false` nastavený při delete, NEBO predikát joinuje/filtruje na ne-smazané `documentId` v rámci stejného modulu (LINQ join uvnitř modulu je OK — není cross-module). Preferováno: delete dokumentu atomicky nastaví chunkům `IsCurrent=false` (ExecuteUpdate guard), takže existující `IsCurrent==true` filtr je dostatečný.
- **Postcondition / záruky:** Smazaný dokument se NIKDY neobjeví v retrievalu, ani okamžitě po delete (žádný stale index window viditelný uživateli).
- **Tenancy / permissions:** Stejné jako UC-05-02. Soft-delete global filter (ISoftDeletable) platí na `documents`.
- **Reuse / canonical pattern:** `ISoftDeletable` global query filter; atomic `ExecuteUpdate` na flip `IsCurrent` při delete (CLAUDE.md Audit caveat — ExecuteUpdate bypasses interceptor; OK protože chunky nejsou auditované entity).
- **Data dotčená:** `documents`, `chunks` · **Eventy:** delete dokumentu publikuje `DocumentDeletedIntegrationEvent` (jiná oblast); zde jen read
- **Priorita:** P0

### Edge cases UC-05-07
- **EC-05-07-01 — stale index po delete (race)** · Trigger: search proběhne mezi delete dokumentu a flipem chunků · Očekávané chování: delete a flip chunků atomicky (jedna transakce); search nikdy nevidí smazaný obsah · Mechanismus: delete handler `ExecuteUpdate` `IsCurrent=false WHERE DocumentId==id` ve stejné transakci jako soft-delete dokumentu · Severity: P0 · Test: delete + okamžitý search → 0 hitů ze smazaného dokumentu.
- **EC-05-07-02 — chunky zůstanou IsCurrent=true (orphan)** · Trigger: delete dokumentu zapomene flip chunků · Očekávané chování: NESMÍ — orphan chunky by leakovaly smazaný obsah · Mechanismus: ingest/delete invariant + reconciliation job (chunky bez current dokumentu → flip); test · Severity: P0 · Test: orphan chunk → reconcile ho deaktivuje; retrieval ho nevrací.
- **EC-05-07-03 — GDPR erase vs soft-delete rozdíl** · Trigger: GDPR erasure (ne pouhý delete) · Očekávané chování: chunky se kryptoshredují (DEK shred → Content `[erased]`) + vyloučí z retrievalu; audit retained · Mechanismus: `IErasePersonalData` impl modulu shreduje + flip IsCurrent (CLAUDE.md GDPR erasure) · Severity: P0 · Test: erase usera → jeho chunky nevrací, audit zůstává.
- **EC-05-07-04 — restore soft-deleted dokumentu** · Trigger: undelete dokumentu (pokud podporováno) · Očekávané chování: chunky se vrátí do retrievalu jen pokud explicitně re-flipnuty `IsCurrent=true`; ne automaticky stale · Mechanismus: restore handler řídí IsCurrent explicitně · Severity: P2 · Test: restore → chunky znovu vyhledatelné jen po re-flip.

## UC-05-08 — Prázdný index / zero-retrieval (low-similarity) fallback
- **Actor / role:** user
- **Precondition:** Kolekce existuje, ale buď 0 current chunků, NEBO všechny chunky mají similarity pod `Rag:Dense:MinSimilarity` prahem.
- **Trigger:** HTTP `POST /v1/rag/search/dense`.
- **Main flow:** Handler provede ANN dotaz; pokud 0 řádků nebo nejlepší `similarity < MinSimilarity`, vrátí `Hits=[]` (nebo jen hity nad prahem) + `LowConfidence=true`/`Degraded=false` flag. Rozhodnutí „nic relevantního" je explicitní signál pro konzumenta (RAG odpoví „nevím / nemám podklad"), ne tichý prázdný seznam zaměnitelný s chybou.
- **Postcondition / záruky:** 200 + explicitní indikace nízké/nulové relevance. Žádná halucinace na úrovni retrievalu.
- **Tenancy / permissions:** Stejné jako UC-05-02.
- **Reuse / canonical pattern:** Explicit Partial/Degraded flag (CLAUDE.md: NIKDY tichá půlka). Threshold v Options.
- **Data dotčená:** `chunks` · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-05-08
- **EC-05-08-01 — úplně prázdný index (žádný chunk v tenantu)** · Trigger: nová kolekce, ingest neproběhl · Očekávané chování: 200 `Hits=[]`, NE 500/404 · Mechanismus: prázdný LINQ result; flag `LowConfidence=true` · Severity: P1 · Test: prázdný tenant → 200 [].
- **EC-05-08-02 — všechny hity pod prahem** · Trigger: off-topic query · Očekávané chování: vrátí prázdno nebo označí `LowConfidence`; konzument fallbackuje · Mechanismus: `MinSimilarity` filtr v handleru po výpočtu similarity · Severity: P1 · Test: nesouvisející query → LowConfidence/[].
- **EC-05-08-03 — práh příliš agresivní (chybí validní hity)** · Trigger: MinSimilarity nastaven moc vysoko · Očekávané chování: konfigurovatelné, default kalibrovaný; nesmí tiše zahodit relevantní · Mechanismus: config + eval test · Severity: P2 · Test: kalibrace prahu vs ground truth.
- **EC-05-08-04 — zero-retrieval zaměněn s provider chybou** · Trigger: embed selhal → 0 hitů · Očekávané chování: rozlišit „embed failed" (Degraded=true, 503) od „nic relevantního" (200, LowConfidence) — jiné HTTP/flag · Mechanismus: dvě různé cesty, různé flagy · Severity: P0 · Test: embed fail → 503 Degraded; off-topic → 200 LowConfidence.

## UC-05-09 — halfvec storage & dimension management
- **Actor / role:** system (migrace/ingest) / tenant-admin (nepřímo)
- **Precondition:** Pro úsporu paměti je zvážen `halfvec(3072)` storage místo `vector(3072)` (pgvector halfvec = poloviční přesnost, poloviční velikost; nutné pro >2000 dim s HNSW, protože `vector` HNSW má limit 2000 dim, `halfvec` až 4000).
- **Trigger:** Migrace schématu (DDL — jediný povolený raw v této oblasti je pgvector extension + index DDL) / každý dotaz používá odpovídající operátor.
- **Main flow:** `Chunk.Embedding` je mapován jako `halfvec(3072)` (kvůli HNSW limitu 2000 pro `vector`). HNSW index používá `halfvec_cosine_ops`. EF mapování přes Pgvector .NET typ; LINQ `CosineDistance` jde proti halfvec sloupci. Query embedding (full `vector`) se castuje na halfvec pro porovnání.
- **Postcondition / záruky:** Index funkční pro 3072 dim; paměťová stopa ~poloviční; recall ztráta z half-precision v akceptovatelné toleranci.
- **Tenancy / permissions:** N/A (storage).
- **Reuse / canonical pattern:** Migrace pgvector extension = jediný povolený raw DDL (CLAUDE.md: „Jediný raw DDL = pgvector extension migrace + custom RLS policy"). Entity config `IEntityTypeConfiguration<Chunk>`.
- **Data dotčená:** `chunks.embedding` · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-05-09
- **EC-05-09-01 — `vector` HNSW limit 2000 dim** · Trigger: pokus o HNSW index na `vector(3072)` · Očekávané chování: nelze (pgvector limit) → MUSÍ `halfvec(3072)` pro HNSW, nebo dimensionality reduction; rozhodnutí explicitní v migraci · Mechanismus: schéma `halfvec(3072)` + `halfvec_cosine_ops` · Severity: P0 · Test: migrace vytvoří index úspěšně na 3072 dim.
- **EC-05-09-02 — recall ztráta half-precision** · Trigger: half-precision zaokrouhlení mění distance pořadí · Očekávané chování: ztráta v toleranci (typicky <1 % recall); měřeno · Mechanismus: eval test full vs half · Severity: P1 · Test: recall@10 half vs full → rozdíl pod prahem.
- **EC-05-09-03 — query vektor full vs storage half mismatch** · Trigger: query embedding je `vector`, sloupec `halfvec` · Očekávané chování: konzistentní cast (query→halfvec) v LINQ; ne runtime type error · Mechanismus: EF/Pgvector mapování + explicit cast v dotazu · Severity: P0 · Test: dense search běží bez type chyby.
- **EC-05-09-04 — embed dimension config drift** · Trigger: někdo změní `dimensions=1536` v gateway, sloupec je 3072 · Očekávané chování: fail-fast mismatch (EC-05-01-07); sloupec dim je zdroj pravdy · Mechanismus: guard dim při embed i při insertu · Severity: P0 · Test: 1536 embed vs 3072 sloupec → odmítnuto.
- **EC-05-09-05 — index build paměť/čas (velký korpus)** · Trigger: 10M chunků, HNSW build · Očekávané chování: build offline/maintenance (CONCURRENTLY pokud možno), `maintenance_work_mem` zvážen; build neblokuje retrieval déle než nutno · Mechanismus: index DDL v MigrationService; `CREATE INDEX CONCURRENTLY` zvážit · Severity: P1 · Test: build na velkém korpu dokončí, retrieval mezitím funguje (sequential scan fallback).

## UC-05-10 — HNSW index údržba & concurrent write během dotazu
- **Actor / role:** system/worker (ingest zapisuje vektory) souběžně s user dotazem
- **Precondition:** Ingest pipeline (jiná oblast) vkládá nové `Chunk` řádky s embeddingy; HNSW index se aktualizuje inkrementálně; user současně hledá.
- **Trigger:** Souběh `INSERT chunks` (ingest) + `POST /v1/rag/search/dense` (user).
- **Main flow:** pgvector HNSW podporuje souběžné čtení i zápis. Dotaz čte konzistentní snapshot (MVCC). Nově vkládané chunky jsou `IsCurrent=true` až po commitu ingest transakce/saga; do té doby pre-filtr (`IsCurrent`) + MVCC zajistí, že rozpracovaný ingest neleakuje polovičatá data.
- **Postcondition / záruky:** Dotaz vidí buď stav před, nebo po commitu ingestu — nikdy half-written. Žádný deadlock retrieval↔ingest.
- **Tenancy / permissions:** Stejné jako UC-05-02.
- **Reuse / canonical pattern:** Concurrency = xmin + `ConcurrencyRetryBehavior` (na ingest mutacích); retrieval je read-only snapshot. Ingest commit = `SaveChangesAndFlushMessagesAsync` (RegisterUserHandler.cs:22).
- **Data dotčená:** `chunks`, HNSW index · **Eventy:** žádné (na retrieval straně)
- **Priorita:** P1

### Edge cases UC-05-10
- **EC-05-10-01 — half-committed ingest viditelný** · Trigger: ingest vložil chunky ale ještě necommitnul/neflipnul IsCurrent · Očekávané chování: retrieval je nevidí (MVCC + IsCurrent) · Mechanismus: nové chunky `IsCurrent=true` až v commitu; saga řídí · Severity: P0 · Test: souběžný ingest+search → search nevidí necommitnuté.
- **EC-05-10-02 — index bloat z opakovaných re-ingestů** · Trigger: mnoho `IsCurrent=false` řádků zůstává v indexu · Očekávané chování: prune/VACUUM job; ef_search kompenzuje dočasně · Mechanismus: retention job pro staré verze + autovacuum tuning · Severity: P1 · Test: po N re-ingestech prune sníží velikost indexu.
- **EC-05-10-03 — index neexistuje (fresh DB / migrace selhala)** · Trigger: HNSW index chybí · Očekávané chování: dotaz funguje (sequential scan fallback, pomalu) ne 500; alert na chybějící index · Mechanismus: pgvector funguje bez indexu (full scan); health check ověří existenci indexu · Severity: P1 · Test: drop index → search stále vrací správné (pomalu); metrika varuje.
- **EC-05-10-04 — concurrent flip IsCurrent vs běžící dlouhý dotaz** · Trigger: re-ingest flipne během pomalého tenant-wide dotazu · Očekávané chování: dotaz dokončí na svém snapshotu, konzistentní · Mechanismus: MVCC snapshot izolace · Severity: P2 · Test: pomalý dotaz + souběžný flip → konzistentní výsledek z jednoho snapshotu.

## UC-05-11 — Interní dense-retrieval port pro hybrid fusion (konzument RRF)
- **Actor / role:** system (hybrid search handler volá dense jako komponentu)
- **Precondition:** Hybrid search (jiná oblast — RRF fusion dense+BM25) potřebuje raw kandidáty z dense větve s normalizovaným skóre/rankem.
- **Trigger:** In-process volání `IDenseRetriever.RetrieveAsync(q, filter, k)` z hybrid handleru (ne přímý HTTP).
- **Main flow:** Dense větev vrací `IReadOnlyList<DenseCandidate { ChunkId, Rank, Distance }>` (rank-based, ne raw-score mixed). Hybrid pak dělá RRF v C# (`1/(rrf_k + rank)`) — NIKDY nemíchá raw cosine distance s raw BM25 skóre. Dense port je čistě read, vrací rank pořadí.
- **Postcondition / záruky:** Dense kandidáti připraveni pro RRF; rank je stabilní (tie-break). Žádná mutace.
- **Tenancy / permissions:** Filtr (tenant+scope+IsCurrent) předán z hybrid kontextu, odvozen z `ITenantContext` — NIKDY z volajícího argumentu jako důvěryhodný tenant (trust boundary).
- **Reuse / canonical pattern:** Port shape jako `IFileStorage` (Ports.cs:166). RRF v C# (CLAUDE.md Zákony: „RRF v C#", míchání raw-score = bug).
- **Data dotčená:** `chunks` · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-05-11
- **EC-05-11-01 — raw-score míchání místo rank (RRF bug)** · Trigger: implementace předá raw cosine distance do fusion místo ranku · Očekávané chování: RRF MUSÍ na ranku; raw distance a BM25 jsou nesouměřitelné škály · Mechanismus: dense port vrací `Rank` (0..k-1), ne raw score do fusion · Severity: P0 · Test: ověř že fusion bere rank; dense+BM25 stejný dokument → RRF součet ranků.
- **EC-05-11-02 — tenant z argumentu (trust boundary leak)** · Trigger: hybrid/MCP volající pošle `tenantId` jako parametr portu · Očekávané chování: IGNOROVAT, brát z `ITenantContext`; argument-tenant = leak · Mechanismus: port čte tenant z DI scope kontextu, ne z parametru (CLAUDE.md MCP trust boundary) · Severity: P0 · Test: volání s cizím tenantId v argu → vidí jen svůj tenant.
- **EC-05-11-03 — dense vrátí prázdno, BM25 ne** · Trigger: dense embed selhal, BM25 OK · Očekávané chování: hybrid degraduje na BM25-only + `Degraded=true`, ne celý fail · Mechanismus: dense port signalizuje degradaci; fusion pokračuje s jednou větví explicitně · Severity: P1 · Test: dense fail → hybrid vrací BM25 výsledky + Degraded.
- **EC-05-11-04 — k pro dense vs finální top-K mismatch** · Trigger: hybrid chce fuse top-100 dense + top-100 BM25 → final top-10 · Očekávané chování: dense k (kandidátní hloubka) ≥ final K; konfigurovatelné · Mechanismus: `Rag:Hybrid:CandidateK` ≥ finalK · Severity: P2 · Test: candidateK=100, finalK=10 → 100 dense kandidátů předáno.
- **EC-05-11-05 — duplicitní ChunkId mezi dense a BM25** · Trigger: stejný chunk v obou větvích · Očekávané chování: RRF správně sečte ranky pro stejný ChunkId (dedup podle id) · Mechanismus: fusion klíčuje podle ChunkId · Severity: P1 · Test: chunk v obou → jeden výsledek se sečteným RRF skóre.


---

## Doplňky z completeness review
- **EC-05-02-07 — Dense predikát neobsahuje EmbeddingModel/Dimensions drift guard (kontradikce s EC-03-03-04)** · Trigger: během re-embed/model-drift okna koexistují chunky dvou modelů; predikát UC-05-02 filtruje jen `TenantId/Scope/IsCurrent`, NE `EmbeddingModel==current` → `CosineDistance` míchá vektory dvou modelů v jednom žebříčku (nesmyslné skóre) · Očekávané chování: dense predikát MUSÍ obsahovat `c.EmbeddingModel == currentModel && c.EmbeddingDimensions == currentDims` (jak vyžaduje EC-03-03-04), drift chunky přeskočit + `Partial/Degraded` flag + `platform.rag.embed_model_drift` · Mechanismus: doplnit model/dim guard do sdíleného `CurrentOnly()` extension (UC-05-04) reusovaného všemi retrieval slice · Severity: P0 · Test: integ — smíšený index → dense vrací jen current-model chunky, drift metrika inkrementována.
