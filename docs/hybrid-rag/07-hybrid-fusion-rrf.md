# Oblast 07 — Hybrid fusion (RRF)

Tato oblast pokrývá **Reciprocal Rank Fusion** — deterministické sloučení dvou (nebo více) nezávislých ranked listů kandidátů (denzní vektorový retrieval přes pgvector `CosineDistance` + lexikální BM25 přes `pg_search` (`ts_rank_cd` fallback)) do jednoho sjednoceného pořadí, výhradně v C# nad rank pozicemi (NIKDY mícháním raw skóre, NIKDY v SQL). Mapuje se na build fázi **Retrieval & Fusion** (mezi `Oblast 06 — Lexical/BM25 retrieval` a `Oblast 08 — Reranking`); fúzovaný výstup je vstupem reranku (Oblast 08) a následně answer-generation (Oblast 09). RRF je čistá in-memory transformace dvou seznamů `(ChunkId, rank)` → jeden seznam `(ChunkId, rrfScore)`; je to **query-side** logika, nemutuje DB, neotvírá transakci, nepublikuje eventy — žije v `IQuery<T>` handleru, případně jako čistá pomocná metoda volaná z hybrid-search query handleru.

Vzorec: `rrf(d) = Σ_listy 1 / (k + rank_list(d))`, kde `k = 60` (konfigurovatelné `Rag:Fusion:K`), `rank` je 1-based pozice dokumentu v daném listu, a dokument chybějící v listu do součtu **nepřispívá vůbec** (ekvivalent přínosu 0 za daný list, NE `1/(k+∞)` ani penalizace).

---

## UC-07-01 — Fúze dvou ranked listů (vektor + BM25) přes RRF v C#
- **Actor / role:** system/worker (volá se uvnitř hybrid-search query handleru; iniciátor je `user` nebo tenant-admin přes search endpoint)
- **Precondition:** existují dva již seřazené seznamy kandidátů pro tentýž dotaz: `denseList: IReadOnlyList<RetrievalCandidate>` (řazeno vzestupně podle `CosineDistance`) a `lexicalList: IReadOnlyList<RetrievalCandidate>` (řazeno sestupně podle BM25 skóre (`pg_search`)). Oba listy nesou `ChunkId` + svůj per-list rank. Oba pocházejí ze stejně tenant/user-scoped retrievalu (RLS už proběhla na DB úrovni).
- **Trigger:** in-process volání `RrfFusion.Fuse(IReadOnlyList<RankedList> lists, FusionOptions opts)` z `HybridSearchHandler` (žádný vlastní HTTP endpoint — fúze je krok query handleru `POST /v1/rag/search`).
- **Main flow:**
  1. `HybridSearchHandler` (query) dostane `denseList` a `lexicalList` z paralelně spuštěných retrieverů (Oblast 05/06).
  2. Každý list je převeden na `Dictionary<Guid ChunkId, int Rank>` (rank = 1-based index). Pozor: input listy MUSÍ být deduplikované per-list (jeden ChunkId v jednom listu max 1×); pokud ne, použije se nejlepší (nejmenší) rank.
  3. Sestaví se sjednocená množina `ChunkId` = union klíčů obou dictionary (ekvivalent FULL OUTER JOIN přes `ChunkId`).
  4. Pro každý `ChunkId` se spočítá `score = Σ (1.0 / (k + rank))` jen přes listy, kde se vyskytuje. `k` z `FusionOptions.K` (default 60).
  5. Výsledek se seřadí sestupně podle `score`; při shodě deterministický tie-break (viz UC-07-06).
  6. Ořez na `opts.TopN` (default 50, viz UC-07-03).
  7. Vrátí `FusionResult` = seřazený `IReadOnlyList<FusedCandidate(ChunkId, RrfScore, PerListRanks, Sources)>` + flag `Degraded`/`Partial` (viz UC-07-05).
- **Postcondition / záruky:** žádná mutace DB, žádný event, žádná transakce. Funkce je **čistá** a **deterministická** pro daný vstup (stejné listy → bitově stejné pořadí). Idempotence triviální (read-only).
- **Tenancy / permissions:** fúze sama nečte DB → RLS se uplatnila už při retrievalu vstupních listů. Scope (Tenant|User) je vlastnost kandidátů, fúze ji jen propaguje; NESMÍ míchat kandidáty z různých scope, než je retrieval povolil. Permission na celý search řeší endpoint (`PlatformPermissions.RagSearch` / tenant-wide varianta), ne fúze.
- **Reuse / canonical pattern:** read query handler bez transakce = `GetProfileHandler.cs:12` (IReadDbContextFactory, žádný outbox). Čistá fúzní logika = nová `internal static class RrfFusion` (žádný platform vzor — je to ryzí algoritmus, zákon „EF/LINQ only, RRF v C#"). Determinismus poradí = analog tie-breaku v paging (`PlatformMetrics.cs:19` pro metriky).
- **Data dotčena:** žádná tabulka (in-memory `Chunk` projekce). · **Eventy:** žádné (query).
- **Priorita:** P0

### Edge cases UC-07-01
- **EC-07-01-01 — Míchání raw skóre místo ranku** · Trigger: implementátor v součtu omylem použije `CosineDistance` / `ts_rank` hodnotu místo `1/(k+rank)` · Očekávané chování: MUSÍ se fúzovat výhradně přes rank pozici; cosine ∈ [0,1] a BM25 skóre ∈ jiná/neohraničená škála — jejich součet je nesmyslný a tiše degraduje kvalitu · Mechanismus: zákon „RRF v C#, NE míchá raw skóre = bug"; `RrfFusion.Fuse` přijímá `RankedList` který drží JEN `ChunkId`+`Rank`, raw skóre se do fúze ani nepředává (typový guard) · Severity: P0 · Test: unit — sestrojit dva listy kde raw-score-mix dá jiné pořadí než rank-RRF; assert výstup == rank-RRF pořadí.
- **EC-07-01-02 — Neseřazený vstupní list** · Trigger: retriever vrátí kandidáty v náhodném pořadí (rank odvozen z indexu, ale list není seřazen podle skóre) · Očekávané chování: fúze věří rank pozicím jak přišly; pokud handler odvozuje rank z indexu, MUSÍ retriever garantovat seřazení PŘED předáním — jinak GIGO · Mechanismus: kontrakt `RankedList` nese explicitní `Rank` per kandidát (ne odvozený z indexu uvnitř fúze), retriever ho stanoví při řazení (`OrderBy CosineDistance` / BM25 `pg_search` řazení) · Severity: P1 · Test: unit — list s explicitními ranky v nemonotónním pořadí; assert fúze respektuje `Rank`, ne pořadí prvků.
- **EC-07-01-03 — Duplicitní ChunkId uvnitř jednoho listu** · Trigger: retriever vrátí tentýž `ChunkId` 2× (např. chunk matchnul dvě query-varianty) · Očekávané chování: per-list dedup na NEJLEPŠÍ (nejmenší) rank PŘED fúzí; chunk nesmí dostat dvojitý příspěvek ze stejného listu · Mechanismus: `denseList.GroupBy(ChunkId).Select(min Rank)` při stavbě dictionary; zákon „handlery idempotentní" + RRF-taxonomie „dedup přes id" · Severity: P1 · Test: unit — list s duplicitou; assert příspěvek = `1/(k+min_rank)` jednou.
- **EC-07-01-04 — `k = 0` nebo záporné** · Trigger: chybná konfigurace `Rag:Fusion:K=0` → dělení `1/(0+rank)`, nebo `K=-60` → dělení nulou/zápor · Očekávané chování: `FusionOptions` validátor fail-fast (k ≥ 1); `k=0` je matematicky platné ale mění váhu top pozic extrémně → zakázat mimo dokumentovaný rozsah · Mechanismus: options validátor (`AddOptionsWithValidateOnStart` vzor jako `JwtOptionsValidator`), startup fail-fast · Severity: P2 · Test: unit options validátor — `K=0`/`-1` → ValidationException; `K=60` OK.
- **EC-07-01-05 — Identický ChunkId přes oba listy (overlap)** · Trigger: chunk je v dense rank 3 i lexical rank 5 · Očekávané chování: score = `1/(60+3) + 1/(60+5)` (sčítá oba příspěvky); overlap-chunky správně bublají nahoru · Mechanismus: union klíčů + součet přes listy obsahující klíč; canonical RRF · Severity: P0 · Test: unit — assert overlap chunk má vyšší skóre než stejně-umístěný chunk v jen jednom listu.
- **EC-07-01-06 — Float přesnost / asociativita součtu** · Trigger: pořadí sčítání listů ovlivní poslední bit `double` → nestabilní tie-break · Očekávané chování: součet v deterministickém pořadí listů (fixní pořadí: dense, lexical, …); tie-break NESMÍ záviset na neurčitém float bitu (viz UC-07-06) · Mechanismus: pevné pořadí iterace listů + sekundární deterministický tie-break klíč · Severity: P2 · Test: unit — permutace pořadí listů v `Fuse`; assert identický výstup.
- **EC-07-01-07 — Velmi velké ranky (k << rank)** · Trigger: list má 10 000 kandidátů, rank 9 999 → příspěvek `1/10059` ≈ 0 · Očekávané chování: žádný overflow/underflow; příspěvek korektně malý ale > 0; chunk hluboko v jednom listu neporazí overlap nahoře · Mechanismus: `double` aritmetika, žádný cap na rank kromě top-N input limitu (viz EC-07-03-02) · Severity: P3 · Test: unit — rank 9999 vs overlap rank 1+1; assert overlap vyhraje.

---

## UC-07-02 — Dokument chybějící v jednom listu přispívá nulou (sparse union)
- **Actor / role:** system/worker
- **Precondition:** `ChunkId` se vyskytuje v `denseList` (rank r), ale NENÍ v `lexicalList` (lexikální retriever ho vůbec nevrátil — např. sémantická shoda bez lexikálního překryvu).
- **Trigger:** in-process `RrfFusion.Fuse` (tatáž cesta jako UC-07-01).
- **Main flow:**
  1. Při stavbě union se chunk objeví v klíčích.
  2. Součet iteruje listy; pro `lexicalList` chunk NENÍ → tento list do součtu **nepřidá nic** (žádný člen, ne `1/(k+∞)`, ne penalty).
  3. Výsledné score = `1/(k + r)` (jen dense příspěvek).
  4. Chunk normálně soutěží v žebříčku; sémanticky silný unikát se může dostat do top-50 i bez lexikální shody.
- **Postcondition / záruky:** chybějící výskyt = nulový příspěvek za daný list, NE diskvalifikace. To je jádro RRF „recall přes komplementární retrievery".
- **Tenancy / permissions:** beze změny vůči UC-07-01.
- **Reuse / canonical pattern:** čistý algoritmus (`RrfFusion`); FULL OUTER JOIN ekvivalent = union dictionary klíčů v C# (žádný JOIN v DB — zákon „EF/LINQ only", a tady ani DB).
- **Data dotčena:** in-memory. · **Eventy:** žádné.
- **Priorita:** P0

### Edge cases UC-07-02
- **EC-07-02-01 — Implementace přes INNER JOIN (jen overlap)** · Trigger: bug — fúze vezme jen průnik obou listů · Očekávané chování: MUSÍ to být UNION (full outer), jinak se ztratí unikátní vysoce-relevantní kandidáti jednoho retrieveru → tichý recall propad · Mechanismus: union klíčů `dense.Keys.Union(lexical.Keys)`; RRF-taxonomie „dokument chybějící v jednom listu přispívá 0" · Severity: P0 · Test: unit — dense-only chunk; assert je ve výstupu se score `1/(k+r)`.
- **EC-07-02-02 — Penalizace za chybění (`1/(k+N+1)`)** · Trigger: implementace přiřadí chybějícímu chunku „rank N+1" místo nuly · Očekávané chování: chybění = 0 příspěvek, NE umělý nejhorší rank (to by zkreslilo škálu a poškodilo unikáty) · Mechanismus: součet jen přes přítomné listy · Severity: P1 · Test: unit — porovnat skóre dense-only chunku proti očekávané čisté `1/(k+r)`; assert žádný lexical člen.
- **EC-07-02-03 — Chunk v žádném listu, ale dotázán na něj** · Trigger: caller se ptá na score pro `ChunkId` mimo oba listy · Očekávané chování: není ve výstupu vůbec (score by bylo 0 = nepřítomen); fúze nevrací nulové duchy · Mechanismus: výstup = jen klíče z union · Severity: P3 · Test: unit — assert neznámý id není ve výstupu.
- **EC-07-02-04 — Asymetrické délky listů** · Trigger: dense vrátí 50, lexical vrátí 3 · Očekávané chování: korektní union; 47 dense-only + 3 možná overlap; žádný index-out-of-range, žádné zarovnávání délek · Mechanismus: union, ne zip · Severity: P2 · Test: unit — listy 50 vs 3; assert |výstup| = |union|.

---

## UC-07-03 — Ořez fúzovaného výstupu na top-50
- **Actor / role:** system/worker
- **Precondition:** union obou listů má > 50 unikátních `ChunkId` (běžné: dense 50 + lexical 50, malý overlap → ~80–100 unikátů).
- **Trigger:** in-process `RrfFusion.Fuse` s `opts.TopN = 50` (`Rag:Fusion:TopN`).
- **Main flow:**
  1. Spočítá se RRF score pro celou union množinu.
  2. Seřadí se sestupně + tie-break.
  3. `Take(opts.TopN)` → vrátí prvních 50.
  4. Tento top-50 je kandidátní množina pro Cohere rerank (Oblast 08), který ji typicky zúží na top-5/10.
- **Postcondition / záruky:** výstup ≤ TopN; pořadí deterministické; ořez se děje AŽ PO kompletním scoringu celé union (ne během, aby se neztratil pozdě-bublající overlap).
- **Tenancy / permissions:** beze změny.
- **Reuse / canonical pattern:** paging/`Take` vzor (čistý LINQ); analog `Paged` ořezu v read query.
- **Data dotčena:** in-memory. · **Eventy:** žádné.
- **Priorita:** P1

### Edge cases UC-07-03
- **EC-07-03-01 — Ořez PŘED scoringem (early truncation)** · Trigger: implementace ořízne každý list na 50 a pak fúzuje JEN těch 50 — ale fúzuje se až union, takže overlap-chunk hluboko v jednom listu se může přesunout nahoru; předčasný per-list ořez už proběhl v retrievalu (input top-50 per list) → to je OK, ale finální `Take` MUSÍ být po fúzním scoringu · Očekávané chování: nejdřív skóruj celou union (≤100), pak Take(50) · Mechanismus: pořadí kroků v `Fuse`; canonical RRF · Severity: P1 · Test: unit — overlap chunk umístěný v dense=49, lexical=48 (oba blízko hrany vstupních listů) musí díky součtu skončit nad některými top-input chunky; assert je ve výstupních top-50.
- **EC-07-03-02 — Union menší než TopN** · Trigger: union má 12 chunků, TopN=50 · Očekávané chování: vrátí všech 12 (žádné padding, žádné duplikáty na dorovnání) · Mechanismus: `Take(50)` na 12 prvcích = 12 · Severity: P2 · Test: unit — 12 chunků; assert |výstup| = 12.
- **EC-07-03-03 — TopN = 0 nebo záporné** · Trigger: konfigurace `Rag:Fusion:TopN=0` · Očekávané chování: options validátor fail-fast (TopN ≥ 1); 0 by vrátilo prázdno a rozbilo answer-gen · Mechanismus: options validátor · Severity: P2 · Test: unit — TopN=0 → ValidationException.
- **EC-07-03-04 — TopN větší než kapacita reranku** · Trigger: TopN=200, ale Cohere rerank-3.5 má limit kandidátů · Očekávané chování: fúze vrátí 200, ale Oblast 08 si ořízne na svůj limit; fúze sama nelimituje podle downstreamu, jen loguje pokud TopN > doporučené (config warn) · Mechanismus: hranice odpovědnosti — rerank guard je v Oblast 08 · Severity: P3 · Test: unit — TopN=200 vrací 200; (rerank cap test patří Oblast 08).
- **EC-07-03-05 — Tie přesně na hranici TopN (50. a 51. mají stejné skóre)** · Trigger: chunk #50 a #51 mají identický RRF score · Očekávané chování: deterministický tie-break rozhodne který se vejde; výsledek stabilní napříč běhy (ne náhodné „který zrovna") · Mechanismus: sekundární tie-break klíč PŘED `Take` (viz UC-07-06) · Severity: P1 · Test: unit — 51 chunků s tie na hranici; assert opakovaný běh dá stejný 50. prvek.

---

## UC-07-04 — Oba ranked listy prázdné → zero-retrieval fallback
- **Actor / role:** user / tenant-admin (přes search), system/worker provádí
- **Precondition:** dotaz nevrátil ŽÁDNÉ kandidáty z dense ani lexical (prázdný korpus, příliš restriktivní filtr, nebo dotaz mimo doménu).
- **Trigger:** `RrfFusion.Fuse([], [])` z `HybridSearchHandler`.
- **Main flow:**
  1. Union obou prázdných listů = ∅.
  2. Fúze vrátí prázdný `FusionResult` s `Empty = true` (NE výjimka, NE null).
  3. `HybridSearchHandler` na základě `Empty` vrátí explicitní „zero-retrieval" odpověď: search endpoint → 200 s prázdným `items` + `meta.retrieval = "empty"`; answer-gen (Oblast 09) → odpoví „nemám podklad" místo halucinace, bez citací.
- **Postcondition / záruky:** žádná výjimka při prázdnu; downstream dostane explicitní prázdno, NE tichou polovinu. Žádný rerank (Oblast 08 skipne prázdný vstup). Žádný LLM call s prázdným kontextem nesmí vést k vymyšlené odpovědi.
- **Tenancy / permissions:** beze změny; prázdno může být i důsledek RLS (uživatel nemá přístup k žádnému relevantnímu chunku) — chování stejné, nesmí prozradit existenci cizích chunků.
- **Reuse / canonical pattern:** „graceful degradation = NIKDY tichá půlka, explicit Partial/Degraded flag" (zákony); `ApiResponse<T>.Ok` s prázdným seznamem.
- **Data dotčena:** žádná. · **Eventy:** žádné.
- **Priorita:** P0

### Edge cases UC-07-04
- **EC-07-04-01 — Výjimka místo prázdna** · Trigger: `Fuse` na prázdných listech hodí (např. `First()` na prázdné) · Očekávané chování: vrátit prázdný `FusionResult`, ne throw; prázdný retrieval je validní stav, ne chyba · Mechanismus: defenzivní `Fuse` (žádné `First`/`Single` na možná-prázdném); zero-retrieval taxonomie · Severity: P0 · Test: unit — `Fuse([],[])` → `Empty==true`, žádná výjimka.
- **EC-07-04-02 — Null list místo prázdného** · Trigger: retriever vrátí `null` (ne `[]`) při chybě/timeoutu · Očekávané chování: `Fuse` ošetří null jako prázdný NEBO odmítne null kontraktem; nesmí NRE · Mechanismus: guard `lists.Select(l => l ?? Empty)` nebo non-null kontrakt + degraded flag z volajícího · Severity: P1 · Test: unit — `Fuse([null, list])` → ošetřeno, lexical-degraded flag (viz UC-07-05).
- **EC-07-04-03 — Prázdno z over-restriktivního scope filtru** · Trigger: user-scope search v korpusu kde uživatel nemá žádné chunky (vše tenant-scope a permission nepustila) · Očekávané chování: 200 prázdno; NESMÍ leaknout že chunky existují u jiných scope/uživatelů (žádný 403 vs 404 distinguisher) · Mechanismus: RLS na retrievalu + uniformní prázdná odpověď; IDOR taxonomie (cizí → neviditelné) · Severity: P0 · Test: integration — user bez přístupu; assert 200 prázdno, ne náznak existence.
- **EC-07-04-04 — Answer-gen halucinuje na prázdnu** · Trigger: prázdný fúzní výstup propadne do LLM bez guardu · Očekávané chování: Oblast 09 MUSÍ detekovat `Empty`/`Degraded` a vrátit „bez podkladu", ne generovat · Mechanismus: citation-missing guard (Oblast 09 odpovědnost), ale fúze MUSÍ flag dodat · Severity: P0 · Test: integration — prázdná fúze → odpověď bez citací + explicit no-context marker.

---

## UC-07-05 — Jen jeden retriever vrátil výsledky (degradace, Partial flag)
- **Actor / role:** system/worker
- **Precondition:** jeden retriever vrátil kandidáty, druhý vrátil prázdno NEBO selhal (provider-down / timeout / výjimka). Např. OpenAI embeddings 429 → dense list nelze získat, lexikální BM25 z lokální DB funguje; nebo naopak.
- **Trigger:** `RrfFusion.Fuse([denseList, EMPTY], opts)` resp. s degraded flagem z `HybridSearchHandler` který zachytil selhání jednoho retrieveru.
- **Main flow:**
  1. `HybridSearchHandler` spustí oba retrievery paralelně (`Task.WhenAll` s per-task try/catch nebo `WhenAll` + výsledkové obálky).
  2. Pokud jeden task selže (provider-down/timeout), handler ho zaloguje (WARN + metrika), NEshodí celý search, a předá fúzi jen úspěšný list + nastaví `Degraded=true` s důvodem (`DenseUnavailable` / `LexicalUnavailable`).
  3. Fúze proběhne nad jedním listem: score chunku = `1/(k+rank)` (jediný příspěvek), union = klíče úspěšného listu.
  4. Výstup nese `Degraded=true` + `DegradedReason`; downstream odpověď to propaguje (`meta.retrieval = "partial:dense_only"`).
- **Postcondition / záruky:** search NEpadá na výpadku jednoho retrieveru (graceful degradation), ALE degradace je EXPLICITNÍ — uživatel/agent ví, že kvalita je snížená. Žádná tichá půlka. Idempotence zachována.
- **Tenancy / permissions:** beze změny.
- **Reuse / canonical pattern:** graceful-degradation + explicit flag (zákony); provider-down handling analog `ClaudeVibeAgentGateway.cs:85` (try + fallback), retry/429 analog v Oblast 04/08; metriky `PlatformMetrics.cs:19` (`platform.rag.fusion.degraded`).
- **Data dotčena:** in-memory. · **Eventy:** žádné. · **Metriky:** `platform.rag.fusion.degraded` counter (tag: reason).
- **Priorita:** P0

### Edge cases UC-07-05
- **EC-07-05-01 — Embedding provider 429 (OpenAI)** · Trigger: dense retrieval selže na rate-limit · Očekávané chování: respektovat `Retry-after` na úrovni gateway (Oblast 04), ale pokud i po retry selže, degradovat na lexical-only + `Degraded=true`, NE 500 celého searche · Mechanismus: per-retriever try/catch v handleru + degraded flag; Cohere/OpenAI 429 taxonomie · Severity: P0 · Test: integration — fake gateway hodí 429; assert 200 partial:lexical_only + degraded flag + metrika.
- **EC-07-05-02 — Tichá degradace bez flagu** · Trigger: handler spolkne selhání a vrátí lexical-only jako by to byl plný hybrid · Očekávané chování: ZAKÁZÁNO — degradace MUSÍ být ve výstupu viditelná · Mechanismus: zákon „NIKDY tichá půlka, explicit Partial/Degraded" · Severity: P0 · Test: integration — assert `meta.retrieval` obsahuje partial marker při single-retriever.
- **EC-07-05-03 — Oba retrievery selžou (ne prázdno, ale chyba)** · Trigger: dense 429 + lexical DB timeout · Očekávané chování: rozlišit od UC-07-04 (legitimní prázdno) — toto je SELHÁNÍ; vrátit 503/degraded-error, ne 200 prázdno (jinak by „nic nenalezeno" maskovalo výpadek) · Mechanismus: handler rozliší „prázdný výsledek" vs „výjimka obou tasků"; throw `BusinessRuleException`/503 mapování · Severity: P0 · Test: integration — oba fake selžou; assert 503/degraded, NE 200 empty.
- **EC-07-05-04 — Degradace mění tie-break škálu** · Trigger: single-list fúze → všechna skóre jsou `1/(k+rank)`, žádné overlapy; pořadí = původní rank listu · Očekávané chování: korektní (fúze jednoho listu = identita ranku); deterministika zachována · Mechanismus: součet jednoho členu · Severity: P3 · Test: unit — single list; assert výstupní pořadí == vstupní rank pořadí.
- **EC-07-05-05 — Timeout jen jednoho retrieveru (pomalý, ne mrtvý)** · Trigger: dense retrieval překročí `Rag:Retrieval:TimeoutMs` · Očekávané chování: per-retriever timeout (CancellationToken s deadline), pak degradace na druhý; nesmí blokovat celý request na nejpomalejším · Mechanismus: `CancellationTokenSource` s timeoutem per task; retrieval timeout taxonomie · Severity: P1 · Test: integration — fake dense spí 5 s, timeout 1 s; assert partial:lexical_only do ~1 s.
- **EC-07-05-06 — Degraded výsledek do reranku** · Trigger: partial fúze (lexical-only) jde do Cohere rerank · Očekávané chování: rerank proběhne normálně nad menší/jednostrannou množinou; degraded flag se propaguje až do finální odpovědi (rerank ho nesmaže) · Mechanismus: flag součástí `FusionResult` → `RerankResult` → answer · Severity: P2 · Test: integration — partial → finální odpověď nese degraded marker.

---

## UC-07-06 — Deterministické pořadí při shodě RRF skóre (tie-break)
- **Actor / role:** system/worker
- **Precondition:** dva nebo více `ChunkId` mají IDENTICKÉ RRF score (běžné: dva chunky každý jen v jednom listu na stejném ranku → `1/(k+r)` shodné; nebo symetrické overlapy).
- **Trigger:** řazení uvnitř `RrfFusion.Fuse`.
- **Main flow:**
  1. Primární klíč řazení: `RrfScore` sestupně.
  2. Při shodě sekundární deterministický tie-break — zvolené pořadí (rozhodnuto níže): **(a)** menší nejlepší rank napříč listy (chunk, který byl v nějakém listu výš, vyhrává), pak **(b)** `ChunkId` (Guid v7, časově uspořádaný) vzestupně jako finální stabilní rozhodčí.
  3. `OrderByDescending(score).ThenBy(bestRank).ThenBy(ChunkId)` — totální uspořádání, žádná závislost na vstupním pořadí slovníku/hash.
- **Postcondition / záruky:** stejný vstup → bitově identické pořadí při každém běhu a na každém uzlu (Worker je multi-instance, competing consumers — ne smí se lišit per instance). Reprodukovatelnost pro testy i pro prompt-cache stabilitu.
- **Tenancy / permissions:** beze změny.
- **Reuse / canonical pattern:** stabilní `OrderBy.ThenBy` s totálním klíčem (analog deterministického paging řazení v read queries). `Guid.CreateVersion7()` jako tie-break = time-ordered, stabilní.
- **Data dotčena:** in-memory. · **Eventy:** žádné.
- **Priorita:** P0

### Edge cases UC-07-06
- **EC-07-06-01 — Tie-break závislý na pořadí Dictionary/hash** · Trigger: implementace spoléhá na pořadí enumerace `Dictionary` (nedeterministické) · Očekávané chování: ZAKÁZÁNO — totální deterministický klíč; `Dictionary` order není garantovaný · Mechanismus: explicitní `ThenBy(ChunkId)`; zákon „deterministika pořadí při shodě" · Severity: P0 · Test: unit — vícenásobné běhy + permutace vstupu; assert identický výstup pořadí.
- **EC-07-06-02 — Float rovnost falešně rozlišuje shodu** · Trigger: dva matematicky shodné score se liší v posledním bitu kvůli pořadí součtu → tie-break se neuplatní, pořadí závisí na float artefaktu · Očekávané chování: buď fixní deterministické pořadí sčítání (stejné pro všechny chunky), NEBO tolerance epsilon při tie detekci, pak tie-break · Mechanismus: pevné pořadí iterace listů (EC-07-01-06) zajistí identický float pro identické struktury; volitelně `Math.Abs(a-b) < eps` · Severity: P1 · Test: unit — dva chunky symetricky v obou listech na zrcadlených ranках; assert stabilní pořadí dané `ChunkId`, ne float.
- **EC-07-06-03 — Tie-break preferuje horší kandidát** · Trigger: zvolený sekundární klíč náhodou upřednostní chunk který byl všude níž · Očekávané chování: tie-break (a) preferuje menší best-rank (lepší jednotlivé umístění) — to je doménově správné; (b) `ChunkId` jen jako finální arbitr · Mechanismus: pořadí `ThenBy(bestRank).ThenBy(ChunkId)` · Severity: P2 · Test: unit — dva chunky stejné RRF, různý best-rank; assert lepší best-rank výš.
- **EC-07-06-04 — Stabilita napříč instancemi Workeru** · Trigger: stejný dotaz zpracován na dvou instancích (competing consumers) · Očekávané chování: identické pořadí (žádná závislost na lokálním stavu/čase) · Mechanismus: čistá funkce + totální klíč; zákon „order-independent handlery" · Severity: P1 · Test: unit — dvě nezávislá volání `Fuse` se stejným vstupem; assert SequenceEqual.

---

## UC-07-07 — Konfigurovatelné `k` a per-retriever váhy (tuning)
- **Actor / role:** tenant-admin (volba parametrů per dotaz, gated) nebo platform default z konfigurace
- **Precondition:** rozhodnutí o `k` (default 60) a volitelných váhách `w_dense`, `w_lexical` (default 1.0/1.0). Vážená RRF: `score = Σ w_list / (k + rank_list)`.
- **Trigger:** `FusionOptions` načtené z `Rag:Fusion:*` (`K`, `TopN`, `Weights:Dense`, `Weights:Lexical`); volitelný per-request override jen pro privilegované volání (tenant-admin / interní tuning endpoint), NIKDY z anonymního vstupu.
- **Main flow:**
  1. Handler načte `FusionOptions` z DI (`IOptions<FusionOptions>`).
  2. Pokud request nese tuning override (a má permission), validuje rozsahy a aplikuje per-call.
  3. `Fuse` použije `w_list / (k + rank)` per list.
  4. Výchozí váhy 1.0 → čisté RRF (UC-07-01).
- **Postcondition / záruky:** parametry validované; default = klasické RRF k=60. Vážení je **deterministické** a auditovatelné (tuning override logován).
- **Tenancy / permissions:** per-request override gated permission (`PlatformPermissions.RagTune` nebo tenant-admin); běžný `user` dostane jen platform/tenant default — nesmí měnit váhy (DoS/manipulace relevance). `k`/váhy NIKDY z LLM toolu (trust boundary).
- **Reuse / canonical pattern:** `IOptions<T>` + validátor (jako `JwtOptionsValidator`); permission gate `.RequirePermission(...)`; per-tenant config přes `Modules:HybridRag:*`.
- **Data dotčena:** žádná (config). · **Eventy:** žádné.
- **Priorita:** P2

### Edge cases UC-07-07
- **EC-07-07-01 — Záporná nebo nulová váha** · Trigger: `Weights:Dense = -1` nebo `0` · Očekávané chování: validátor odmítne záporné; `0` efektivně vypne retriever (povolit jen explicitně, dokumentovaně) → fail-fast mimo rozsah · Mechanismus: options validátor (váha ≥ 0, doporučeně > 0) · Severity: P2 · Test: unit — váha -1 → ValidationException.
- **EC-07-07-02 — Override z neprivilegovaného requestu** · Trigger: běžný `user` pošle `k=1` v body, aby manipuloval pořadí · Očekávané chování: ignorovat / 403 — override jen s permission; jinak platform default · Mechanismus: permission gate; identity/params z tokenu, ne z body · Severity: P1 · Test: integration — user override → použit default, ne body hodnota.
- **EC-07-07-03 — Override z MCP toolu (trust boundary)** · Trigger: MCP klient předá `k`/váhy v argumentu toolu · Očekávané chování: ZAKÁZÁNO — tuning parametry z LLM/MCP argumentu se ignorují; jen server config · Mechanismus: MCP trust-boundary taxonomie (tenant/scope/params NIKDY z argumentu) · Severity: P0 · Test: integration — MCP search tool s `k` argumentem; assert použit server default.
- **EC-07-07-04 — Extrémní `k` (např. 1)** · Trigger: `k=1` → top pozice dominují drtivě, recall přínos slabšího retrieveru mizí · Očekávané chování: povoleno v rozsahu, ale dokumentovaný dopad; validátor jen brání nesmyslům (k ≥ 1), volba je tuningové rozhodnutí · Mechanismus: rozsahový validátor + dokumentace v `Rag:Fusion:K` · Severity: P3 · Test: unit — k=1 vs k=60 dají různé pořadí (sanity), oba validní.
- **EC-07-07-05 — Per-tenant odlišný default** · Trigger: tenant A chce k=40, tenant B k=60 · Očekávané chování: konfigurace per-tenant (až tenancy entitlements, Oblast tenancy); do té doby platform-wide default + per-request gated override · Mechanismus: hranice s NOT-YET tenancy; dnes platform default · Severity: P3 · Test: config — default aplikován; per-tenant parkováno.

---

## UC-07-08 — Fúze N ≥ 2 listů (rozšiřitelnost: + graf-rozšířené kandidáty)
- **Actor / role:** system/worker
- **Precondition:** budoucí/volitelný třetí list — kandidáti z graf-expanze (chunky napojené přes `GraphNode`/`GraphEdge` 1-2 hop, Oblast graf-retrieval). RRF musí fúzovat 2 NEBO 3+ listů beze změny vzorce.
- **Trigger:** `RrfFusion.Fuse(IReadOnlyList<RankedList> lists, opts)` — počet listů je proměnný.
- **Main flow:**
  1. `Fuse` iteruje libovolný počet listů (ne hardcoded dense+lexical).
  2. Union = klíče přes všechny listy; score = Σ přes přítomné listy `w_l/(k+rank_l)`.
  3. Graf list má vlastní rank (graf relevance ordering) a vlastní váhu.
  4. Zbytek (tie-break, top-N) beze změny.
- **Postcondition / záruky:** API fúze je N-list od začátku (žádný 2-list hardcode), takže přidání graf-retrieveru nevyžaduje přepis. Determinismus zachován pro N listů.
- **Tenancy / permissions:** každý list musí být stejně scope-konzistentní (graf kandidáti respektují stejný Tenant|User scope jako vektor/lexikál — RLS na `GraphNode`/`Chunk`).
- **Reuse / canonical pattern:** generický algoritmus (`RrfFusion` přijímá kolekci listů); graf traverz = LINQ join (zákon „graf traverz LINQ join, ne raw Cypher").
- **Data dotčena:** in-memory (kandidáti odkazují `ChunkId`). · **Eventy:** žádné.
- **Priorita:** P2

### Edge cases UC-07-08
- **EC-07-08-01 — Hardcoded dva listy** · Trigger: `Fuse(dense, lexical)` se dvěma parametry místo kolekce · Očekávané chování: API přijímá `IReadOnlyList<RankedList>` → škálovatelné na N; přidání 3. listu = jen další prvek · Mechanismus: generický kontrakt od začátku · Severity: P2 · Test: unit — `Fuse` se 3 listy; assert součet 3 členů pro overlap-all chunk.
- **EC-07-08-02 — Graf list s entitami, ne chunky (mismatch klíče)** · Trigger: graf retriever vrátí `GraphNodeId`, ne `ChunkId` · Očekávané chování: před fúzí MUSÍ být graf kandidáti namapováni na `ChunkId` (přes node→chunk asociaci); fúze pracuje JEN s `ChunkId` jako jednotnou identitou · Mechanismus: mapování v graf-retrieveru PŘED `Fuse`; jednotný klíč · Severity: P1 · Test: unit — graf list mapovaný na ChunkId; assert fúze nesměšuje typy id.
- **EC-07-08-03 — Supernode v graf listu zaplaví kandidáty** · Trigger: graf expanze z „supernode" (tisíce hran) vrátí 5000 chunků · Očekávané chování: graf retriever ořeže na vlastní top-K PŘED fúzí (input cap); fúze nemá filtrovat supernode (to je graf odpovědnost) ale nesmí spadnout na velikosti · Mechanismus: per-list input cap; supernode taxonomie (řešeno v graf oblasti) · Severity: P2 · Test: unit — graf list 5000; fúze OK, downstream top-N drží.
- **EC-07-08-04 — Tři listy, různé škály ranků** · Trigger: dense top-50, lexical top-50, graf top-20 · Očekávané chování: RRF přes rank je škálo-invariantní (proto se používá rank, ne raw score) — různé délky/škály jsou v pohodě · Mechanismus: rank-based RRF (to je celý smysl) · Severity: P3 · Test: unit — různě dlouhé listy; assert korektní union + součty.

---

## UC-07-09 — Telemetrie a observabilita fúze (overlap, contribution, degradace)
- **Actor / role:** system/worker (emise), platform operátor (čtení)
- **Precondition:** každý search projde fúzí; chceme měřit kvalitu/zdraví retrievalu bez logování PII obsahu chunků.
- **Trigger:** uvnitř/po `RrfFusion.Fuse` v `HybridSearchHandler`.
- **Main flow:**
  1. Po fúzi handler emituje metriky: `platform.rag.fusion.candidates` (počet union), `platform.rag.fusion.overlap` (počet chunků v ≥2 listech), `platform.rag.fusion.dense_only` / `lexical_only`, `platform.rag.fusion.degraded` (counter, tag reason), `platform.rag.fusion.latency_ms` (histogram).
  2. Strukturovaný log (DEBUG) per dotaz: počty per list, overlap ratio — BEZ obsahu chunku, BEZ raw query textu (PII/injection risk), jen `ChunkId`+ranky.
  3. Metriky exportovány přes `PlatformMetrics.Meter` (`.AddMeter` v telemetry).
- **Postcondition / záruky:** observabilita bez úniku PII; metriky deterministické; žádný dopad na výsledek fúze.
- **Tenancy / permissions:** metriky agregované, neobsahují obsah; tagy bezpečné (reason, retriever name — ne tenant/user id v high-cardinality).
- **Reuse / canonical pattern:** `PlatformMetrics.cs:19` (Meter, `platform.{area}.{thing}`), `.AddMeter` v `AddPlatformTelemetry`.
- **Data dotčena:** žádná. · **Eventy:** žádné. · **Metriky:** viz výše.
- **Priorita:** P2

### Edge cases UC-07-09
- **EC-07-09-01 — Metrika neexportovaná (druhý Meter)** · Trigger: vytvoření nového `Meter("HybridRag")` bez `.AddMeter` · Očekávané chování: použít `PlatformMetrics.Meter`; jinak metriky tiše zmizí · Mechanismus: zákon „custom metriky off `PlatformMetrics.Meter`" · Severity: P2 · Test: unit/integration — assert instrumenty na sdíleném Meteru.
- **EC-07-09-02 — Log obsahuje obsah chunku / query (PII + injection)** · Trigger: DEBUG log raw `Content` nebo query text · Očekávané chování: ZAKÁZÁNO — chunky mají `[Encrypted][PersonalData]` obsah; logovat jen `ChunkId`/ranky · Mechanismus: PII-at-rest + structured-log minimalizace; zákon `[Encrypted]` · Severity: P1 · Test: review/unit — log payload neobsahuje `Content`.
- **EC-07-09-03 — High-cardinality tag (user/chunk id jako tag)** · Trigger: tag `chunk_id` → exploze časových řad · Očekávané chování: id NEpatří do metrik tagů; jen reason/retriever name · Mechanismus: nízko-kardinalitní tagy · Severity: P2 · Test: review — tag set ohraničený.
- **EC-07-09-04 — Overlap metrika špatně počítá (dedup)** · Trigger: overlap počítán před per-list dedup → nadhodnocen · Očekávané chování: overlap = počet `ChunkId` v ≥2 dedup-listech · Mechanismus: počítat po union stavbě · Severity: P3 · Test: unit — známý overlap; assert metrika == očekávání.

---

## UC-07-10 — Fúzovaný top-N jako kandidátní vstup reranku (předání do Oblast 08)
- **Actor / role:** system/worker
- **Precondition:** fúze vrátila neprázdný (možná degraded) top-N; další krok je Cohere rerank-3.5 nad původním textem chunků.
- **Trigger:** `HybridSearchHandler` po `Fuse` načte `Chunk.Content` pro top-N `ChunkId` (jeden batch read, RLS-scoped) a předá reranku.
- **Main flow:**
  1. Z `FusionResult` se vezme seřazený seznam `ChunkId` (top-N).
  2. Batch-load `Chunk` (read context, `IReadDbContextFactory`) JEN těch id; dešifrování `[Encrypted] Content` model-konvertorem.
  3. Sestaví se rerank kandidáti `(ChunkId, Content)` ve fúzním pořadí (rerank dostane pořadí jako prior, ale skóruje znovu).
  4. Předá Oblast 08; degraded flag propaguje.
- **Postcondition / záruky:** rerank dostává jen autorizované chunky (RLS); fúzní pořadí je tie-break-stabilní (reprodukovatelný vstup reranku → prompt-cache příznivé). Žádná mutace.
- **Tenancy / permissions:** batch-load přes RLS context → cizí `ChunkId` (kdyby se podvrhl) zmizí (404/skip), nikdy neleakne.
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12` (read factory), `[Encrypted]` read konvertor (model-level), batch `Where(c => ids.Contains(c.Id))`.
- **Data dotčena:** `Chunk` (read-only). · **Eventy:** žádné.
- **Priorita:** P1

### Edge cases UC-07-10
- **EC-07-10-01 — Chunk smazaný mezi retrievalem a load (stale index)** · Trigger: dokument soft-deleted po sestavení listů, chunk `IsCurrent=false` nebo soft-deleted · Očekávané chování: batch-load filtruje `IsCurrent && !deleted`; chybějící id se vynechá, výstup se zmenší, NEpadá; degraded ne nutně, ale konzistentní · Mechanismus: soft-delete filtr + `IsCurrent`; stale-index taxonomie · Severity: P1 · Test: integration — soft-delete chunk po fúzi; assert vynechán z reranku, žádná chyba.
- **EC-07-10-02 — Podvržené cizí ChunkId v listu (IDOR)** · Trigger: list (hypoteticky) obsahuje cizí `ChunkId` · Očekávané chování: RLS read context ho nevrátí → tiše vypadne; nikdy se nedostane do reranku/odpovědi · Mechanismus: RLS na `Chunk` (`IUserOwned`/`ITenantScoped`); IDOR → neviditelné · Severity: P0 · Test: integration — cizí id v simulovaném listu; assert nepřítomen ve výstupu.
- **EC-07-10-03 — Batch read N+1** · Trigger: load chunků po jednom v cyklu · Očekávané chování: jeden `Where(Contains)` batch dotaz, ne N dotazů · Mechanismus: EF batch (zákon EF/LINQ, výkon) · Severity: P2 · Test: integration — assert 1 SQL roundtrip pro top-N.
- **EC-07-10-04 — Dešifrování PII selže pro některý chunk (shredded DEK)** · Trigger: subjekt erased (GDPR), DEK shredded → `Content` = `[erased]` · Očekávané chování: chunk buď vyloučen z reranku (prázdný obsah nemá smysl rerankovat), nebo předán jako `[erased]` — preferováno vyloučení + log; nesmí throw · Mechanismus: `[Encrypted]` čtení vrací `[erased]`; GDPR erase taxonomie · Severity: P1 · Test: integration — erased subjekt chunk; assert vyloučen, žádná výjimka.
- **EC-07-10-05 — Pořadí předané reranku se liší od fúze** · Trigger: batch-load `Where(Contains)` vrátí v DB pořadí, ne fúzním · Očekávané chování: po loadu re-seřadit podle fúzního pořadí (`ChunkId` → index z `FusionResult`) PŘED předáním · Mechanismus: reorder podle fúzního indexu (DB pořadí není garantované) · Severity: P1 · Test: integration — assert rerank kandidáti v RRF pořadí.

---

## UC-07-11 — Prompt-cache stabilita fúzního výstupu (deterministický prefix)
- **Actor / role:** system/worker (downstream Oblast 09 answer-gen s Anthropic prompt caching)
- **Precondition:** answer-gen sestavuje LLM prompt s top-N chunky jako kontext; používá Anthropic prompt caching (statický prefix první, volatilní za breakpointem).
- **Trigger:** sestavení promptu z `FusionResult` (po rerankу) — fúze je upstream, její determinismus podmiňuje cache hit.
- **Main flow:**
  1. Fúze produkuje deterministické pořadí (UC-07-06) → identický dotaz nad neměnným korpusem dá identický top-N v identickém pořadí.
  2. Answer-gen vloží chunky (stabilní pořadí) do STATICKÉ části promptu (cache-able prefix), volatilní data (timestamp, user query echo, request id) AŽ za cache breakpoint.
  3. Cache hit při opakovaném/podobném dotazu nad stejným korpusem.
- **Postcondition / záruky:** fúze nesmí vnášet nedeterminismus (žádný `DateTime.Now`, žádný náhodný tie-break, žádné pořadí závislé na hash) — jinak se cache prefix mění a cache je k ničemu.
- **Tenancy / permissions:** beze změny; cache je per-prompt-prefix, obsah scope-izolovaný (různí uživatelé/tenanti mají různé chunky → různé prefixy, žádné křížení).
- **Reuse / canonical pattern:** Anthropic prompt cache pravidlo (zákon „static prefix první, volatilní za breakpointem"); determinismus z UC-07-06; `IClock.UtcNow` jen ve volatilní části.
- **Data dotčena:** žádná (fúze). · **Eventy:** žádné.
- **Priorita:** P2

### Edge cases UC-07-11
- **EC-07-11-01 — Timesamp/nondeterminismus uprostřed prefixu** · Trigger: do statické (chunk) části se dostane timestamp nebo request id · Očekávané chování: ZAKÁZÁNO — volatilní data za breakpoint; jinak cache nikdy nehitne · Mechanismus: zákon prompt-cache; prefix = jen chunky v deterministickém pořadí · Severity: P1 · Test: unit — dva běhy stejného dotazu; assert identický prefix string (byte-equal).
- **EC-07-11-02 — Nedeterministický tie-break láme cache** · Trigger: fúze vrátí jiné pořadí při shodě skóre (hash-order) · Očekávané chování: deterministický tie-break (UC-07-06) → stabilní prefix · Mechanismus: totální řadicí klíč · Severity: P1 · Test: unit — opakované `Fuse` se shodami; assert identické pořadí → identický prefix.
- **EC-07-11-03 — Korpus se změnil mezi dotazy (cache musí minout správně)** · Trigger: nový chunk ingestován → top-N se legitimně liší · Očekávané chování: prefix se LEGITIMNĚ změní (jiný obsah) → cache miss je správný, ne bug; determinismus platí jen pro NEMĚNNÝ korpus · Mechanismus: cache je obsahová; změna obsahu = jiný prefix · Severity: P3 · Test: integration — ingest mezi dotazy; assert odpověď reflektuje nový chunk (cache neslepí starý).
- **EC-07-11-04 — Per-user/tenant prefix kolize** · Trigger: dva uživatelé mají náhodou stejné `ChunkId` pořadí · Očekávané chování: chunky jsou scope-izolované; i kdyby textově shodné, cache klíč zahrnuje plný prefix obsah — žádný leak mezi scope (cache nese jen text, ne přístupová práva, a retrieval už scope vyřešil) · Mechanismus: RLS na retrievalu + obsahový cache klíč · Severity: P2 · Test: integration — dva tenanti; assert žádné křížení kontextu.

---

## UC-07-12 — Validace a guardy vstupu fúze (kontrakt RankedList)
- **Actor / role:** system/worker
- **Precondition:** `RrfFusion.Fuse` je volána z různých míst (hybrid search, budoucí N-list, testy); vstupní kontrakt musí být robustní.
- **Trigger:** vstup do `Fuse`.
- **Main flow:**
  1. `Fuse` validuje: listy ≠ null (nebo ošetří jako prázdné + degraded), `opts` ≠ null (default), `k ≥ 1`, `TopN ≥ 1`.
  2. Per-list: ranky kladné, `ChunkId ≠ Guid.Empty`.
  3. Při porušení tvrdého invariantu (např. `Guid.Empty` jako id) → odmítnout (programátorská chyba), ne tiše pokračovat.
- **Postcondition / záruky:** fúze nikdy neprodukuje nesmyslný výstup z poškozeného vstupu; chyby jsou hlasité (test-time), degradace jen pro očekávané runtime stavy (prázdno, single-list).
- **Tenancy / permissions:** N/A (čistá funkce).
- **Reuse / canonical pattern:** guard clauses; `FusionOptions` validátor (jako `JwtOptionsValidator`).
- **Data dotčena:** žádná. · **Eventy:** žádné.
- **Priorita:** P2

### Edge cases UC-07-12
- **EC-07-12-01 — `Guid.Empty` ChunkId** · Trigger: list nese prázdný Guid · Očekávané chování: odmítnout / vyloučit (nikdy validní chunk); programátorská chyba upstream · Mechanismus: guard; `Guid.CreateVersion7` nikdy nedá Empty · Severity: P3 · Test: unit — Empty id → vyloučen/throw.
- **EC-07-12-02 — Rank 0 nebo záporný** · Trigger: list s rankem 0 (0-based omylem) · Očekávané chování: kontrakt je 1-based; `1/(k+0)` nezpůsobí dělení nulou (k≥1), ale 0-based zkresluje váhu vs jiný list → normalizovat na 1-based v retrieveru · Mechanismus: kontrakt 1-based + dokumentace · Severity: P2 · Test: unit — rank 0 vs 1 dá jinou váhu; assert retrievery dodávají 1-based.
- **EC-07-12-03 — Null `opts`** · Trigger: volání bez options · Očekávané chování: default `FusionOptions` (k=60, TopN=50, váhy 1.0) · Mechanismus: `opts ??= FusionOptions.Default` · Severity: P3 · Test: unit — `Fuse(lists)` bez opts → defaulty.
- **EC-07-12-04 — Obrovský vstup (DoS)** · Trigger: zlomyslně velké listy (miliony) přes manipulovaný retrieval param · Očekávané chování: per-list input cap (retrieval `Limit` max, např. ≤ 200) vynucen už v retrievalu; fúze chráněna upstream limitem + rate-limit na search endpointu · Mechanismus: retrieval `Limit` validátor + endpoint rate-limit (DoS taxonomie) · Severity: P1 · Test: integration — request s `limit=10_000_000` → odmítnut validátorem na search vstupu.
