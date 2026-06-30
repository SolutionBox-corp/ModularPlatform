# Oblast 09 — Freshness & versioning
Tato oblast pokrývá časovou relevanci výsledků (time-decay až PO reranku), verzování dokumentů přes `IsCurrent` (HARD filtr superseded chunků), re-ingest jako supersede, rozlišení changelog vs. neplatná data, vyvážení relevance vs. čerstvost a chování pro dokumenty bez data. Mapuje na **build fázi 9** (po dokončení retrieval pipeline z oblastí 05–08: ANN → BM25 → RRF → rerank). Freshness je poslední re-scoring vrstva, NIKDY ne uvnitř ANN (rozbila by geometrii vektorového prostoru).

---

## UC-09-01 — Time-decay re-scoring kandidátů AŽ PO reranku
- **Actor / role:** system/worker (součást query handleru, transparentní pro volajícího user|tenant-admin)
- **Precondition:** Retrieval pipeline vrátila rerankovaný seznam kandidátů (`List<RerankedCandidate>` s `RerankScore` z Cohere rerank-3.5); každý kandidát nese `Chunk.CreatedAt` (a odvozené `DocumentEffectiveDate`). Kolekce má v konfiguraci `Rag:Freshness:HalfLifeDays` a `Rag:Freshness:DecayWeight`.
- **Trigger:** in-process volání `ApplyFreshnessDecayAsync` ze `SearchKnowledgeQueryHandler` (query, NE command — pouze čte).
- **Main flow:**
  1. Query handler dokončí rerank → má `topN` kandidátů s `RerankScore ∈ [0,1]`.
  2. Pro každého kandidáta se v C# (NE v DB, NE v LINQ to SQL) spočítá `ageDays = (clock.UtcNow - candidate.EffectiveDate).TotalDays` s `IClock.UtcNow`.
  3. `decayFactor = Math.Pow(0.5, ageDays / halfLifeDays)` (exponenciální půlrozpad; `decayFactor ∈ (0,1]`).
  4. `finalScore = (1 - decayWeight) * rerankScore + decayWeight * (rerankScore * decayFactor)` — decay moduluje rerank skóre, NEpřepisuje ho; při `decayWeight=0` je freshness vypnutá a `finalScore == rerankScore`.
  5. Re-sort sestupně podle `finalScore`, ořez na `topK`.
  6. Vrátí se výsledek + per-kandidát `FreshnessDebug { EffectiveDate, AgeDays, DecayFactor, RerankScore, FinalScore }` (pouze v debug/preview módu, jinak skryté).
- **Postcondition / záruky:** Žádná mutace DB, žádný event (Law: queries jen čtou). Pořadí výsledků reflektuje relevanci i čerstvost. Deterministické pro fixní `UtcNow` (testovatelné injektovaným `IClock`).
- **Tenancy / permissions:** Scope dle volajícího (User → tenant korpus + privátní; tenant search → permission `rag.search.tenant`). RLS zajišťuje, že kandidáti už NEobsahují cizí řádky (filtr proběhl v retrievalu). Freshness vrstva nečte další data.
- **Reuse / canonical pattern:** Read query shell `GetProfileHandler.cs:12` (IReadDbContextFactory, žádná transakce); čas výhradně přes `IClock.UtcNow` jako v `StartDemoOperationHandler.cs:17`.
- **Data dotčená:** čte `chunks` (`CreatedAt`, `IsCurrent`), `documents` (effective date) — read-only · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-09-01
- **EC-09-01-01 — Decay aplikován UVNITŘ ANN místo po reranku** · Trigger: implementace omylem násobí embedding distance decay faktorem před kNN · Očekávané chování: ZAKÁZÁNO — decay smí vstoupit až po reranku, jinak deformuje geometrii vektorového prostoru a vrací nesmyslné sousedy · Mechanismus: pipeline pořadí ANN→BM25→RRF→rerank→**decay** je fixní (zákon „time-decay AŽ PO reranku"); architektonický test ověří, že `ApplyFreshnessDecay` se volá až na výstupu rerankeru · Severity: P0 · Test: unit — decay funkce nikdy nedostane raw `CosineDistance`, jen `RerankScore`; ArchUnitNET/structural test na pořadí volání
- **EC-09-01-02 — Budoucí datum dokumentu (EffectiveDate > UtcNow)** · Trigger: dokument s datem v budoucnosti (špatná metadata / time-zone bug) · Očekávané chování: `ageDays < 0` → `decayFactor > 1` by uměle nadlehčilo skóre; clampnout `ageDays = Math.Max(0, ageDays)` → `decayFactor = 1` · Mechanismus: explicitní clamp v decay funkci; log WARN `rag.freshness.future_dated` + metrika · Severity: P1 · Test: unit — dokument s `EffectiveDate = UtcNow + 30d` → `decayFactor == 1.0`
- **EC-09-01-03 — Dokument bez data (EffectiveDate == null)** · Trigger: chunk z dokumentu, kde nešlo odvodit datum · Očekávané chování: fallback dle `Rag:Freshness:UndatedPolicy` (`UseIngestDate` | `NeutralNoDecay`); default `UseIngestDate` → použít `Chunk.CreatedAt` (datum ingestu); `NeutralNoDecay` → `decayFactor = 1` · Mechanismus: viz UC-09-05; nikdy `NullReferenceException` · Severity: P1 · Test: unit — null date + `NeutralNoDecay` → finalScore == rerankScore
- **EC-09-01-04 — HalfLifeDays = 0 nebo záporné** · Trigger: chybná konfigurace · Očekávané chování: fail-fast při startu přes options validator (`HalfLifeDays > 0`), mimo Development výjimka; v Dev WARN + fallback na default (90 dní) · Mechanismus: `RagFreshnessOptionsValidator` (copy `JwtOptionsValidator`), nikdy dělení nulou v `ageDays / halfLifeDays` · Severity: P0 · Test: unit — validator odmítne `HalfLifeDays=0`; integ — boot s nulou selže mimo Dev
- **EC-09-01-05 — DecayWeight mimo [0,1]** · Trigger: konfigurace `DecayWeight=1.5` nebo `-0.2` · Očekávané chování: validator clampuje/odmítá; `DecayWeight ∈ [0,1]` jinak `finalScore` může převýšit 1 nebo jít záporný a rozbít řazení · Mechanismus: options validator `Range(0,1)` · Severity: P1 · Test: unit — `DecayWeight=2` odmítnuto validatorem
- **EC-09-01-06 — Velmi starý dokument → decayFactor podtekl na 0** · Trigger: dokument starý např. 50× half-life · Očekávané chování: `decayFactor → ~0` ale NIKDY záporný; relevantní starý dokument se NESMÍ úplně ztratit, pokud byl jediný kandidát (zero-retrieval guard navazuje) · Mechanismus: `Math.Pow` vrací kladné číslo; pokud po decay zůstanou všechny skóre ~0, vrať je seřazené dle původního `RerankScore` jako Partial s `Degraded=true` flagem (graceful degradation, NIKDY tichá prázdná půlka) · Severity: P1 · Test: unit — dokument 5000 dní starý, half-life 30 → `decayFactor > 0`; integ — jediný starý kandidát se vrátí, ne prázdný výsledek
- **EC-09-01-07 — Časová zóna / non-UTC datum dokumentu** · Trigger: `EffectiveDate` parsovaný z dokumentu v lokální TZ · Očekávané chování: vše normalizováno na UTC `DateTimeOffset` při ingestu; decay počítá v UTC · Mechanismus: zákon „vše UTC (`IClock.UtcNow`)"; ingest pipeline ukládá `EffectiveDate` jako UTC · Severity: P2 · Test: unit — datum s offsetem +14:00 a -12:00 dají stejný `ageDays` po normalizaci
- **EC-09-01-08 — Decay mění pořadí jen mezi vyrovnanými kandidáty (no-op u dominantní relevance)** · Trigger: jeden kandidát má rerankScore 0.95, ostatní 0.3 · Očekávané chování: i s decay zůstane dominantní nahoře, pokud není extrémně starý — `decayWeight` kontroluje sílu; ověřit, že čerstvý šum nepřebije silně relevantní starý fakt · Mechanismus: konvexní kombinace `(1-w)*rerank + w*(rerank*decay)` zaručuje monotonii v rerankScore při fixním decay · Severity: P2 · Test: unit — property: při fixním decay je finalScore monotónní v rerankScore
- **EC-09-01-09 — NaN / Infinity v decay výpočtu** · Trigger: `ageDays` přeteče (extrémně velký), nebo `halfLifeDays` extrémně malé · Očekávané chování: žádný NaN nesmí proniknout do řazení (NaN rozbije `Sort`); guard `double.IsFinite(finalScore)` → fallback rerankScore · Mechanismus: defensivní kontrola po výpočtu · Severity: P1 · Test: unit — `halfLifeDays=1e-300` nezpůsobí NaN v řazení

---

## UC-09-02 — Re-ingest dokumentu = supersede (staré chunky `IsCurrent=false`)
- **Actor / role:** user | tenant-admin (vlastník dokumentu) nebo system/worker (automatický re-ingest při změně zdroje)
- **Precondition:** Dokument `D` už existuje a má indexované chunky s `IsCurrent=true`. Uživatel nahraje novou verzi se STEJNÝM logickým identifikátorem (stejný `DocumentId` při re-uploadu, nebo `SourceKey`/dedup-hash match).
- **Trigger:** HTTP `POST /v1/rag/collections/{collectionId}/documents/{documentId}/reingest` (multipart) → `ReingestDocumentCommand` → dispatcher → handler → `IngestSaga`.
- **Main flow:**
  1. Endpoint mapuje request → `ReingestDocumentCommand { DocumentId, Bytes, FileName, ContentType, IdempotencyKey }`; identita z tokenu (`ITenantContext.UserId`), NE z body.
  2. Handler ověří vlastnictví dokumentu (RLS → cizí id = 404), zvedne `Document.Version`, uloží nové bytes přes `IFileStorage.PutAsync` (server-generated key).
  3. Spustí se durable `IngestSaga`: extrakce → chunking → embedding → graf. NOVÉ chunky se zapisují s `IsCurrent=true`, `Version=N+1`.
  4. **Atomicky v jedné transakci na konci pipeline** (commit přes `SaveChangesAndFlushMessagesAsync`): staré chunky dokumentu `IsCurrent=false` (atomický `ExecuteUpdate` `WHERE DocumentId=@id AND IsCurrent=true AND Version < @newVersion`) + nové chunky `IsCurrent=true`.
  5. Publikuje `DocumentReindexedIntegrationEvent { DocumentId, OldVersion, NewVersion }` přes outbox.
  6. Vrátí **202** + `Location: /v1/rag/operations/{ingestOperationId}` (dlouhá práce).
- **Postcondition / záruky:** Po commitu má dokument právě jednu sadu `IsCurrent=true` chunků (nová verze). Staré chunky zůstávají fyzicky v DB s `IsCurrent=false` (verzovací historie/audit), NEjsou smazané. Idempotence: stejný `IdempotencyKey` re-ingest nevytvoří duplicitní verzi (UNIQUE → catch `DbUpdateException` → vrátí existující operaci).
- **Tenancy / permissions:** Scope dle dokumentu (Tenant|User); permission `rag.document.write`; RLS na `documents`+`chunks` (`IUserOwned`/`ITenantScoped`).
- **Reuse / canonical pattern:** 202+saga jako `StartDemoOperationHandler.cs:17` + `CreditPurchaseSaga.cs:30` (terminal-state guard, idempotentní); atomický supersede flip = `ExecuteUpdate` guard vzor z Billing debit (CLAUDE.md §4 money debit); outbox publish `RegisterUserHandler.cs:22`; blob put `UploadFileHandler.cs:21`.
- **Data dotčená:** `documents` (Version, StorageKey, Status), `chunks` (IsCurrent flip + nové řádky), `graph_nodes`/`graph_edges` (re-derive — viz EC) · **Eventy:** `DocumentReindexedIntegrationEvent`
- **Priorita:** P0

### Edge cases UC-09-02
- **EC-09-02-01 — Crash sagy mezi vytvořením nových chunků a flipnutím starých** · Trigger: worker spadne po insertu nové verze, před `IsCurrent=false` flipnutím · Očekávané chování: po resume saga NESMÍ nechat DVĚ `IsCurrent=true` verze; flip + insert musí být v jedné DB transakci (commit = `SaveChangesAndFlushMessagesAsync`), nebo saga při resume detekuje rozpracovaný stav a dokončí flip idempotentně · Mechanismus: saga terminal-state guard (`CreditPurchaseSaga.cs:30`); supersede je jeden atomický commit · Severity: P0 · Test: integ — kill workeru mid-saga, resume → právě jedna current verze; assert `chunks.Count(IsCurrent) == newChunkCount`
- **EC-09-02-02 — Duplicate re-ingest (idempotency)** · Trigger: dvojí submit stejného re-ingestu (retry, double-click) se stejným `IdempotencyKey` · Očekávané chování: druhý běh nevytvoří novou verzi; vrátí existující `operationId`/verzi · Mechanismus: UNIQUE `ingest_operations.idempotency_key` + catch `DbUpdateException` (RAG edge-case taxonomie: duplicate ingest) · Severity: P0 · Test: integ — 2× reingest stejný key → `Document.Version` se zvedne jen o 1
- **EC-09-02-03 — Concurrent re-ingest dvou různých verzí téhož dokumentu** · Trigger: dva uživatelé/joby re-ingestují `D` současně · Očekávané chování: serializace — vyhraje vyšší `Version`; flip guard `WHERE Version < @newVersion` zabrání tomu, aby starší běh shodil novější current verzi · Mechanismus: atomický `ExecuteUpdate` s verzní podmínkou + xmin na `documents` (`ConcurrencyRetryBehavior`); order-independent (parallel competing consumers) · Severity: P0 · Test: integ — 2 souběžné reingesty, finální current = nejvyšší verze, žádný „lost update"
- **EC-09-02-04 — Re-ingest IDENTICKÉHO obsahu (žádná změna)** · Trigger: nový upload bytově shodný se současnou verzí (content-hash match) · Očekávané chování: dle politiky `Rag:Freshness:SkipUnchangedReingest` — default přeskočit (žádná nová verze, žádný re-embed → úspora providerských tokenů), vrátit 200 s informací „unchanged"; nebo (config off) povýšit verzi a obnovit `EffectiveDate` (re-touch) · Mechanismus: content-hash porovnání před spuštěním embeddingu; rozhodnutí logováno · Severity: P1 · Test: integ — identický re-ingest → žádné nové chunky, žádné volání embed gateway (fake gateway counter == 0)
- **EC-09-02-05 — Graf nody/hrany ze staré verze po supersede** · Trigger: stará verze přispěla `GraphNode`/`GraphEdge`, nová verze je nemá · Očekávané chování: graf se re-derivuje z aktuálních chunků; hrany odvozené výhradně ze superseded chunků se musí oslabit/odebrat (nebo přepočítat `Weight`), aby graf neukazoval na neaktuální fakta; supernode/orphan node nesmí zůstat viset · Mechanismus: re-ingest saga přepočítá příspěvek dokumentu do grafu (entity merge/un-merge); viz oblast grafu · Severity: P1 · Test: integ — fakt v v1, odstraněn v v2 → hrana z toho faktu po reingestu pryč/oslabená
- **EC-09-02-06 — Retrieval během běžícího re-ingestu** · Trigger: query přijde, když saga zatím vytvořila nové chunky, ale ještě neflipla staré · Očekávané chování: nikdy nesmí vrátit MIX dvou verzí; dokud commit neproběhl, retrieval vidí jen starou (current) verzi; po commitu jen novou · Mechanismus: nové chunky mají `IsCurrent=false` až do finálního atomického flipu → retrieval HARD filtr `IsCurrent=true` (UC-09-03) vidí konzistentní snapshot · Severity: P0 · Test: integ — query mezi fázemi vrací výhradně jednu verzi, nikdy duplikáty stejného textu z obou verzí
- **EC-09-02-07 — Re-ingest dokumentu, který je soft-deleted** · Trigger: re-ingest na `Document.IsDeleted=true` · Očekávané chování: buď 404/409 (`document.deleted`), nebo (dle politiky) „undelete + new version"; nikdy tiše neobnovit smazaný dokument do retrievalu bez explicitního undelete · Mechanismus: `ISoftDeletable` filtr; explicitní business rule · Severity: P1 · Test: integ — reingest smazaného → 409
- **EC-09-02-08 — Oversized / unsupported nová verze** · Trigger: nová verze překračuje size cap nebo má nepovolený MIME · Očekávané chování: re-ingest ODMÍTNUT validatorem PŘED supersede; STARÁ verze zůstává `IsCurrent=true` a plně funkční (žádná částečná degradace) · Mechanismus: `ReingestDocumentValidator` (allowlist + size cap, copy `UploadFileValidator`); supersede flip se nikdy nespustí při selhání validace · Severity: P0 · Test: integ — reingest 50 MB → 400, stará verze stále vyhledatelná
- **EC-09-02-09 — Embedding dimension / model drift mezi verzemi** · Trigger: re-ingest po změně embed modelu (3072-dim → jiný) · Očekávané chování: nové chunky mají nesené `EmbeddingModel`/`Dimension`; míchat různě-dimenzionální vektory v jedné ANN dotaz je nelegální → buď re-embed celé kolekce, nebo verzování modelu; viz globální politika dimension drift · Mechanismus: `Chunk.EmbeddingModel` sloupec + guard, že kolekce má homogenní dimenzi · Severity: P1 · Test: integ — reingest s jiným modelem bez migrace kolekce → odmítnuto s `rag.embedding.model_mismatch`
- **EC-09-02-10 — PII v nové verzi chunků** · Trigger: nová verze obsahuje osobní data · Očekávané chování: `Chunk.Content` je `[Encrypted][PersonalData]` → šifruje se at rest pod DEK subjektu i pro novou verzi · Mechanismus: `PersonalDataEncryptionInterceptor` (CLAUDE.md §4 PII at rest) · Severity: P0 · Test: integ — raw DB čtení nové verze chunku → ciphertext `penc:v2`
- **EC-09-02-11 — Audit bypass při supersede flipu** · Trigger: `IsCurrent` flip přes `ExecuteUpdate` obejde `AuditInterceptor` + xmin · Očekávané chování: vědomé rozhodnutí — flip je masová atomická operace; samotná verzovací historie (řádky chunků s `Version`) JE auditní stopa, takže bypass auditu na flagu je přijatelný; `DocumentReindexedIntegrationEvent` slouží jako observable záznam · Mechanismus: dokumentovaný caveat (CLAUDE.md §4 „ExecuteUpdate bypasses interceptor") + event místo audit řádku · Severity: P2 · Test: integ — reingest vyprodukuje `DocumentReindexedIntegrationEvent`; verze dohledatelné

---

## UC-09-03 — `IsCurrent` HARD filtr v retrievalu (superseded verze NIKDY nevyhraje)
- **Actor / role:** system/worker (retrieval pipeline), transparentní pro user|tenant-admin
- **Precondition:** Kolekce obsahuje dokumenty s více verzemi chunků (`IsCurrent` true/false). Time-decay (UC-09-01) je aktivní.
- **Trigger:** in-process retrieval (ANN + BM25 stage) ve `SearchKnowledgeQueryHandler`.
- **Main flow:**
  1. Vektorový kNN i BM25 stage aplikují v LINQ `WHERE c.IsCurrent == true` jako součást základního predikátu PŘED scoringem — `IsCurrent=false` chunky se NIKDY nedostanou ani do kandidátní množiny.
  2. RRF, rerank i time-decay pracují už jen s current chunky.
  3. Filtr je HARD (boolean), NE měkký (time-decay) — superseded verze nesmí „vyhrát" ani kdyby byla extrémně relevantní.
- **Postcondition / záruky:** Žádná odpověď nikdy necituje superseded chunk. Read-only.
- **Tenancy / permissions:** Standardní RLS + Scope filtry; `IsCurrent` je ortogonální dodatečný predikát.
- **Reuse / canonical pattern:** LINQ predikát v read query (`GetProfileHandler.cs:12`); pravidlo „graceful degradation, NIKDY tichá půlka" pro prázdný výsledek po filtru.
- **Data dotčená:** `chunks` (read, `IsCurrent`) · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-09-03
- **EC-09-03-01 — IsCurrent řešen time-decayem místo HARD filtru** · Trigger: implementace omylem dá superseded chunkům jen nízký decay místo úplného vyloučení · Očekávané chování: ZAKÁZÁNO — smazaná/nahrazená verze nese NEPLATNÁ data a nesmí nikdy proniknout, ani s minimálním skóre; musí být HARD `WHERE IsCurrent` · Mechanismus: zákon „IsCurrent HARD filtr, ne time-decay"; structural test, že `IsCurrent` je v `WHERE`, ne ve scoring funkci · Severity: P0 · Test: integ — silně relevantní superseded chunk + irelevantní current chunk → vrátí JEN current, superseded vůbec ne
- **EC-09-03-02 — Stale index po delete (orphan current chunky)** · Trigger: dokument smazán, ale chunky zůstaly `IsCurrent=true` · Očekávané chování: delete dokumentu musí současně shodit chunky (`IsCurrent=false` nebo soft-delete) ve stejné transakci; retrieval nikdy nevrátí chunk smazaného dokumentu · Mechanismus: delete handler atomicky + `ISoftDeletable` filtr (RAG taxonomie: stale index po delete) · Severity: P0 · Test: integ — smaž dokument, okamžitě query → 0 jeho chunků
- **EC-09-03-03 — Žádný current chunk (vše superseded, nová verze ještě nehotová)** · Trigger: re-ingest běží, stará verze už shozena, nová ještě necommitnuta (teoreticky, ač UC-09-02 to atomicitou brání) · Očekávané chování: zero-retrieval fallback → explicitní `Partial`/`Degraded` flag + prázdný-ale-honest výsledek, NE tichá nula bez vysvětlení · Mechanismus: graceful degradation zákon; flip a insert jsou ale jedna transakce, takže tento stav je krátkodobý/nedosažitelný v praxi · Severity: P1 · Test: integ — umělý stav „0 current" → odpověď nese `degraded: index_rebuilding`
- **EC-09-03-04 — IsCurrent filtr zapomenut v BM25 stage, přítomen v ANN** · Trigger: nekonzistentní aplikace filtru mezi dvěma retrieval cestami · Očekávané chování: OBA stage (vektor i lexikální) musí filtrovat `IsCurrent`; jinak BM25 propašuje superseded chunk do RRF · Mechanismus: sdílený základní `IQueryable<Chunk>` predikát (`CurrentChunks(scope)`) použitý v obou stage — DRY, jeden zdroj pravdy · Severity: P0 · Test: integ — superseded chunk s přesnou keyword shodou se NEobjeví přes BM25 cestu
- **EC-09-03-05 — Point-in-time dotaz potřebuje historickou verzi** · Trigger: as-of query (UC-09-11) chce verzi platnou k datu T · Očekávané chování: HARD `IsCurrent` filtr se NEAPLIKUJE u explicitního temporal dotazu; místo toho `WHERE Version platná v T` — current filtr je default, ne absolutní · Mechanismus: parametrizovaný retrieval (default = current; as-of = verzní okno) · Severity: P2 · Test: integ — as-of T vrátí verzi platnou v T, ne nejnovější
- **EC-09-03-06 — Index/výkon HARD filtru** · Trigger: velká kolekce, `IsCurrent=false` řádky tvoří většinu (mnoho verzí) · Očekávané chování: partial index na `(collection_id, is_current) WHERE is_current` aby ANN/BM25 nečetly mrtvé verze · Mechanismus: EF migrace s filtered indexem (HasFilter) · Severity: P2 · Test: integ/perf — query plan používá partial index, ne seq scan přes superseded

---

## UC-09-04 — Konfigurace freshness (half-life, decay weight) per kolekce / tenant
- **Actor / role:** tenant-admin
- **Precondition:** Existuje kolekce; admin chce naladit, jak silně čerstvost ovlivňuje řazení (např. právní knowledge base = slabý decay; novinky/changelog = silný decay).
- **Trigger:** HTTP `PUT /v1/rag/collections/{collectionId}/freshness` → `UpdateFreshnessSettingsCommand` → handler.
- **Main flow:**
  1. Request `{ HalfLifeDays, DecayWeight, UndatedPolicy }` → validátor.
  2. Handler načte kolekci (RLS), aktualizuje sloupce `FreshnessHalfLifeDays`, `FreshnessDecayWeight`, `UndatedPolicy` na entitě `KnowledgeCollection`.
  3. Commit (tracked entity → xmin + `ConcurrencyRetryBehavior`); publikuje `CollectionFreshnessUpdatedIntegrationEvent` (volitelně, pro cache invalidaci).
  4. 200 + nová konfigurace.
- **Postcondition / záruky:** Nová nastavení platí pro NÁSLEDNÉ dotazy (retrieval čte konfiguraci kolekce při každém query). Idempotentní (stejné hodnoty → no-op update).
- **Tenancy / permissions:** Scope Tenant; permission `rag.collection.manage`; RLS na `knowledge_collections`.
- **Reuse / canonical pattern:** Write command `RegisterUserHandler.cs:22` (tracked update + outbox); options validátor pattern `JwtOptionsValidator`; per-resource konfigurace jako Billing subscription plans.
- **Data dotčená:** `knowledge_collections` (Freshness* sloupce) · **Eventy:** `CollectionFreshnessUpdatedIntegrationEvent` (volitelný)
- **Priorita:** P1

### Edge cases UC-09-04
- **EC-09-04-01 — HalfLifeDays/DecayWeight mimo rozsah** · Trigger: `HalfLifeDays=-5`, `DecayWeight=3` · Očekávané chování: `UpdateFreshnessSettingsValidator` odmítne (`HalfLifeDays > 0`, `DecayWeight ∈ [0,1]`) → 400 `rag.freshness.invalid_range` · Mechanismus: FluentValidation `.WithErrorCode` + errorCode do `SharedResource.resx`+`.cs.resx` · Severity: P0 · Test: integ — invalid hodnoty → 400, žádná mutace
- **EC-09-04-02 — Změna nastavení během retrievalu (cache koherence)** · Trigger: admin změní decay, souběžně běží dotazy · Očekávané chování: in-flight dotaz použije snapshot nastavení z času načtení kolekce; nový dotaz použije nová nastavení; žádný retrieval nesmí zkombinovat půl starých/půl nových parametrů · Mechanismus: nastavení se načtou jednou na začátku query a předají decay funkci jako immutable param · Severity: P2 · Test: integ — concurrent update + query → query je interně konzistentní
- **EC-09-04-03 — Cizí kolekce (IDOR)** · Trigger: admin tenant A volá PUT na collectionId tenant B · Očekávané chování: RLS → 404 (ne 403, neodhalit existenci) · Mechanismus: RLS na `knowledge_collections` + `ITenantContext` (CLAUDE.md Law 10) · Severity: P0 · Test: integ — cross-tenant PUT → 404
- **EC-09-04-04 — User-scoped kolekce vs tenant-level freshness override** · Trigger: privátní user kolekce — kdo smí ladit freshness? · Očekávané chování: vlastník user kolekce smí ladit svou; tenant-admin nesmí přepsat cizí privátní bez permission; tenant kolekce → jen `rag.collection.manage` · Mechanismus: Scope check (Tenant|User) + permission · Severity: P1 · Test: integ — user ladí svou privátní OK; jiný user → 404
- **EC-09-04-05 — Globální default když kolekce nemá nastavení** · Trigger: stará kolekce bez Freshness sloupců (před migrací) · Očekávané chování: null sloupce → fallback na globální `Rag:Freshness:*` defaulty, nikdy crash · Mechanismus: coalesce v retrievalu (`collection.HalfLife ?? globalDefault`) · Severity: P2 · Test: unit — null sloupce → použije globální default
- **EC-09-04-06 — DecayWeight=0 (freshness úplně vypnuta)** · Trigger: admin vypne decay · Očekávané chování: legitimní stav; retrieval = čistý rerank bez time vlivu; `IsCurrent` HARD filtr ALE zůstává aktivní (verzování ≠ freshness) · Mechanismus: `DecayWeight=0` přeskočí decay násobení, `IsCurrent` filtr nezávislý · Severity: P1 · Test: integ — weight 0 → superseded stále vyloučen, pořadí = rerank

---

## UC-09-05 — Dokument bez data (chybějící timestamp) — fallback do decay
- **Actor / role:** system/worker (decay vrstva), nepřímo user
- **Precondition:** Ingestovaný dokument, ze kterého nešlo odvodit `EffectiveDate` (žádné metadata, žádné parsovatelné datum v obsahu).
- **Trigger:** decay re-scoring (UC-09-01) narazí na kandidáta s `EffectiveDate == null`.
- **Main flow:**
  1. Při ingestu se `Document.EffectiveDate` pokusí odvodit (metadata → obsah → fallback). Pokud null, zůstává null a nastaví se `Document.DateSource = None`.
  2. V decay vrstvě: dle `Rag:Freshness:UndatedPolicy`:
     - `UseIngestDate` (default) → použij `Chunk.CreatedAt` jako proxy data (čerstvost = kdy bylo nahráno).
     - `NeutralNoDecay` → `decayFactor = 1` (dokument bez data se nepenalizuje za stáří).
     - `Penalize` → aplikuj fixní `UndatedPenaltyFactor ∈ (0,1]` (považuj nedatovaný za potenciálně zastaralý).
- **Postcondition / záruky:** Žádný NRE; deterministické dle politiky; read-only.
- **Tenancy / permissions:** N/A (interní decay logika).
- **Reuse / canonical pattern:** Decay funkce UC-09-01; politika jako konfigurovatelný enum (copy options pattern).
- **Data dotčená:** `documents` (EffectiveDate, DateSource), `chunks` (CreatedAt) · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-09-05
- **EC-09-05-01 — Smíšená kolekce (datované + nedatované)** · Trigger: půlka dokumentů má datum, půlka ne, politika `NeutralNoDecay` · Očekávané chování: nedatované dostanou `decayFactor=1` (žádný věk) → mohou systematicky převyšovat staré datované; admin si musí být vědom — proto preview (UC-09-09); default `UseIngestDate` to zmírňuje · Mechanismus: konzistentní politika napříč kolekcí; debug expozice `DateSource` · Severity: P2 · Test: integ — mix → nedatované dle politiky, datované dle stáří
- **EC-09-05-02 — Datum odvozeno z obsahu chybně (parsing halucinace)** · Trigger: regex/LLM extrakce data vytáhne „1970" nebo nesmysl z patičky · Očekávané chování: sanity range guard (`EffectiveDate` musí být v `[reasonableMin, UtcNow+slack]`); mimo rozsah → považuj za null (`DateSource = None`) · Mechanismus: validace odvozeného data při ingestu · Severity: P1 · Test: unit — extrahované „1900-01-01" → odmítnuto, EffectiveDate=null
- **EC-09-05-03 — Více kandidátních dat v dokumentu** · Trigger: dokument zmiňuje publikační datum, datum revize, datum účinnosti · Očekávané chování: deterministická priorita (metadata > revize > publikace > obsah) konfigurovaná v ingest pipeline; vybrané `DateSource` se uloží pro transparentnost · Mechanismus: dokumentovaná priorita; `Document.DateSource` enum · Severity: P2 · Test: unit — dokument s 3 daty → vybráno dle priority
- **EC-09-05-04 — UndatedPenaltyFactor špatně nakonfigurovaný** · Trigger: `Penalize` politika s factor=0 → nedatované vždy úplně potlačeny · Očekávané chování: validátor `UndatedPenaltyFactor ∈ (0,1]` (ostře nad 0), aby nedatovaný relevantní dokument nezmizel úplně · Mechanismus: options validátor · Severity: P2 · Test: unit — factor=0 odmítnuto
- **EC-09-05-05 — Všechny dokumenty v kolekci nedatované** · Trigger: celá kolekce bez dat · Očekávané chování: decay je de-facto no-op (všichni stejní); pořadí = rerank; žádná chyba · Mechanismus: politika aplikována uniformně · Severity: P3 · Test: integ — všechny null + UseIngestDate → řazení dle ingest času sekundárně

---

## UC-09-06 — Rozlišení changelog (append) vs. neplatná data (supersede)
- **Actor / role:** user | tenant-admin při (re)ingestu; rozhoduje versioning policy dokumentu/kolekce
- **Precondition:** Re-ingest nebo nová verze dokumentu. Otázka: nahradit stará fakta (supersede, stará verze NEPLATNÁ) NEBO připojit jako další záznam v čase (changelog/append, stará verze STÁLE PLATNÁ historie)?
- **Trigger:** `ReingestDocumentCommand` s `VersioningMode { Supersede | Append }` (z konfigurace kolekce `Rag:Freshness:VersioningMode` nebo per-request override).
- **Main flow:**
  1. `Supersede` (default pro „živé" dokumenty jako ceník, FAQ): staré chunky `IsCurrent=false` (UC-09-02) — stará fakta jsou neplatná, nesmí proniknout do retrievalu.
  2. `Append` (pro changelogy, audit logy, time-series záznamy): NOVÉ chunky `IsCurrent=true`, ALE staré chunky ZŮSTÁVAJÍ `IsCurrent=true` — obě verze jsou platná historie; rozlišení v čase řeší time-decay (UC-09-01), ne HARD filtr.
  3. Mode se persistuje per dokument (`Document.VersioningMode`), aby budoucí re-ingesty byly konzistentní.
- **Postcondition / záruky:** `Supersede` → max jedna current sada; `Append` → kumulativní current sada s časovým rozlišením přes decay. Read-only retrieval respektuje obě.
- **Tenancy / permissions:** Scope dokumentu; permission `rag.document.write`.
- **Reuse / canonical pattern:** Re-ingest saga UC-09-02; rozhodovací logika jako business rule (`BusinessRuleException` při konfliktu).
- **Data dotčená:** `documents` (VersioningMode), `chunks` (IsCurrent dle mode) · **Eventy:** `DocumentReindexedIntegrationEvent` (nese `Mode`)
- **Priorita:** P1

### Edge cases UC-09-06
- **EC-09-06-01 — Append mode → nekonečný růst current chunků (supernode na časové ose)** · Trigger: denní append do changelogu po roky · Očekávané chování: bounded — buď MAXLEN/retention na current verze (`Rag:Freshness:AppendMaxVersions`), nebo velmi staré append verze auto-superseded; decay je zmírňuje, ale neomezuje počet · Mechanismus: retention sweep job (copy `GdprRetentionSweepJob` seam) na append historii · Severity: P1 · Test: integ — 1000 append verzí + retention → current omezeno na konfigurovaný počet
- **EC-09-06-02 — Změna mode mezi verzemi (Supersede → Append)** · Trigger: dokument byl Supersede, admin přepne na Append · Očekávané chování: deterministické — nová verze append, ALE už-superseded (`IsCurrent=false`) verze se NEvzkřísí; jen forward chování se mění · Mechanismus: mode ovlivňuje budoucí flip, nikdy neobnoví staré false→true bez explicitního restore (UC-09-07) · Severity: P2 · Test: integ — přepnutí mode neobnoví staré superseded chunky
- **EC-09-06-03 — Append duplikuje fakt (stejný obsah ve dvou verzích)** · Trigger: changelog opakuje nezměněný odstavec · Očekávané chování: retrieval může vrátit dva téměř-duplikátní chunky z různých časů; dedup po reranku (near-duplicate collapse) ponechá nejčerstvější/nejrelevantnější, ostatní označí jako související verze · Mechanismus: post-rerank dedup dle content-hash + version awareness · Severity: P2 · Test: integ — identický odstavec v 2 append verzích → vrácen jednou (nejčerstvější), ne dvakrát
- **EC-09-06-04 — Špatná volba mode = ztráta čerstvosti** · Trigger: živý ceník nahrán jako `Append` → staré ceny stále vyhledatelné a mohou vyhrát · Očekávané chování: dokumentační varování; default `Supersede`; pokud `Append` na evidentně „živém" typu, decay alespoň upřednostní nový — ale stará cena MŮŽE proniknout → toto je důvod, proč Supersede existuje · Mechanismus: explicit policy + admin preview (UC-09-09) ukazuje, že staré verze jsou retrievable · Severity: P1 · Test: integ — Append ceník → stará cena retrievable (demonstrace rizika); Supersede → nikdy
- **EC-09-06-05 — Citace musí odlišit historickou vs aktuální verzi** · Trigger: Append výsledek mixuje verze z 2023 a 2026 · Očekávané chování: každá citace nese `Version` + `EffectiveDate`, aby uživatel/LLM věděl, že jde o historický záznam, ne aktuální fakt · Mechanismus: citation metadata (UC-09-08) · Severity: P1 · Test: integ — Append výsledek → citace s rozlišnými daty

---

## UC-09-07 — Rollback / restore předchozí verze dokumentu
- **Actor / role:** tenant-admin (oprávněný vlastník)
- **Precondition:** Dokument má aktuální verzi `N` (`IsCurrent=true`) a starší superseded verzi `N-1` (`IsCurrent=false`) zachovanou v historii. Admin zjistí, že nová verze byla chybná, a chce vrátit předchozí.
- **Trigger:** HTTP `POST /v1/rag/documents/{documentId}/versions/{version}/restore` → `RestoreDocumentVersionCommand` → handler.
- **Main flow:**
  1. Handler ověří vlastnictví (RLS → 404), ověří, že cílová `version` existuje a její chunky jsou zachované.
  2. Atomicky: aktuální verze `N` → `IsCurrent=false`, cílová verze → `IsCurrent=true` (NEBO se vytvoří nová verze `N+1` jako kopie staré — „forward restore" pro lineární historii — preferováno, aby historie nebyla nelineární).
  3. Publikuje `DocumentVersionRestoredIntegrationEvent { DocumentId, RestoredFromVersion, NewVersion }`.
  4. 200 / 202.
- **Postcondition / záruky:** Retrieval okamžitě vrací obnovenou verzi; chybná verze se stane superseded. Idempotentní (restore na již-current verzi = no-op). Audit/event zachycuje akci.
- **Tenancy / permissions:** Scope dokumentu; permission `rag.document.write` (případně silnější `rag.document.restore`).
- **Reuse / canonical pattern:** Atomický flip = `ExecuteUpdate` guard; outbox event `RegisterUserHandler.cs:22`; terminal-state idempotence jako saga.
- **Data dotčená:** `chunks` (IsCurrent flip), `documents` (Version) · **Eventy:** `DocumentVersionRestoredIntegrationEvent`
- **Priorita:** P2

### Edge cases UC-09-07
- **EC-09-07-01 — Restore na verzi, jejíž chunky byly GDPR-vymazány** · Trigger: cílová verze obsahovala PII subjektu, který požádal o erasure → DEK shrednut, chunky `[erased]` · Očekávané chování: restore NESMÍ vzkřísit čitelná PII; obnovené chunky zůstanou `[erased]` (ciphertext nečitelný, DEK pryč); restore historického obsahu nesmí obejít erasure · Mechanismus: crypto-shred je nevratný (DEK tombstone retained); decryption converter vrací `[erased]` · Severity: P0 · Test: integ — erase subjekt → restore staré verze → obsah `[erased]`, ne plaintext
- **EC-09-07-02 — Restore na verzi, která už neexistuje (retention smazala)** · Trigger: stará verze byla vyčištěna retention sweepem (UC-09-10) · Očekávané chování: 404 `rag.document.version_not_found`; nikdy restore „prázdné" verze · Mechanismus: existence check před flipem · Severity: P1 · Test: integ — restore vyčištěné verze → 404
- **EC-09-07-03 — Concurrent restore + re-ingest** · Trigger: admin restoruje N-1, souběžně přijde re-ingest N+1 · Očekávané chování: serializace přes verzní guard `WHERE Version < @newVersion` + xmin; vyhraje vyšší výsledná verze; žádné dvě current sady · Mechanismus: atomický flip + concurrency retry · Severity: P1 · Test: integ — souběh → konzistentní jedna current verze
- **EC-09-07-04 — Restore vytvoří nelineární historii** · Trigger: restore přímým flipnutím (N-1 → current) místo forward kopie · Očekávané chování: preferovat „forward restore" (nová verze N+1 = obsah N-1) → historie zůstane monotónní pro audit a as-of dotazy · Mechanismus: handler vytvoří novou verzi místo reaktivace staré · Severity: P2 · Test: integ — restore → Version monotónně roste, žádná díra/reuse
- **EC-09-07-05 — Restore u Append-mode dokumentu** · Trigger: restore nedává smysl, když jsou všechny verze current · Očekávané chování: 409 `rag.document.restore_not_applicable` pro `Append` mode (není co restorovat, nic není superseded) · Mechanismus: business rule check na `VersioningMode` · Severity: P2 · Test: integ — restore na Append dokument → 409

---

## UC-09-08 — Citace s verzí + datem (version/freshness metadata v odpovědi)
- **Actor / role:** user | tenant-admin (čte odpověď), system (sestavuje)
- **Precondition:** Retrieval + decay vrátily finální chunky; sestavuje se odpověď (RAG generace přes Claude nebo prostý retrieval výsledek).
- **Trigger:** in-process — `SearchKnowledgeQueryHandler` / `AnswerQuestionHandler` mapuje chunky → citace.
- **Main flow:**
  1. Pro každý použitý chunk se do citace přidá `DocumentId`, `FileName`, `Version`, `EffectiveDate`, `IsCurrent`, `AgeDays`, `DecayFactor` (debug-gated), `ChunkId`.
  2. Odpověď nese `freshness` blok: nejstarší/nejnovější `EffectiveDate` mezi citacemi, příznak `containsHistoricalVersions` (true u Append).
  3. Citation-missing guard: pokud generovaná odpověď tvrdí fakt bez navázané citace → odpověď označena `unsupported` / odmítnuta (závisí na oblasti generace, zde se garantuje, že každý retrieval výsledek MÁ verzní metadata).
- **Postcondition / záruky:** Každý vrácený fragment je jednoznačně identifikovatelný včetně verze a stáří; uživatel pozná zastaralý/historický záznam. Read-only.
- **Tenancy / permissions:** Citace odhalují jen data, na která má volající scope (RLS už filtroval).
- **Reuse / canonical pattern:** Response DTO mapping (vertical slice response record); debug expozice gated jako preview.
- **Data dotčená:** `chunks`, `documents` (read) · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-09-08
- **EC-09-08-01 — Citace bez data (nedatovaný dokument)** · Trigger: chunk z nedatovaného dokumentu · Očekávané chování: citace nese `EffectiveDate=null` + `DateSource=None` + lidsky čitelné „datum neznámé", ne `0001-01-01` ani prázdno · Mechanismus: explicitní null handling v DTO · Severity: P2 · Test: unit — null date → citace `dateSource: none`
- **EC-09-08-02 — Citace historické (Append) bez označení** · Trigger: vrácen starý platný changelog záznam · Očekávané chování: `IsCurrent=true` ale `EffectiveDate` starý → `containsHistoricalVersions=true`; LLM prompt instruuje, že jde o historický kontext · Mechanismus: freshness blok + prompt anotace · Severity: P1 · Test: integ — Append výsledek → flag true
- **EC-09-08-03 — Debug freshness metadata uniknou v produkci** · Trigger: `DecayFactor`/interní skóre v produkční odpovědi · Očekávané chování: interní debug pole gated za `Rag:Debug:ExposeScores` (default off mimo Dev); produkce vrací jen `Version`+`EffectiveDate` · Mechanismus: conditional serializace (fail-safe off) · Severity: P2 · Test: integ — prod config → odpověď NEobsahuje decayFactor
- **EC-09-08-04 — Citace na chunk, který byl mezi retrievalem a renderem superseden** · Trigger: re-ingest proběhne těsně po retrievalu, citovaný chunk je teď `IsCurrent=false` · Očekávané chování: citace je snapshot z času dotazu (konzistentní); volitelně příznak `staleAsOf` pokud follow-up zjistí změnu; nikdy crash · Mechanismus: snapshot konzistence dotazu · Severity: P3 · Test: integ — reingest po query → vrácená citace stále validně identifikuje verzi
- **EC-09-08-05 — PII v citovaném snippetu** · Trigger: citace obsahuje úryvek šifrovaného `[Encrypted][PersonalData]` chunku · Očekávané chování: dešifrováno jen pokud má volající scope (DEK subjektu); jinak `[erased]`/skryto; citace neodhalí PII cizího subjektu · Mechanismus: decryption converter + RLS · Severity: P0 · Test: integ — citace cizí-subjekt PII (mimo scope) → nečitelná

---

## UC-09-09 — Admin preview / tuning freshness (relevance vs. čerstvost balance)
- **Actor / role:** tenant-admin
- **Precondition:** Admin ladí `HalfLifeDays`/`DecayWeight` a chce vidět DOPAD na pořadí pro vzorový dotaz PŘED uložením.
- **Trigger:** HTTP `POST /v1/rag/collections/{collectionId}/freshness/preview` `{ Query, HalfLifeDays, DecayWeight, UndatedPolicy }` → `PreviewFreshnessQuery` (query, nemutuje!).
- **Main flow:**
  1. Spustí se plný retrieval + rerank pro `Query`.
  2. Spočítají se DVĚ varianty řazení: (a) bez decay (jen rerank), (b) s navrženými freshness parametry.
  3. Vrátí se side-by-side: pro každý kandidát `RerankScore`, `EffectiveDate`, `AgeDays`, `DecayFactor`, `FinalScore`, `RankBefore`, `RankAfter`, `RankDelta`.
  4. Žádná persistencia — admin uloží přes UC-09-04, pokud je spokojen.
- **Postcondition / záruky:** Read-only, žádná mutace, žádný event. Deterministické pro fixní `IClock`.
- **Tenancy / permissions:** Scope Tenant; permission `rag.collection.manage`; RLS.
- **Reuse / canonical pattern:** Read query `GetProfileHandler.cs:12`; decay funkce sdílená s UC-09-01 (DRY — preview a produkční retrieval volají TUTÉŽ funkci, jen s jinými parametry).
- **Data dotčená:** `chunks`, `documents` (read) · **Eventy:** žádné
- **Priorita:** P2

### Edge cases UC-09-09
- **EC-09-09-01 — Preview používá jinou decay logiku než produkce** · Trigger: duplikovaná implementace decay v preview vs retrieval · Očekávané chování: ZAKÁZÁNO — obě cesty volají identickou `ApplyFreshnessDecay`, jinak preview lže · Mechanismus: DRY (CLAUDE.md Law 4 — reuse, nekopíruj logiku); sdílená funkce · Severity: P1 · Test: unit — preview a retrieval dají identické finalScore pro stejné parametry
- **EC-09-09-02 — Preview je drahý (plný retrieval + 2× scoring)** · Trigger: admin spamuje preview · Očekávané chování: rate-limit (`"auth"`-like policy nebo per-user limit) + cap na topN v preview · Mechanismus: partitioned rate limiter (CLAUDE.md request-edge hardening) · Severity: P2 · Test: integ — N preview/min → 429 + Retry-After
- **EC-09-09-03 — Preview na prázdné kolekci / zero-retrieval** · Trigger: dotaz nic nevrátí · Očekávané chování: prázdný-ale-honest výsledek s `degraded: no_candidates`, ne chyba · Mechanismus: graceful degradation · Severity: P2 · Test: integ — prázdná kolekce → 200 prázdný seznam, flag
- **EC-09-09-04 — Navržené parametry mimo rozsah** · Trigger: preview s `DecayWeight=5` · Očekávané chování: stejný validátor jako UC-09-04 → 400 · Mechanismus: sdílený validátor · Severity: P2 · Test: integ — invalid preview params → 400
- **EC-09-09-05 — Preview odhalí, že čerstvost potlačila autoritativní starý fakt** · Trigger: silný `DecayWeight` shodí relevantní starý dokument · Očekávané chování: `RankDelta` to viditelně ukáže (starý dokument spadl) → admin informovaně rozhodne; systém nevnucuje · Mechanismus: side-by-side delta v odpovědi · Severity: P3 · Test: integ — silný decay → starý relevantní kandidát má záporný RankDelta

---

## UC-09-10 — Retention / cleanup starých superseded verzí (vacuum verzí)
- **Actor / role:** system/worker (cron job v Jobs hostu)
- **Precondition:** Dokumenty mají naakumulované superseded (`IsCurrent=false`) chunky a staré append verze, které překračují retenční politiku (`Rag:Freshness:VersionRetention { MaxVersions, MaxAgeDays }`).
- **Trigger:** Quartz cron `Modules:Rag:Jobs:VersionRetentionCron` → `IJob` → `PruneDocumentVersionsCommand` → handler.
- **Main flow:**
  1. Job dispatchuje command (pure publisher; logika v handleru, ne v jobu — CLAUDE.md cron pravidlo).
  2. Handler najde superseded chunky starší než `MaxAgeDays` NEBO nad rámec `MaxVersions` na dokument (LINQ, žádné raw SQL).
  3. Smaže/anonymizuje je (soft-delete nebo hard-delete dle politiky); odpovídající blob bytes v `IFileStorage` jen pokud žádná zachovaná verze je nepoužívá.
  4. NIKDY nesmaže `IsCurrent=true` chunky; NIKDY append-only audit/ledger; respektuje GDPR (PII chunky se neobnovují).
  5. Per-run cap (jako reconcile joby); idempotentní; publikuje `DocumentVersionsPrunedIntegrationEvent` (metriky).
- **Postcondition / záruky:** Historie ořezána dle politiky; aktuální verze a recent historie zachována. Job běží opakovaně bezpečně (idempotentní, order-independent).
- **Tenancy / permissions:** Systémový kontext (`SystemTenantContext` v Jobs hostu); operuje napříč tenanty, ale per-tenant politika.
- **Reuse / canonical pattern:** Cron job `BillingExpireCreditsJob` + `RetentionSweepCommand` seam (`GdprRetentionSweepJob`); per-run cap jako `ReconcileStripeCommand`; metrika `PlatformMetrics.Meter` (`platform.rag.versions_pruned`).
- **Data dotčená:** `chunks` (delete superseded), `file_objects`/blob (orphan cleanup) · **Eventy:** `DocumentVersionsPrunedIntegrationEvent`
- **Priorita:** P2

### Edge cases UC-09-10
- **EC-09-10-01 — Prune smaže verzi potřebnou pro as-of dotaz / restore** · Trigger: retenční okno je kratší než očekávání pro point-in-time · Očekávané chování: dokumentovaný trade-off; `MaxAgeDays` musí pokrýt as-of horizont; prune jen za hranicí; restore vyčištěné verze → 404 (UC-09-07-02) · Mechanismus: konfigurovatelná politika; konzistence s UC-09-11 oknem · Severity: P1 · Test: integ — prune respektuje MaxAgeDays; verze uvnitř okna zůstane
- **EC-09-10-02 — Souběh prune + retrieval** · Trigger: query čte chunk, který prune zrovna maže · Očekávané chování: query vidí jen `IsCurrent=true` (HARD filtr) → mazané superseded chunky stejně nečte; žádná inkonzistence · Mechanismus: prune se dotýká jen `IsCurrent=false`; retrieval filtr je ortogonální · Severity: P2 · Test: integ — souběh → query výsledky neovlivněné
- **EC-09-10-03 — Prune omylem smaže current verzi** · Trigger: chyba v predikátu · Očekávané chování: NIKDY — predikát MUSÍ obsahovat `IsCurrent == false`; guard test · Mechanismus: explicitní `WHERE !IsCurrent` + invariant test · Severity: P0 · Test: integ — prune nikdy nesníží počet `IsCurrent=true` chunků
- **EC-09-10-04 — Orphan blob po prune** · Trigger: smazána poslední verze odkazující na blob · Očekávané chování: blob v `IFileStorage` se smaže jen pokud žádná zachovaná verze (current i retained) ho nepoužívá; jinak orphan storage roste · Mechanismus: ref-count / kontrola před `IFileStorage.DeleteAsync`; kompenzační delete vzor `UploadFileHandler.cs:21` · Severity: P1 · Test: integ — prune poslední verze → blob smazán; prune s živou verzí → blob zůstává
- **EC-09-10-05 — Prune běží na multi-instance Jobs (duplicitní spuštění)** · Trigger: víc Jobs instancí · Očekávané chování: idempotentní + order-independent; dvojí prune nezpůsobí chybu (už smazané = no-op) · Mechanismus: idempotentní delete; Quartz clustering nebo idempotence handleru · Severity: P2 · Test: integ — 2× prune stejných verzí → bez chyby, stejný výsledek
- **EC-09-10-06 — Prune nesmí mazat audit / append-only** · Trigger: politika omylem cílí na `{rag}_audit_entries` · Očekávané chování: ZAKÁZÁNO — audit a verzovací event historie se nemažou (AML/forenzní); prune jen chunk data · Mechanismus: prune scope omezen na `chunks`/blob; audit retained · Severity: P0 · Test: integ — prune nezmění audit entries count
- **EC-09-10-07 — Job exception → DLQ / retry storm** · Trigger: prune hodí výjimku uprostřed dávky · Očekávané chování: per-run cap omezí blast; messaging retry-with-cooldown → DLQ; další běh dokončí zbytek (idempotentní) · Mechanismus: `PlatformMessaging` retry/DLQ; cap jako reconcile · Severity: P2 · Test: integ — injektovaná chyba → retry, žádná nekonečná smyčka

---

## UC-09-11 — Point-in-time / as-of retrieval (dotaz k historickému datu)
- **Actor / role:** user | tenant-admin
- **Precondition:** Dokumenty mají verzovanou historii (Append nebo zachované superseded verze v retenčním okně). Uživatel se ptá „jak to bylo k datu T".
- **Trigger:** HTTP `GET/POST /v1/rag/search?asOf={timestamp}` → `SearchKnowledgeQuery { AsOf }` (query).
- **Main flow:**
  1. Místo defaultního HARD `IsCurrent=true` filtru se aplikuje verzní okno: vyber pro každý dokument verzi, jejíž platnost pokrývá `AsOf` (`EffectiveDate <= AsOf` a žádná novější verze ≤ AsOf, nebo `ValidFrom <= AsOf < ValidUntil`).
  2. Time-decay se počítá relativně k `AsOf`, NE k `UtcNow` (čerstvost z pohledu zvoleného okamžiku).
  3. Retrieval + rerank + decay na této historické množině; citace nesou verzi platnou v T.
- **Postcondition / záruky:** Výsledek reflektuje stav znalostí k `AsOf`. Read-only. `AsOf` mimo retenční okno → degradace (viz EC).
- **Tenancy / permissions:** Standardní Scope/RLS; permission stejná jako běžný search.
- **Reuse / canonical pattern:** Read query; parametrizovaný verzní predikát (rozšíření UC-09-03 filtru); decay funkce s injektovaným referenčním časem místo `IClock.UtcNow`.
- **Data dotčená:** `chunks` (Version, EffectiveDate, ValidFrom/Until), `documents` · **Eventy:** žádné
- **Priorita:** P3

### Edge cases UC-09-11
- **EC-09-11-01 — AsOf v budoucnosti** · Trigger: `asOf = UtcNow + 1y` · Očekávané chování: clamp na `UtcNow` (nelze dotazovat budoucnost) nebo 400 `rag.search.asof_future`; decay relativně k `UtcNow` · Mechanismus: validace `AsOf <= UtcNow` · Severity: P2 · Test: integ — budoucí asOf → 400/clamp
- **EC-09-11-02 — AsOf před existencí jakékoli verze** · Trigger: `asOf` starší než nejstarší dokument · Očekávané chování: zero-retrieval → `degraded: no_data_at_asof`, prázdný-honest výsledek · Mechanismus: graceful degradation · Severity: P2 · Test: integ — pradávné asOf → prázdný + flag
- **EC-09-11-03 — Verze platná v T byla retention-pruned** · Trigger: `asOf` uvnitř teoretického okna, ale verze už smazána (UC-09-10) · Očekávané chování: explicitní `Partial`/`Degraded` flag „historie nedostupná pro část dokumentů", NE tiché vrácení novější verze jako by byla platná v T · Mechanismus: detekce chybějící historie + degradace flag (NIKDY tichá záměna verze) · Severity: P1 · Test: integ — prune verzi, as-of na ni → degraded flag, ne novější verze maskovaná jako historická
- **EC-09-11-04 — AsOf decay vs UtcNow decay záměna** · Trigger: implementace omylem počítá decay k `UtcNow` u as-of dotazu · Očekávané chování: decay referenční čas = `AsOf`, jinak jsou všechny historické verze „staré" a decay je nesmyslný · Mechanismus: decay funkce přijímá referenční čas jako parametr (ne pevně `IClock.UtcNow`) · Severity: P2 · Test: unit — as-of decay používá AsOf jako referenci
- **EC-09-11-05 — Supersede-mode dokument bez zachované staré verze** · Trigger: Supersede dokument, jehož N-1 byla pruned, as-of cílí na N-1 · Očekávané chování: degraded (jako EC-09-11-03); Supersede negarantuje historii bez retence · Mechanismus: stejný degradation flag · Severity: P3 · Test: integ — supersede + pruned historie + as-of → degraded
- **EC-09-11-06 — Cross-tenant/IDOR přes asOf** · Trigger: pokus dostat se k cizí historii přes parametr · Očekávané chování: RLS filtruje stejně jako u běžného dotazu; `asOf` neobchází tenant/user scope · Mechanismus: RLS + Scope predikát aplikován před verzním oknem · Severity: P0 · Test: integ — as-of dotaz nevidí cizí tenant historii


---

## Doplňky z completeness review

### UC-09-01 (time-decay po reranku)
- **EC-09-01-10 — Freshness po reranku může jen přeřadit přeživší top-8, nikdy nezachrání čerstvý dokument, který rerank zahodil** · Trigger: relevantní čerstvý dokument skončí rerankem na pozici 9 (mimo top-8), decay běží až nad top-8 · Očekávané chování: frozen pořadí „decay AŽ PO reranku" znamená, že decay reorderuje pouze rerank-survivors — velmi čerstvý, ale rerankem vyřazený dokument je nenávratně pryč. To je DŮSLEDEK zákona, který je třeba EXPLICITNĚ zdokumentovat; pokud má freshness reálně promovat čerstvost, decay musí běžet nad PRE-rerank top-50 (kandidátní okno), ne nad post-rerank top-8 — otevřené rozhodnutí (kde v pipeline decay aplikovat vs frozen „po reranku") · Mechanismus: dokumentovaný trade-off; pokud rescue žádán → decay nad rerank candidate window před finálním Take(8) · Severity: P1 · Test: integrační — čerstvý dokument na rerank-pozici 9 se s decay NEdostane do výsledku (demonstrace limitu); rozhodnutí potvrzeno.
