# Oblast 06 — Sparse / lexical retrieval (BM25)

> **Status:** DESIGN DOKUMENTACE (kód modulu `HybridRag` zatím neexistuje). Tento katalog je závazná specifikace chování lexikální (sparse) retrievalové nohy hybridního RAG. Slouží jako vstup pro implementaci vertikálních slices, pro test plán (`docs/test-scenarios.md`) a pro architektonický review raw-SQL výjimky.
>
> **Klíčové rozhodnutí (frozen):** lexikální leg používá **pravé BM25** přes ParadeDB `pg_search` (operátor `@@@`, `paradedb.score()`) — IDF saturace + délková normalizace. **NE** `ts_rank` / `ts_rank_cd` (ten nemá ani IDF, ani length-norm a uměle nafukuje skóre dlouhých dokumentů). `ts_rank_cd` zůstává pouze jako **degradovaný fallback** pro managed DB bez rozšíření `pg_search`, přepínaný configem `Rag:Lexical:Provider = pg_search | ts_rank`.
>
> **Důsledek pro architekturu:** pravé BM25 v Postgresu **nemá EF/LINQ surface** — `@@@` a `paradedb.score()` se z LINQ nepřeloží. Proto je nutná **úzká, dokumentovaná raw-SQL carve-out** ze zákona „EF/LINQ only, NEVER raw SQL" **jen pro lexikální leg**: parametrizovaný `FromSqlInterpolated` / `SqlQuery<T>` (injection-safe, NIKDY string konkatenace). Tato výjimka je analogická k již akceptovaným carve-outům (pgvector extension DDL, custom RLS policy DDL) a podléhá stejnému režimu code review + auditní poznámky v kódu.

---

## Obsah a konvence

- **Scope dvouvrstvého vlastnictví:** entita `Chunk` má `Scope ∈ {Tenant, User}`. Běžný uživatel hledá **současně** v tenantím korpusu (`Scope=Tenant`) a ve svých privátních chuncích (`Scope=User AND OwnerUserId = me`). Tenantí/firemní search (permission-gated) hledá v `Scope=Tenant` a **volitelně** napříč chunky všech userů tenantu.
- **Pre-filtr MUSÍ být uvnitř BM25 SQL** (`WHERE tenant_id = @tenant AND (scope = 'Tenant' OR owner_user_id = @me) AND is_current AND NOT is_deleted`). Filtrovat až po BM25 (post-hoc) je **zakázáno** — způsobilo by to (a) leak napříč tenanty/usery a (b) děravé top-K (kandidáti by se „spotřebovali" na cizí řádky a vrátil by se méně než K legitimních).
- **Zákony, které tato oblast neporušuje:** queries pouze čtou (žádná transakce / mutace / event); identita VŽDY z `ITenantContext.UserId` (nikdy z body / route / LLM argumentu); vše UTC; `IsCurrent` tvrdý filtr; soft-deleted dokument vyloučen z retrievalu.
- **Reuse seamy:** read query pattern = `GetProfileHandler.cs:12` (`IReadDbContextFactory`); lexikální leg používá tentýž `IReadDbContextFactory`, ale místo LINQ volá `readDb.Database.SqlQuery<LexicalHit>($"…")` / `readDb.Set<Chunk>().FromSqlInterpolated($"…")`. RRF fúze žije v **oblasti 07** (k=60 v C#, pracuje s **RANK**, ne s raw skóre → různá škála BM25 vs cosine není problém). Chyby = `throw ValidationException/ConflictException` + errorCode do `SharedResource.resx` (`en` + `cs`). Metriky = `PlatformMetrics.Meter` (`platform.rag.lexical_latency`).

---

## UC-06-01 — BM25 lexikální retrieval přes `pg_search` (`@@@`), top-50 kandidátů

- **Actor / role:** user | system/worker (retrieval orchestrátor oblasti 07) | MCP klient (přes nástroj `rag.search`)
- **Precondition:** Modul `HybridRag` enabled (`Modules:HybridRag:Enabled=true`); rozšíření `pg_search` nainstalováno; BM25 index nad `(content + contextual_prefix)` existuje; `Rag:Lexical:Provider = pg_search`; v korpusu existují `Chunk` řádky se `IsCurrent=true`.
- **Trigger:** interní volání `dispatcher.Query(new LexicalRetrieveQuery(QueryText, TopK=50, Scope))` z hybridního retrieval orchestrátoru; nebo přímý debug endpoint `GET /v1/hybridrag/lexical?q=…` (permission-gated, diagnostický).
- **Main flow:**
  1. Endpoint/orchestrátor zmapuje `Request → LexicalRetrieveQuery`. `QueryText` se NORMALIZUJE pouze měkce (trim, sjednocení whitespace) — žádné odstranění diakritiky na app vrstvě (to řeší tokenizer, UC-06-06).
  2. `LexicalRetrieveHandler` (query handler, jen čte) získá read context z `IReadDbContextFactory`.
  3. Handler poskládá **parametrizovaný** BM25 dotaz přes `FromSqlInterpolated` / `SqlQuery<LexicalHit>`: 
     `SELECT id, document_id, paradedb.score(id) AS score FROM hybridrag_chunks WHERE tenant_id = {tenant} AND (scope = 'Tenant' OR owner_user_id = {me}) AND is_current AND NOT is_deleted AND search_bm25 @@@ {queryText} ORDER BY score DESC LIMIT {topK}`.
     `{queryText}`, `{tenant}`, `{me}`, `{topK}` jdou jako **DB parametry**, ne konkatenace.
  4. Postgres vyhodnotí BM25 (IDF saturace + length-norm, parametry k1/b z UC-06-07), seřadí podle `score DESC`, ořízne na `LIMIT 50`.
  5. Handler materializuje `IReadOnlyList<LexicalHit>` (`ChunkId`, `DocumentId`, `Score`, `Rank` = pořadí 1..N).
  6. Změří `platform.rag.lexical_latency` (histogram, tag `provider=pg_search`) a `platform.rag.lexical_hits` (počet vrácených).
  7. Vrátí seznam orchestrátoru pro RRF (oblast 07). BM25 raw `Score` se předává jen informativně; fúze používá `Rank`.
- **Postcondition / záruky:** vrácených ≤ 50 kandidátů, výhradně z tenantu volajícího, jen `IsCurrent` a ne-soft-deleted; žádná mutace; idempotentní (stejný dotaz + stav korpusu → stejný výsledek).
- **Tenancy / permissions:** Scope dle volání (User → tenant+vlastní; Tenant-admin → viz UC-06-03). RLS na `hybridrag_chunks` je defence-in-depth; explicitní `WHERE` v SQL je primární. Identita z tokenu.
- **Reuse / canonical pattern:** read přes `IReadDbContextFactory` analogicky `GetProfileHandler.cs:12`; raw-SQL leg dle carve-out UC-06-04; metrika přes `PlatformMetrics.Meter`.
- **Data dotčena:** `hybridrag_chunks` (read-only). · **Eventy:** žádné (query nikdy nepublikuje).
- **Priorita:** P0

### Edge cases UC-06-01
- **EC-06-01-01 — Méně než 50 matchů** · Trigger: korpus má jen 12 lexikálních shod · Očekávané chování: vrátí se 12, ne chyba, ne padding · Mechanismus: přirozený `LIMIT`; handler nepředpokládá plný K · Severity: P1 · Test: integrační — naseed 12 matchů, assert `Count==12`.
- **EC-06-01-02 — Přesně 50+ matchů, deterministický tie-break** · Trigger: 200 shod, mnoho se shodným BM25 score · Očekávané chování: stabilní pořadí — tie-break přes sekundární klíč `id ASC` v `ORDER BY` · Mechanismus: `ORDER BY score DESC, id ASC`; jinak Postgres vrátí nedeterministické pořadí a top-50 by „blikalo" mezi dotazy · Severity: P1 · Test: dva běhy stejného dotazu → identický seznam ID.
- **EC-06-01-03 — `TopK` mimo rozsah** · Trigger: orchestrátor pošle `TopK=0` nebo `TopK=100000` · Očekávané chování: validace — `TopK ∈ [1, Rag:Lexical:MaxTopK]` (default cap 200); jinak `ValidationException` errorCode `rag.lexical.topk_out_of_range` · Mechanismus: `LexicalRetrieveValidator` `.WithErrorCode("rag.lexical.topk_out_of_range")`; zákon §4 validace v behavioru, ne v handleru · Severity: P2 · Test: unit validator + integrační 400.
- **EC-06-01-04 — `score()` volání s nesprávným alias** · Trigger: `paradedb.score(id)` odkazuje na špatný klíč indexu · Očekávané chování: chyba se nesmí propagovat jako 500 bez kontextu — handler zachytí `PostgresException` a přeloží na `rag.lexical.scoring_failed` (500-class, logováno) · Mechanismus: try/catch kolem raw dotazu → `BusinessRuleException`; OTel error counter `platform.rag.lexical_errors{reason=scoring}` · Severity: P2 · Test: integrační s úmyslně rozbitým indexem → kontrolovaná chyba, ne neošetřená.
- **EC-06-01-05 — Korpus prázdný (žádné chunky tenantu)** · Trigger: nový tenant bez ingestu · Očekávané chování: prázdný list, latence ~0, žádná chyba · Mechanismus: `WHERE` nevrátí nic; viz též UC-06-10 · Severity: P1 · Test: tenant bez dat → `Count==0`.
- **EC-06-01-06 — Dotaz obsahuje `contextual_prefix` termíny** · Trigger: shoda padne jen do `ContextualPrefix`, ne do `Content` · Očekávané chování: BM25 index pokrývá **oba** sloupce (`content || ' ' || contextual_prefix`), shoda platí · Mechanismus: index definován nad konkatenací / multi-field BM25 schématem `pg_search` · Severity: P1 · Test: chunk s termínem jen v prefixu → nalezen.
- **EC-06-01-07 — Latence překročí SLA práh** · Trigger: BM25 dotaz trvá > `Rag:Lexical:SlaWarnMs` (default 250 ms) · Očekávané chování: výsledek se vrátí (ne timeout drop), ale zaloguje se WARN + inkrementuje `platform.rag.lexical_slow` · Mechanismus: stopwatch v handleru, prahový WARN; tvrdý DB statement timeout řeší EC-06-11-03 · Severity: P2 · Test: simulace pomalého dotazu → WARN log + metrika.
- **EC-06-01-08 — Souběžný ingest mění korpus během dotazu** · Trigger: worker právě vkládá nové chunky · Očekávané chování: read vidí konzistentní snapshot (MVCC); nově committed chunky se buď celé objeví, nebo ne — žádné půlené řádky · Mechanismus: Postgres MVCC; query bez explicitní transakce čte committed stav · Severity: P3 · Test: concurrent ingest + read, assert žádný partial/duplicitní `ChunkId`.

---

## UC-06-02 — Tenant + scope pre-filtr UVNITŘ BM25 SQL (uživatelský scope: tenant + vlastní privátní)

- **Actor / role:** user
- **Precondition:** Přihlášený uživatel; token nese `tenant_id` a `sub`; korpus obsahuje mix `Scope=Tenant` (firemní) i `Scope=User` chunků různých vlastníků.
- **Trigger:** `LexicalRetrieveQuery` s `RetrievalScope=UserDefault` (default pro běžný retrieval).
- **Main flow:**
  1. Handler načte `tenant = ITenantContext.TenantId`, `me = ITenantContext.UserId` (z tokenu, NIKDY z requestu).
  2. Do BM25 SQL vloží predikát: `tenant_id = {tenant} AND (scope = 'Tenant' OR (scope = 'User' AND owner_user_id = {me}))`.
  3. BM25 `@@@` se aplikuje **na již zúžené množině** (Postgres plánovač spojí BM25 index scan s pre-filtrem; pre-filtr je součást WHERE, ne post-processing v C#).
  4. Top-50 z této množiny → orchestrátoru.
- **Postcondition / záruky:** uživatel nikdy nevidí cizí `Scope=User` chunk; vidí všechny `Scope=Tenant`; top-K počítáno až po zúžení (ne děravé).
- **Tenancy / permissions:** Scope = User-level; RLS jako druhá vrstva. Identita z tokenu.
- **Reuse / canonical pattern:** identitní zdroj jako Billing/GDPR (`ITenantContext.UserId`); pre-filtr pattern v SQL leg.
- **Data dotčena:** `hybridrag_chunks`. · **Eventy:** žádné.
- **Priorita:** P0

### Edge cases UC-06-02
- **EC-06-02-01 — IDOR pokus přes tělo požadavku** · Trigger: klient/MCP pošle `ownerUserId` nebo `tenantId` v body/argumentu · Očekávané chování: hodnota IGNOROVÁNA; použije se výhradně token · Mechanismus: zákon §10 — identita z tokenu; `LexicalRetrieveCommand` ani nemá pole pro cizí owner; pokud by ho LLM dodal, nemapuje se · Severity: P0 · Test: integrační — dotaz s podvrženým `ownerUserId` cizího usera → 0 cizích řádků.
- **EC-06-02-02 — Cross-tenant leak** · Trigger: dva tenanti se shodným termínem, user tenantu A hledá · Očekávané chování: vrátí jen řádky tenantu A · Mechanismus: `tenant_id = {tenant}` v SQL + RLS GUC `app.principal_id`/tenant filtr · Severity: P0 · Test: dva tenanti, identický dokument; assert zero overlap ID.
- **EC-06-02-03 — Děravé top-K bez pre-filtru (regresní guard)** · Trigger: hypotetická implementace, kde by se BM25 udělalo bez tenant filtru a filtrovalo se v C# · Očekávané chování: ZAKÁZÁNO — test ověří, že počet vrácených legitimních kandidátů == min(K, počet legitimních matchů), ne méně · Mechanismus: pre-filtr v WHERE; architektonická poznámka + test · Severity: P0 · Test: 1000 cizích matchů + 50 vlastních, K=50 → assert 50 vlastních (ne „cizí spotřebovaly K").
- **EC-06-02-04 — User nemá žádné privátní chunky** · Trigger: nový user, jen firemní korpus · Očekávané chování: hledá jen v `Scope=Tenant`, funguje · Mechanismus: `scope='Tenant'` větev predikátu · Severity: P2 · Test: user bez `Scope=User` dat → výsledky jen z tenantu.
- **EC-06-02-05 — Privátní chunk jiného usera má vyšší BM25 než vlastní** · Trigger: cizí privátní řádek by byl relevantnější · Očekávané chování: stejně se nevrátí (filtr před BM25) · Mechanismus: pre-filtr · Severity: P0 · Test: cizí privátní s perfektní shodou → nevrácen.
- **EC-06-02-06 — Chybějící `tenant_id` claim** · Trigger: token bez `tenant_id` (degenerovaný/servisní) · Očekávané chování: NEINTERPRETOVAT jako „vidět vše" — query selže `UnauthorizedException`/`rag.lexical.no_tenant`, NEBO běží jako SYSTEM jen pokud volá worker/jobs kontext · Mechanismus: zákon §tenant isolation — missing claim ≠ wildcard; `HttpTenantContext` vs `SystemTenantContext` · Severity: P0 · Test: token bez tenantu z HTTP edge → odmítnuto, ne wildcard.
- **EC-06-02-07 — `Scope` enum hodnota mimo {Tenant,User}** · Trigger: poškozený řádek `scope='Global'` · Očekávané chování: nezahrnut do uživatelského scope (predikát ho nezachytí); zaloguj anomálii · Mechanismus: explicitní výčet `scope IN ('Tenant') OR owner match` — nic jiného neprojde · Severity: P3 · Test: vložit anomální scope → nevrácen uživateli.

---

## UC-06-03 — Tenant/firemní search (tenant-admin, permission-gated, napříč všemi usery)

- **Actor / role:** tenant-admin (drží permission `PlatformPermissions.RagTenantSearch` / `hybridrag.tenant_search`)
- **Precondition:** Volající má v tokenu permission claim pro firemní search; korpus obsahuje `Scope=User` chunky více vlastníků.
- **Trigger:** `LexicalRetrieveQuery` s `RetrievalScope=TenantWide` (volitelný flag `IncludeAllUsers=true`); endpoint gated `.RequirePermission(PlatformPermissions.RagTenantSearch)`.
- **Main flow:**
  1. Endpoint ověří permission (claim, ne DB lookup).
  2. Handler ověří, že volající permission skutečně drží (defence-in-depth — re-check `ITenantContext` permission), jinak `ForbiddenException` `rag.lexical.tenant_search_forbidden`.
  3. SQL pre-filtr se rozšíří: `tenant_id = {tenant} AND (scope = 'Tenant' OR scope = 'User')` — tj. bez `owner_user_id` omezení, ale **stále uvnitř tenantu**.
  4. BM25 top-50 napříč celým tenantem.
- **Postcondition / záruky:** firemní search nikdy neopustí hranici tenantu; přístup jen s permission; auditováno (kdo firemně hledal — viz EC).
- **Tenancy / permissions:** Scope = Tenant-wide, permission `hybridrag.tenant_search`. Cross-tenant nikdy.
- **Reuse / canonical pattern:** `.RequirePermission(...)` gating jako Identity admin endpointy; permission konstanta v `PlatformPermissions` (auto-seed, auto-grant adminovi).
- **Data dotčena:** `hybridrag_chunks`. · **Eventy:** volitelně audit záznam (read query → ne event, ale strukturovaný audit log).
- **Priorita:** P1

### Edge cases UC-06-03
- **EC-06-03-01 — Tenant-wide bez permission** · Trigger: běžný user pošle `RetrievalScope=TenantWide` · Očekávané chování: `ForbiddenException` `rag.lexical.tenant_search_forbidden`, fallback NENÍ tichý downgrade na UserDefault (musí být explicitní rozhodnutí — viz EC-06-03-02) · Mechanismus: permission re-check v handleru · Severity: P0 · Test: user bez permission → 403.
- **EC-06-03-02 — Politika při chybějící permission: deny vs degrade** · Trigger: orchestrátor žádá TenantWide pro usera bez práva · Očekávané chování: **deny** (403), NE tiché zúžení — jinak by se maskoval konfigurační bug · Mechanismus: explicitní; degrade jen pokud `Rag:Lexical:DegradeTenantSearch=true` (default false), pak vrátí UserDefault + WARN · Severity: P1 · Test: oba režimy configu.
- **EC-06-03-03 — Cross-tenant i pro admina** · Trigger: tenant-admin chce vidět cizí tenant · Očekávané chování: nemožné — `tenant_id = {tenant}` je vždy z tokenu admina · Mechanismus: SQL pre-filtr; platform-admin (`admin.` plane) je oddělený, mimo tuto oblast · Severity: P0 · Test: admin tenantu A → 0 řádků tenantu B.
- **EC-06-03-04 — Audit firemního prohledávání** · Trigger: tenant-admin spustí firemní search · Očekávané chování: zaloguje se strukturovaný audit (actor, dotaz-hash, počet hitů, timestamp UTC) — firemní search nad cizími privátními daty je citlivý · Mechanismus: explicitní audit log v handleru (NE `AuditInterceptor`, ten je na SaveChanges; query nemutuje) — strukturovaný `ILogger` event `rag.tenant_search.executed` · Severity: P1 · Test: assert log event s actor + count; dotaz-text se neloguje plný (jen hash/délka — PII).
- **EC-06-03-05 — `IncludeAllUsers=false` ale TenantWide** · Trigger: admin chce jen firemní (`Scope=Tenant`) bez privátních userů · Očekávané chování: predikát `scope='Tenant'` only · Mechanismus: flag řídí, zda se přidá `OR scope='User'` · Severity: P2 · Test: flag false → žádné `Scope=User` řádky.
- **EC-06-03-06 — Permission odebrána za běhu** · Trigger: admin práva ztratil, ale starý token claim ještě platí · Očekávané chování: claim je snapshot — platí do expirace tokenu; akceptovaný kompromis (jako celá platforma) · Mechanismus: zákon authorization — claims snapshot, refresh při re-auth · Severity: P3 · Test: dokumentační/known-limitation, ne blokující.

---

## UC-06-04 — Raw-SQL carve-out: parametrizovaný `FromSqlInterpolated`, injection guard, auditní výjimka

- **Actor / role:** system (návrhový/architektonický kontrakt); enforcement = code review + ArchUnitNET + lidský audit
- **Precondition:** Lexikální leg vyžaduje `@@@` / `paradedb.score()`, které LINQ nepřeloží → potřeba raw SQL.
- **Trigger:** implementace `LexicalRetrieveHandler` (a fallback `ts_rank_cd` jen pokud i ten potřebuje raw — viz UC-06-08, ten je LINQ-clean).
- **Main flow:**
  1. Veškerý dynamický vstup (`QueryText`, `tenant`, `me`, `topK`, k1/b) jde výhradně jako **interpolovaný parametr** v `FromSqlInterpolated($"… @@@ {queryText} …")` / `SqlQuery<T>($"…")` → EF/Npgsql generuje `$1, $2, …` placeholdery. **Žádná** `string.Format`, `+`, `$"{x}"` do `FromSqlRaw`.
  2. Konstanta SQL textu je **statická**, review-nutná, opatřená komentářem `// RAW-SQL CARVE-OUT (Law §5): pg_search @@@ has no LINQ surface. Input is parametrized, never concatenated. Reviewed: <PR>`.
  3. Tato výjimka je vyjmenovaná v `docs/` jako jeden ze **tří** povolených raw-SQL ostrůvků (pgvector DDL, RLS policy DDL, BM25 lexikální leg) — žádný čtvrtý bez rozhodnutí uživatele.
  4. ArchUnitNET pravidlo: `FromSqlRaw`/`ExecuteSqlRaw` zakázáno všude; `FromSqlInterpolated`/`SqlQuery` povoleno JEN v `HybridRag` lexikálním namespace (allowlist typů).
- **Postcondition / záruky:** žádná SQL injection surface; výjimka je auditovatelná, ohraničená, a nešíří se do dalších modulů.
- **Tenancy / permissions:** N/A (architektonický kontrakt) — ale parametrizace chrání tenant/owner predikáty před manipulací.
- **Reuse / canonical pattern:** analogicky k akceptovaným carve-outům (pgvector extension, RLS policy); ArchUnitNET allowlist jako u existujících boundary pravidel.
- **Data dotčena:** N/A (kontrakt). · **Eventy:** žádné.
- **Priorita:** P0

### Edge cases UC-06-04
- **EC-06-04-01 — `@@@` operátor injection v dotazu uživatele** · Trigger: uživatel zadá `foo @@@ bar OR 1=1` · Očekávané chování: celý řetězec je JEDEN parametr → `pg_search` ho zpracuje jako query-string termíny, ne jako SQL operátor; žádná injekce · Mechanismus: parametrizace — DB ho nikdy neparsuje jako SQL · Severity: P0 · Test: integrační — dotaz s `@@@`/`;`/`--`/`' OR '1'='1` → bezpečně tokenizováno, žádná chyba/leak.
- **EC-06-04-02 — pg_search query syntax injection** · Trigger: uživatel zneužije pg_search query DSL (`field:value`, boolean, fuzzy `~`) · Očekávané chování: rozhodnout režim — buď query jde jako **plain/raw term query** (`paradedb.match` / `query.term`), kde DSL není interpretován, NEBO se DSL povolí vědomě (UC-06-05) · Mechanismus: default = plain match function (ne `query_string` parser), aby uživatel neměnil sémantiku · Severity: P1 · Test: `field:secret` → hledá literál, ne strukturovaný field dotaz mimo allowlist.
- **EC-06-04-03 — Pokus o `FromSqlRaw` v PR** · Trigger: vývojář omylem použije `FromSqlRaw` s konkatenací · Očekávané chování: ArchUnitNET test červený · Mechanismus: boundary rule zakazuje `FromSqlRaw`/`ExecuteSqlRaw` v celém solution · Severity: P0 · Test: ArchUnitNET — žádné použití `*SqlRaw`.
- **EC-06-04-04 — Carve-out se šíří mimo lexikální leg** · Trigger: někdo použije `FromSqlInterpolated` v jiném handleru („je to přece povolené") · Očekávané chování: zakázáno mimo allowlistovaný namespace · Mechanismus: ArchUnitNET allowlist = jen `HybridRag.…Lexical` typy · Severity: P1 · Test: ArchUnitNET — `FromSqlInterpolated` jen v allowlistu.
- **EC-06-04-05 — Parametr `topK` / k1 / b jako parametr, ne literál v ORDER/LIMIT** · Trigger: `LIMIT` a tuning čísla · Očekávané chování: i numerika parametrizovaná (Npgsql podporuje `LIMIT $1`); k1/b validovány jako čísla v rozsahu před vložením (defence-in-depth) · Mechanismus: parametrizace + validace rozsahu · Severity: P2 · Test: `topK` z proměnné → korektní `$n`.
- **EC-06-04-06 — Projekce do keyless typu** · Trigger: `LexicalHit` projekce přes `SqlQuery<LexicalHit>` · Očekávané chování: `LexicalHit` je keyless DTO (žádné tracking, žádný RLS bypass přes owner context) · Mechanismus: read context, keyless entity / `SqlQuery` · Severity: P2 · Test: assert read-only, žádné EF tracking.
- **EC-06-04-07 — Logování SQL s parametry** · Trigger: diagnostické logování dotazu · Očekávané chování: query-text uživatele je potenciální PII → neloguj plný text v produkci (jen hash/délka); parametry maskovat · Mechanismus: EF command logging na `Information` jen v Dev; prod = bez parametrů · Severity: P1 · Test: prod log config neobsahuje plný dotaz.

---

## UC-06-05 — Exact match na ID / kód / SKU / jméno (tam, kde vektor over-fuzzuje)

- **Actor / role:** user | MCP klient
- **Precondition:** Korpus obsahuje chunky s identifikátory typu `INV-2026-0042`, `ERR_1729`, SKU, e-maily, čísla smluv.
- **Trigger:** `LexicalRetrieveQuery` s dotazem obsahujícím identifikátor; tohle je hlavní důvod existence lexikální nohy — dense/vektor retrieval u krátkých alfanumerických kódů „rozmazává" (sémanticky blízké, ale ne přesné), BM25 trefí přesně.
- **Main flow:**
  1. Tokenizer NESMÍ rozsekat / zahodit identifikátory (viz UC-06-06 — kódy obsahují `-`, `_`, číslice).
  2. BM25 dá vysoké skóre dokumentu obsahujícímu přesný token díky IDF (vzácný token = vysoká váha).
  3. Exact-ish kandidát se dostane na vrch lexikálního seznamu; v RRF (oblast 07) doplní/koriguje dense leg, který by ho minul.
- **Postcondition / záruky:** dotaz na unikátní kód vrátí dokument s tím kódem na předních pozicích lexikálního výstupu.
- **Tenancy / permissions:** dle scope (UC-06-02/03).
- **Reuse / canonical pattern:** tokenizer config UC-06-06; BM25 IDF chování `pg_search`.
- **Data dotčena:** `hybridrag_chunks`. · **Eventy:** žádné.
- **Priorita:** P0

### Edge cases UC-06-05
- **EC-06-05-01 — Identifikátor s pomlčkou/podtržítkem rozbit tokenizerem** · Trigger: `INV-2026-0042` se tokenizuje na `inv`, `2026`, `0042` · Očekávané chování: buď zachovat jako jeden token (tokenizer s `keyword`/whitespace pravidlem pro kódy), nebo zajistit, že kombinace tří tokenů stále jednoznačně vybere dokument · Mechanismus: tokenizer config UC-06-06 — pro identifikátorové korpusy `code`/`keyword` analyzer; rozhodnutí default vs per-collection · Severity: P0 · Test: dotaz `INV-2026-0042` → přesný dokument na pozici 1.
- **EC-06-05-02 — Case-insensitivita kódů** · Trigger: `err_1729` vs `ERR_1729` · Očekávané chování: lowercase normalizace v tokenizeru → shoda nezávisle na case · Mechanismus: `lowercase` filter v analyzer chainu · Severity: P1 · Test: obě varianty → stejný hit.
- **EC-06-05-03 — Vektor by over-fuzzoval (kontrolní)** · Trigger: dotaz `ERR_1729`, korpus má `ERR_1728`, `ERR_1730` · Očekávané chování: BM25 preferuje přesný `ERR_1729`; fuzzy sousedy nepovyšuje · Mechanismus: exact term match + IDF (default žádná fuzzy expanze; fuzzy jen explicitně) · Severity: P1 · Test: assert `ERR_1729` > `ERR_1728/1730` v lexikálním pořadí.
- **EC-06-05-04 — Identifikátor jen v `ContextualPrefix`** · Trigger: kód je v kontextovém prefixu chunku, ne v těle · Očekávané chování: nalezen (index pokrývá oba) · Mechanismus: multi-field BM25 (EC-06-01-06) · Severity: P2 · Test: kód jen v prefixu → hit.
- **EC-06-05-05 — Příliš častý „identifikátor"** · Trigger: kód, který je v každém dokumentu (např. company prefix `ACME-`) · Očekávané chování: IDF ho potlačí (nízká rozlišovací síla) — správně, není diskriminační · Mechanismus: BM25 IDF saturace · Severity: P3 · Test: ubikvitní token → nízká váha, nerozhoduje pořadí.
- **EC-06-05-06 — Numerický-only dotaz** · Trigger: `1729` · Očekávané chování: čísla jsou indexovaná (ne zahozená jako stop), shoda funguje · Mechanismus: tokenizer nezahazuje numerika · Severity: P1 · Test: `1729` → dokumenty s tím číslem.
- **EC-06-05-07 — E-mail / URL jako identifikátor** · Trigger: dotaz `jan.novak@acme.cz` · Očekávané chování: rozumná tokenizace (buď celé, nebo `jan`,`novak`,`acme`,`cz`) — dokumentovat zvolené chování · Mechanismus: tokenizer rozhodnutí; PII pozor — Content je `[Encrypted]`, ale BM25 index potřebuje plaintext (viz EC-06-09-05) · Severity: P2 · Test: e-mail dotaz → relevantní dokument.

---

## UC-06-06 — Analyzer / tokenizer konfigurace (jazyk, stemming, stop words)

- **Actor / role:** tenant-admin (volba per-collection) | system (default)
- **Precondition:** Collection má atribut `Language` (`cs` | `en` | `auto` | `code`); `pg_search` tokenizer/analyzer nakonfigurovatelný na BM25 indexu.
- **Trigger:** vytvoření BM25 indexu při ingestu collection; nebo změna jazyka collection (vyžaduje reindex, UC-06-09).
- **Main flow:**
  1. Při zakládání BM25 indexu se zvolí analyzer chain dle `Collection.Language`:
     - `en`: `lowercase` → `english` stemmer → anglické stop words.
     - `cs`: `lowercase` → unaccent (diakritika) → český stemming (pokud `pg_search` nabízí; jinak light/none + unaccent) → české stop words.
     - `code`/identifikátorový korpus: `lowercase`, BEZ stemmingu, BEZ stop words, zachování `-`/`_`.
     - `auto`: detekce jazyka při ingestu → uložení rozhodnutí (deterministicky pro reindex).
  2. Default při neuvedení = `Rag:Lexical:DefaultLanguage` (default `en`).
  3. Tentýž analyzer se MUSÍ použít na **index** i na **query** (jinak query token ≠ index token → 0 shod).
- **Postcondition / záruky:** dotaz i dokument prochází stejnou normalizací; jazykově konzistentní matching.
- **Tenancy / permissions:** per-collection volba gated `hybridrag.manage_collection`.
- **Reuse / canonical pattern:** config přes Options pattern (`RagLexicalOptions`), fail-fast validátor jako `JwtOptionsValidator`.
- **Data dotčena:** BM25 index metadata; `hybridrag_collections`. · **Eventy:** žádné (index management).
- **Priorita:** P1

### Edge cases UC-06-06
- **EC-06-06-01 — Query analyzer ≠ index analyzer** · Trigger: index `cs` se stemmingem, query bez stemmingu · Očekávané chování: ZAKÁZÁNO — query analyzer odvozen z indexu collection, ne nezávisle · Mechanismus: jeden zdroj pravdy (collection language); test že query path používá tutéž config · Severity: P0 · Test: dotaz na stemmovaný tvar (`běžící` vs `běžel`) → shoda jen když oba stemují stejně.
- **EC-06-06-02 — České stemming nedostupné v pg_search** · Trigger: `pg_search` nemá kvalitní `cs` stemmer · Očekávané chování: graceful — fallback na `unaccent + lowercase` bez stemmingu (horší recall, ne chyba); zaloguj capability · Mechanismus: capability detekce při index build; dokumentovaný kompromis · Severity: P1 · Test: `cs` collection bez stemmeru → index vznikne, hledá (bez stemmingu).
- **EC-06-06-03 — Diakritika: `běžící` vs `bezici`** · Trigger: uživatel píše bez diakritiky · Očekávané chování: `unaccent` na obou stranách → shoda · Mechanismus: unaccent filter v `cs`/`auto` chainu · Severity: P1 · Test: dotaz `bezici` → dokument `běžící`.
- **EC-06-06-04 — Stop words odstraní celý dotaz** · Trigger: dotaz `a v na` (samé stop words) · Očekávané chování: po odstranění prázdná množina → zero-retrieval (UC-06-10), ne chyba · Mechanismus: viz UC-06-10 · Severity: P1 · Test: stop-word-only dotaz → prázdný list.
- **EC-06-06-05 — Stop words u kódového korpusu zahazují `in`, `or`** · Trigger: identifikátor `OR-1729` v `code` collection · Očekávané chování: `code` analyzer NEMÁ stop words → `or` se nezahodí · Mechanismus: per-language stop word set; `code` = prázdný · Severity: P2 · Test: `OR-1729` v code collection → nalezen.
- **EC-06-06-06 — Změna jazyka collection bez reindexu** · Trigger: admin přepne `en→cs`, index zůstal `en` · Očekávané chování: vynutit reindex (UC-06-09) nebo zablokovat změnu dokud reindex; jinak nekonzistence · Mechanismus: změna `Language` → status `ReindexPending`; retrieval mezitím používá starý analyzer + WARN · Severity: P1 · Test: změna jazyka → reindex flag.
- **EC-06-06-07 — `auto` detekce nestabilní mezi ingesty** · Trigger: smíšený dokument → různá detekce · Očekávané chování: rozhodnutí jazyka je na úrovni **collection**, ne per-dokument (deterministické pro index); per-dokument multilingual řeší UC-06-12 · Mechanismus: language fixed per collection · Severity: P2 · Test: dva ingesty stejné collection → stejný analyzer.
- **EC-06-06-08 — Nepodporovaný jazyk** · Trigger: `Language='jp'` bez analyzeru · Očekávané chování: `ValidationException` `rag.lexical.unsupported_language` při zakládání collection, NE tiché degradování · Mechanismus: validátor proti allowlistu jazyků · Severity: P2 · Test: neznámý jazyk → 400 při create collection.

---

## UC-06-07 — BM25 parametry k1 / b (tuning) a defaultní hodnoty

- **Actor / role:** tenant-admin (per-collection tuning) | system (default)
- **Precondition:** BM25 ranking; `pg_search` umožňuje nastavit k1 (term-frequency saturace) a b (length normalizace).
- **Trigger:** konfigurace `Rag:Lexical:Bm25:K1` / `:B` (global default) nebo per-collection override; aplikuje se v ranking funkci dotazu / index config.
- **Main flow:**
  1. Default `k1 = 1.2`, `b = 0.75` (standardní BM25 hodnoty) — zapsáno v `RagLexicalOptions` s fail-fast validací rozsahu.
  2. Per-collection override (volitelný) v `hybridrag_collections.bm25_k1/bm25_b`.
  3. Hodnoty se předají do BM25 scoring (parametr dotazu nebo index-level config dle `pg_search` API) — VŽDY jako validovaná čísla, parametrizovaná (EC-06-04-05).
  4. Změna ovlivní pořadí, ne množinu kandidátů (kandidáti = match na `@@@`).
- **Postcondition / záruky:** deterministické ranking pro dané k1/b; změna tuning je auditovaná.
- **Tenancy / permissions:** override gated `hybridrag.manage_collection`.
- **Reuse / canonical pattern:** Options + validátor (fail-fast), per-collection sloupce.
- **Data dotčena:** `hybridrag_collections`. · **Eventy:** žádné.
- **Priorita:** P2

### Edge cases UC-06-07
- **EC-06-07-01 — k1/b mimo rozumný rozsah** · Trigger: `k1=-1` nebo `b=5` · Očekávané chování: `ValidationException` `rag.lexical.bm25_param_out_of_range`; rozumné meze `k1 ∈ [0, 5]`, `b ∈ [0, 1]` · Mechanismus: Options validátor · Severity: P2 · Test: mimo rozsah → 400/fail-fast.
- **EC-06-07-02 — `b=0` (vypnutá length-norm)** · Trigger: admin vypne délkovou normalizaci · Očekávané chování: povoleno (legitimní volba pro homogenní krátké dokumenty), zdokumentovat dopad · Mechanismus: validní rozsah · Severity: P3 · Test: `b=0` → dlouhé dokumenty nejsou penalizované.
- **EC-06-07-03 — Změna tuning vyžaduje reindex?** · Trigger: změna k1/b · Očekávané chování: pokud jsou k1/b **query-time** parametry, reindex NENÍ nutný; pokud jsou baked do indexu, reindex nutný — dokumentovat dle `pg_search` chování · Mechanismus: capability dependent; default preferovat query-time · Severity: P2 · Test: změna k1/b → pořadí se změní bez reindexu (query-time režim).
- **EC-06-07-04 — Default při chybějícím configu** · Trigger: config klíče chybí · Očekávané chování: použít 1.2 / 0.75, ne 0 · Mechanismus: Options default hodnoty · Severity: P1 · Test: bez configu → standardní default.
- **EC-06-07-05 — Per-collection override koliduje s globálem** · Trigger: collection má override, global jiný · Očekávané chování: collection override vyhrává · Mechanismus: precedence collection > global > hardcoded default · Severity: P3 · Test: override aplikován jen na tu collection.

---

## UC-06-08 — Fallback `ts_rank_cd` (LINQ-clean, GIN) když `pg_search` chybí

- **Actor / role:** system | user (transparentně)
- **Precondition:** `Rag:Lexical:Provider = ts_rank` (managed DB bez `pg_search`, např. RDS/Cloud SQL bez ParadeDB); `Chunk` má generated `SearchVector NpgsqlTsVector` s GIN indexem.
- **Trigger:** stejný `LexicalRetrieveQuery`; provider switch v handleru/strategy.
- **Main flow:**
  1. Handler detekuje provider z configu (rozhodnuto při startu, ne per-request — fail-fast pokud `pg_search` zvolen, ale rozšíření chybí — viz EC).
  2. Pro `ts_rank` provider se použije **čistý LINQ** (žádný raw-SQL carve-out): `EF.Functions.ToTsQuery`/`PlainToTsQuery` + `.Matches(searchVector)` + `EF.Functions.WebSearchToTsQuery` a `ts_rank_cd` přes `EF.Functions` (Npgsql full-text support).
  3. Tentýž pre-filtr (`tenant`, `scope`, `IsCurrent`, `NOT deleted`) jako LINQ `Where`.
  4. `OrderByDescending(ts_rank_cd) ... Take(50)`.
  5. Vrátí `LexicalHit` se **score** (ne BM25 — `ts_rank_cd`), ale orchestrátor stejně používá RANK (oblast 07), takže rozdílná škála nevadí.
  6. Metrika `provider=ts_rank`; explicitní WARN/info že běží degradovaný režim (horší kvalita — bez IDF/length-norm).
- **Postcondition / záruky:** retrieval funguje i bez ParadeDB, jen s nižší kvalitou rankingu; žádný crash.
- **Tenancy / permissions:** identický pre-filtr a scope jako BM25 cesta.
- **Reuse / canonical pattern:** čistý LINQ read (`GetProfileHandler.cs:12`), Npgsql full-text `EF.Functions`.
- **Data dotčena:** `hybridrag_chunks` (sloupec `search_vector` GIN). · **Eventy:** žádné.
- **Priorita:** P1

### Edge cases UC-06-08
- **EC-06-08-01 — `pg_search` zvolen v configu, ale rozšíření chybí** · Trigger: `Provider=pg_search`, DB ho nemá · Očekávané chování: **fail-fast při startu** (`RagLexicalOptions` validátor / startup health check) s jasnou hláškou „install pg_search or set Provider=ts_rank", NE runtime 500 při prvním dotazu · Mechanismus: startup capability probe (`SELECT 1 FROM pg_extension WHERE extname='pg_search'`); fail-fast jako `JwtOptionsValidator` · Severity: P0 · Test: bez rozšíření + `Provider=pg_search` → host se nenastartuje / health unhealthy.
- **EC-06-08-02 — Runtime ztráta `pg_search`** · Trigger: rozšíření odstraněno za běhu · Očekávané chování: dotaz selže kontrolovaně `rag.lexical.provider_unavailable`; NE tichý fallback na ts_rank (změna kvality musí být vědomá konfigurace) · Mechanismus: catch + jasný errorCode; auto-fallback jen pokud `Rag:Lexical:AutoFallback=true` (default false) · Severity: P1 · Test: drop extension → kontrolovaná chyba.
- **EC-06-08-03 — `SearchVector` generated column chybí pro ts_rank** · Trigger: `Provider=ts_rank`, ale migrace negenerovala sloupec · Očekávané chování: fail-fast / migrace povinná; bez sloupce nelze · Mechanismus: startup check; migrace přidává `search_vector` generated + GIN · Severity: P1 · Test: chybějící sloupec → startup fail.
- **EC-06-08-04 — `ts_rank` provider zachová identitu/scope filtr** · Trigger: degradovaná cesta · Očekávané chování: stejné tenant/scope/owner/IsCurrent/soft-delete predikáty · Mechanismus: sdílený filtr builder pro obě cesty · Severity: P0 · Test: cross-tenant test na ts_rank cestě → 0 leaků.
- **EC-06-08-05 — `ts_rank_cd` vs `ts_rank`** · Trigger: volba ranking funkce · Očekávané chování: `ts_rank_cd` (cover density) jako lepší z dvou špatných variant — dokumentovat, že stále nemá IDF/length-norm jako BM25 · Mechanismus: `EF.Functions` cd varianta · Severity: P2 · Test: použije se `_cd`.
- **EC-06-08-06 — `websearch_to_tsquery` parsing speciálních znaků** · Trigger: uživatelský dotaz s `"`, `-`, `OR` · Očekávané chování: `websearch_to_tsquery` (bezpečný, neházet na syntaxi) místo `to_tsquery` (který hází na malformed) · Mechanismus: Npgsql `EF.Functions.WebSearchToTsQuery` · Severity: P1 · Test: `foo -bar "baz"` → nehází, rozumný výsledek.
- **EC-06-08-07 — Konzistence top-K mezi providery** · Trigger: stejný dotaz na obou providerech · Očekávané chování: oba vrátí ≤50 z téže filtrované množiny; pořadí se LIŠÍ (jiný ranking) — to je očekávané, RRF to absorbuje · Mechanismus: dokumentovaný rozdíl kvality · Severity: P2 · Test: oba providery → stejná množina ID (případně jiné pořadí), žádný leak.

---

## UC-06-09 — BM25 index build / maintenance / reindex po ingestu chunků

- **Actor / role:** system/worker (po ingestu) | tenant-admin (manuální reindex) | jobs (periodická údržba)
- **Precondition:** Ingest pipeline (jiná oblast) vložila/aktualizovala `Chunk` řádky; BM25 index má reflektovat aktuální `IsCurrent` chunky.
- **Trigger:** dokončení ingest dávky (integration event z ingest oblasti) → handler ve Worker; nebo CRON údržbový job; nebo admin endpoint `POST /v1/hybridrag/collections/{id}/reindex`.
- **Main flow:**
  1. Po ingestu jsou nové chunky automaticky v BM25 indexu (pg_search index je transakční/aktualizovaný při insertu — pokud je `pg_search` index over tabulkou, není potřeba ruční rebuild pro běžné inserty).
  2. Pro `ts_rank` provider je `search_vector` **generated column** → také automatické.
  3. Reindex (úplný rebuild) je potřeba jen při: změně analyzeru/jazyka (UC-06-06), změně schématu indexu, nebo korupci. Reindex běží jako dlouhoběžná operace přes **Operations modul** (202 + status), ne v HTTP requestu.
  4. Reindex je per-collection, idempotentní, neblokuje retrieval (starý index slouží do swapu) — pokud `pg_search` umožňuje concurrent reindex; jinak status `ReindexPending` + retrieval degraduje/varuje.
- **Postcondition / záruky:** index odpovídá aktuálnímu `IsCurrent` stavu; reindex nezablokuje běžný provoz; auditováno.
- **Tenancy / permissions:** reindex gated `hybridrag.manage_collection`; per-tenant izolovaný.
- **Reuse / canonical pattern:** dlouhá operace = Operations modul (`IOperationStore`, 202 + `/operations/{id}`); ingest reakce = Wolverine handler ve Worker.
- **Data dotčena:** BM25 index, `hybridrag_chunks`, `operations`. · **Eventy:** konzumuje ingest-completed event; může publikovat `RagIndexRebuilt`.
- **Priorita:** P1

### Edge cases UC-06-09
- **EC-06-09-01 — Nový chunk hned dohledatelný** · Trigger: ingest committne chunk, vzápětí dotaz · Očekávané chování: chunk je v BM25 výsledku (index aktualizován v téže transakci jako insert) · Mechanismus: pg_search index transakční; MVCC viditelnost po commitu · Severity: P1 · Test: insert → okamžitý dotaz → nalezen.
- **EC-06-09-02 — Reindex drží HTTP request** · Trigger: admin spustí reindex velké collection synchronně · Očekávané chování: ZAKÁZÁNO — 202 + Operation, práce ve Worker · Mechanismus: Operations modul; zákon „long work returns 202" · Severity: P1 · Test: reindex velké collection → 202 + Location, ne blokující request.
- **EC-06-09-03 — Reindex za běhu dotazů** · Trigger: retrieval během reindexu · Očekávané chování: dotazy běží dál (starý index) nebo dostanou jasný `rag.lexical.reindex_in_progress` (dle capability); žádný crash / žádné půlené výsledky · Mechanismus: concurrent reindex / swap; status flag · Severity: P1 · Test: souběžný dotaz + reindex → konzistentní výsledky.
- **EC-06-09-04 — Soft-delete dokumentu vs index** · Trigger: dokument soft-deleted po ingestu · Očekávané chování: chunky vyloučeny dotazovým predikátem `NOT is_deleted` okamžitě (index je nemusí fyzicky odstranit — filtr to řeší); fyzický úklid při údržbě · Mechanismus: query-time filtr je zdroj pravdy, ne mazání z indexu · Severity: P0 · Test: soft-delete → ihned mimo výsledky (i kdyby v indexu zůstal).
- **EC-06-09-05 — `[Encrypted]` Content vs BM25 plaintext** · Trigger: `Content` je `[Encrypted]` at-rest, ale BM25 potřebuje tokenizovatelný plaintext · Očekávané chování: **rozhodnutí nutné** — BM25 nemůže indexovat ciphertext. Buď (a) lexikální index nad NEšifrovaným odvozeným polem (a přijmout, že lexikální index je plaintext → ohrožuje crypto-shred záruku), nebo (b) lexikální retrieval jen pro ne-PII collections. **STOP a eskalovat uživateli** (zákon §11 — kolize crypto-shred × BM25 nemá canonical řešení) · Mechanismus: known design tension, vyžaduje rozhodnutí; nezavádět tiše plaintext index nad PII · Severity: P0 · Test: design review gate — žádný BM25 index nad `[Encrypted]` sloupcem bez explicitního rozhodnutí.
- **EC-06-09-06 — Reindex selže v půlce** · Trigger: chyba během rebuildu · Očekávané chování: Operation → Failed; starý index zůstává funkční; `ReconcileStaleOperations` ošetří zaseknuté · Mechanismus: Operations reconcile job; atomický swap nebo rollback · Severity: P2 · Test: vynucená chyba reindexu → Operation Failed, retrieval funguje dál.
- **EC-06-09-07 — Duplicitní ingest event (idempotence indexace)** · Trigger: Wolverine doručí ingest event 2× · Očekávané chování: inbox dedup; reindex idempotentní (stejný výsledek) · Mechanismus: Wolverine inbox UNIQUE MessageId; reindex idempotentní design · Severity: P2 · Test: dvojí event → jeden efekt.
- **EC-06-09-08 — `IsCurrent` flip (nová verze chunku)** · Trigger: re-ingest dokumentu → staré chunky `IsCurrent=false`, nové `true` · Očekávané chování: retrieval vrací jen `IsCurrent=true`; staré verze mimo · Mechanismus: query predikát `is_current`; index je nemusí mazat · Severity: P0 · Test: nová verze → jen aktuální chunky v výsledku.

---

## UC-06-10 — Zero-retrieval (žádný lexikální match) → prázdný list, ne chyba

- **Actor / role:** user | MCP klient | system
- **Precondition:** Dotaz, který lexikálně nematchuje nic (neznámý termín, jen stop-words po filtraci, prázdno).
- **Trigger:** `LexicalRetrieveQuery` bez shod.
- **Main flow:**
  1. BM25 `@@@` nevrátí řádky → handler vrátí prázdný `IReadOnlyList<LexicalHit>`.
  2. Metrika `platform.rag.lexical_zero_hits` inkrement; (volitelně debug log s hash dotazu).
  3. Orchestrátor (oblast 07) dostane prázdný lexikální seznam a RRF poběží jen s dense legem (lexikální noha nepřispěje žádný rank).
- **Postcondition / záruky:** zero-retrieval NIKDY není výjimka — je to validní výsledek; hybridní retrieval se nerozbije.
- **Tenancy / permissions:** dle scope.
- **Reuse / canonical pattern:** prázdná kolekce, ne exception; RRF tolerance prázdné nohy (oblast 07).
- **Data dotčena:** žádná. · **Eventy:** žádné.
- **Priorita:** P0

### Edge cases UC-06-10
- **EC-06-10-01 — Prázdný / whitespace dotaz** · Trigger: `q=""` / `q="   "` · Očekávané chování: rozhodnutí — buď `ValidationException` `rag.lexical.empty_query` (preferováno na vstupu retrieval orchestrátoru), NEBO prázdný list. Pro lexikální leg samotný: prázdný po normalizaci → prázdný list (ne volat BM25 s prázdným query stringem) · Mechanismus: guard před SQL; nevolat `@@@ ''` · Severity: P1 · Test: prázdný dotaz → prázdný list / kontrolovaná 400, ne 500.
- **EC-06-10-02 — Jen stop-words** · Trigger: `q="the a of"` (en) · Očekávané chování: po stop-word filtru prázdná query → prázdný list, ne chyba · Mechanismus: tokenizer odstraní vše → guard → prázdný list · Severity: P1 · Test: stop-word-only → prázdný list.
- **EC-06-10-03 — Dotaz jen speciální znaky / interpunkce** · Trigger: `q="!!! ??? ;;;"` · Očekávané chování: po tokenizaci prázdno → prázdný list; žádná injekce (parametrizováno) · Mechanismus: tokenizer + guard · Severity: P1 · Test: interpunkce → prázdný list, žádná chyba.
- **EC-06-10-04 — Termín existuje v jiném tenantu, ne v mém** · Trigger: dotaz matchuje cizí tenant · Očekávané chování: prázdný list (pre-filtr) — ne leak · Mechanismus: tenant pre-filtr · Severity: P0 · Test: match jen v cizím tenantu → prázdný list pro mě.
- **EC-06-10-05 — Termín jen v soft-deleted / ne-current** · Trigger: jediný match je soft-deleted nebo `IsCurrent=false` · Očekávané chování: prázdný list · Mechanismus: `NOT is_deleted AND is_current` predikát · Severity: P0 · Test: match jen v deleted → prázdný list.
- **EC-06-10-06 — Zero-hits nezablokuje RRF** · Trigger: lexikální prázdný, dense má výsledky · Očekávané chování: hybridní výsledek = jen dense (oblast 07 zvládne prázdnou nohu) · Mechanismus: RRF tolerance · Severity: P1 · Test: prázdná lexikální noha → hybridní výstup = dense.

---

## UC-06-11 — Latence, metrika a BM25 nad velkým korpusem

- **Actor / role:** system (observabilita) | SRE
- **Precondition:** Velký tenant korpus (statisíce–miliony chunků); BM25/GIN index existuje.
- **Trigger:** každý lexikální dotaz; agregace v OTel.
- **Main flow:**
  1. Handler měří wall-clock BM25 dotazu → `platform.rag.lexical_latency` (histogram, tagy `provider`, `scope`).
  2. Počítadla: `platform.rag.lexical_hits`, `_zero_hits`, `_slow`, `_errors{reason}`.
  3. Index scan + `LIMIT 50` udržuje latenci sub-lineární vůči velikosti korpusu (BM25/GIN index, ne seq scan).
  4. Tvrdý DB statement timeout chrání před runaway dotazem.
- **Postcondition / záruky:** latence pozorovatelná; velký korpus neznamená lineární zpomalení; runaway dotaz je useknutý kontrolovaně.
- **Tenancy / permissions:** N/A (telemetrie); metriky bez PII (žádný dotaz-text v tagu).
- **Reuse / canonical pattern:** `PlatformMetrics.Meter` (`platform.rag.*`), `.AddMeter` v `AddPlatformTelemetry`.
- **Data dotčena:** žádná. · **Eventy:** žádné.
- **Priorita:** P2

### Edge cases UC-06-11
- **EC-06-11-01 — Metrika není exportovaná** · Trigger: instrument na jiném Meteru než `ModularPlatform` · Očekávané chování: použít `PlatformMetrics.Meter`, jinak silently neexportováno · Mechanismus: zákon custom metrics — jen `PlatformMetrics.Meter` · Severity: P2 · Test: assert instrument na správném Meteru.
- **EC-06-11-02 — PII v metrice/logu** · Trigger: tag/label obsahuje dotaz-text · Očekávané chování: nikdy plný dotaz v metrice/logu (cardinality + PII); jen délka/hash · Mechanismus: review; tagy jen low-cardinality · Severity: P1 · Test: assert žádný `query` tag.
- **EC-06-11-03 — Runaway dotaz (patologický termín)** · Trigger: extrémně častý termín, obrovský match set · Očekávané chování: statement timeout (`Rag:Lexical:StatementTimeoutMs`, default 2 s) → kontrolovaná `rag.lexical.timeout`, ne visící connection · Mechanismus: per-command timeout na read connection · Severity: P1 · Test: simulace pomalého dotazu → timeout errorCode.
- **EC-06-11-04 — Seq scan místo index scan** · Trigger: index nepoužit (špatný plán / chybějící index) · Očekávané chování: detekovat regresi výkonu; index MUSÍ existovat (migrace) · Mechanismus: EXPLAIN v testu / health; migrace garantuje index · Severity: P2 · Test: EXPLAIN ukazuje index scan, ne seq.
- **EC-06-11-05 — Latence roste s tenant velikostí** · Trigger: velký tenant · Očekávané chování: sub-lineární (index + LIMIT); ne O(n) · Mechanismus: BM25/GIN top-K · Severity: P2 · Test: 10k vs 1M chunků → latence neroste lineárně.
- **EC-06-11-06 — Histogram bucket rozsah** · Trigger: latence mimo buckety · Očekávané chování: rozumné hranice (1–2000 ms) · Mechanismus: explicit bucket config · Severity: P3 · Test: latence se zaznamená do správného bucketu.

---

## UC-06-12 — Multilingual dokument, encoding a Unicode

- **Actor / role:** system | user
- **Precondition:** Korpus s vícejazyčnými dokumenty / smíšenými jazyky v jednom chunku; různé encodingy při ingestu (řeší ingest, ale lexikální leg musí zvládnout výsledný Unicode).
- **Trigger:** lexikální dotaz nad multilingual collection.
- **Main flow:**
  1. Collection má jeden `Language` (UC-06-06), ale dokument může obsahovat i cizí termíny. Analyzer collection se aplikuje uniformně.
  2. Pro skutečně vícejazyčný korpus = doporučená praxe oddělené collections per jazyk; jinak `Language=auto`/multi s kompromisem v recall.
  3. Unicode normalizace (NFC) a UTF-8 garantována (Postgres `UTF8` encoding); diakritika přes unaccent (UC-06-06).
- **Postcondition / záruky:** žádné mojibake; konzistentní matching i pro non-ASCII; rozumný recall i u smíšených dokumentů.
- **Tenancy / permissions:** dle scope.
- **Reuse / canonical pattern:** tokenizer config UC-06-06; UTF-8 DB konvence.
- **Data dotčena:** `hybridrag_chunks`. · **Eventy:** žádné.
- **Priorita:** P2

### Edge cases UC-06-12
- **EC-06-12-01 — Smíšený CS/EN dokument** · Trigger: chunk obsahuje českou i anglickou větu, collection `cs` · Očekávané chování: české termíny stemují/unaccentují korektně; anglické se indexují jako tokeny (bez EN stemmingu) — nižší recall na EN, akceptováno; doporučit per-language collection · Mechanismus: jednotný analyzer per collection; dokumentovaný kompromis · Severity: P2 · Test: smíšený dokument → CS dotaz hit, EN dotaz částečně.
- **EC-06-12-02 — Unicode NFC vs NFD (diakritika jako kombinující znaky)** · Trigger: `č` jako `c + ̌` (NFD) vs precomposed (NFC) · Očekávané chování: normalizace NFC při ingestu/dotazu → shoda; jinak by se identický text nematchoval · Mechanismus: NFC normalizace na obou stranách (app vrstva před tokenizací nebo unaccent) · Severity: P1 · Test: NFD dotaz vs NFC dokument → shoda.
- **EC-06-12-03 — Emoji / non-BMP znaky** · Trigger: dotaz/obsah s emoji nebo CJK · Očekávané chování: nepadnout; tokenizer je buď zahodí (non-word) nebo indexuje jako tokeny; UTF-8 bezpečně · Mechanismus: Unicode-safe tokenizer; parametrizace · Severity: P2 · Test: emoji dotaz → žádná chyba.
- **EC-06-12-04 — Mojibake při špatném encodingu** · Trigger: dokument byl ingestován v latin1 a uložen jako UTF-8 mangled · Očekávané chování: to je ingest bug (mimo tuto oblast), ale lexikální leg nesmí spadnout — pracuje s tím, co je v DB; garantuje UTF8 na DB úrovni · Mechanismus: DB `UTF8`; ingest validuje encoding · Severity: P3 · Test: korektně uložený Unicode → korektní match.
- **EC-06-12-05 — Velmi dlouhý dotaz** · Trigger: dotaz tisíce slov · Očekávané chování: omezit délku dotazu `Rag:Lexical:MaxQueryChars` (default 2000) → `ValidationException` `rag.lexical.query_too_long`, ne DoS na tokenizer/BM25 · Mechanismus: validátor délky · Severity: P2 · Test: nadlimitní dotaz → 400.
- **EC-06-12-06 — Pravo-levé písmo / kombinace skriptů** · Trigger: arabský/hebrejský text · Očekávané chování: indexuje a hledá bez chyby; směr písma je UI věc, ne retrieval · Mechanismus: Unicode-safe · Severity: P3 · Test: RTL termín → match.
- **EC-06-12-07 — Collation a case-folding pro non-ASCII** · Trigger: `İ`/`i` turecké, řecké sigma · Očekávané chování: lowercase folding přes tokenizer (ne DB collation závislé na locale) pro deterministický výsledek · Mechanismus: analyzer `lowercase` filter, locale-independent kde možné · Severity: P3 · Test: non-ASCII case → konzistentní folding.

---

## Souhrn priorit a křížové odkazy

| UC | Téma | Priorita |
|---|---|---|
| UC-06-01 | BM25 retrieval přes `@@@`, top-50 | P0 |
| UC-06-02 | Tenant+scope pre-filtr (user scope) | P0 |
| UC-06-03 | Firemní search (tenant-admin, permission) | P1 |
| UC-06-04 | Raw-SQL carve-out + injection guard | P0 |
| UC-06-05 | Exact match ID/SKU/kód | P0 |
| UC-06-06 | Analyzer/tokenizer (jazyk, stemming, stop) | P1 |
| UC-06-07 | BM25 k1/b tuning | P2 |
| UC-06-08 | `ts_rank_cd` fallback (managed DB) | P1 |
| UC-06-09 | Index build/maintenance/reindex | P1 |
| UC-06-10 | Zero-retrieval → prázdný list | P0 |
| UC-06-11 | Latence/metrika, velký korpus | P2 |
| UC-06-12 | Multilingual / encoding / Unicode | P2 |

**Otevřená rozhodnutí pro uživatele (zákon §11 — STOP, neimprovizovat):**
1. **EC-06-09-05** — kolize `[Encrypted][PersonalData] Content` × BM25 (index potřebuje plaintext). BM25 nad ciphertextem nelze; plaintext lexikální index nad PII oslabuje crypto-shred záruku. Rozhodnout: lexikální retrieval jen pro ne-PII collections, vs oddělený režim, vs akceptovaný plaintext index.
2. **EC-06-04-02** — povolit uživateli `pg_search` query DSL (boolean/fuzzy/field), nebo vždy plain term match.
3. **EC-06-08-02** — auto-fallback `pg_search → ts_rank` za běhu (default OFF, kvalita se mění tiše) vs tvrdá chyba.
4. **EC-06-10-01** — prázdný dotaz: validační chyba na orchestrátoru vs tichý prázdný list na lexikální noze.
5. **Errorové kódy** k doplnění do `SharedResource.resx` (`en`+`cs`): `rag.lexical.topk_out_of_range`, `rag.lexical.scoring_failed`, `rag.lexical.no_tenant`, `rag.lexical.tenant_search_forbidden`, `rag.lexical.unsupported_language`, `rag.lexical.bm25_param_out_of_range`, `rag.lexical.provider_unavailable`, `rag.lexical.reindex_in_progress`, `rag.lexical.empty_query`, `rag.lexical.query_too_long`, `rag.lexical.timeout`.
