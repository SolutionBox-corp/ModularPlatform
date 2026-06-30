# Oblast 23 — Admin / management (catalogue, reindex, delete)
Tato oblast pokrývá administrativní a správcovské operace nad korpusem HybridRag: výpis kolekcí a dokumentů (paged, RLS-scoped), reindexaci dokumentu i celé kolekce (re-embed novým modelem, re-chunk), kaskádové mazání dokumentu i kolekce (chunky + vektory + grafové uzly/hrany + aliasy), statistiky dokumentů a kolekcí, reprocessing selhaného ingestu, bulk operace, manuální supersede a oddělení práv tenant-admin vs user. Mapuje se na build fázi **F7 — Admin & lifecycle management** (po F1-ingest, F4-retrieval, F6-graph), staví výhradně na již vyřešených platform seamech (vertical slice, outbox, saga, IOperationStore 202, IFileStorage, RLS, audit, GDPR) a NEzavádí žádný paralelní mechanismus.

## UC-23-01 — Výpis kolekcí (paged, RLS-scoped)
- **Actor / role:** user | tenant-admin
- **Precondition:** Autentizovaný uživatel; existuje ≥0 `KnowledgeCollection` v tenantu (Scope=Tenant) a/nebo privátních (Scope=User, OwnerUserId=já).
- **Trigger:** `GET /v1/rag/collections?page=1&pageSize=20&scope=all|tenant|mine`
- **Main flow:** Endpoint → mapuje Request na `ListCollectionsQuery(Page, PageSize, ScopeFilter)` → `IDispatcher.Query` → `ListCollectionsHandler` (read-only, `IReadDbContextFactory`) → LINQ projekce `KnowledgeCollection` filtrovaná global query filterem (tenant) + RLS (`IUserOwned` se zde neuplatní na kolekci přímo, ale viditelnost privátních = `Scope==User && OwnerUserId==tenant.UserId`) → `OrderBy(Name)` → `Skip/Take` → vrací `Paged<CollectionSummaryDto>` (Id, Name, Scope, DocumentCount, totalCount) v `ApiResponse<T>.Ok`.
- **Postcondition / záruky:** 200; žádná mutace; žádný event; stabilní řazení (Name asc, tie-break Id).
- **Tenancy / permissions:** Scope Tenant pro tenant kolekce + Scope User pro privátní; bez extra permission (čtení vlastního/tenant katalogu). RLS + global filter brání cross-tenant i cross-user úniku.
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12` (read query, IReadDbContextFactory, žádná transakce).
- **Data dotčena:** `knowledge_collections` · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-23-01
- **EC-23-01-01 — Prázdný katalog** · Trigger: tenant nemá žádné kolekce · Očekávané chování: 200 + `items:[]`, `totalCount:0` (NE 404) · Mechanismus: query vrací prázdný seznam, žádná NotFoundException; zákon §2 (query jen čte) · Severity: P2 · Test: integration — nový tenant → assert 200 a prázdné pole.
- **EC-23-01-02 — pageSize mimo rozsah (0, záporné, >100)** · Trigger: `pageSize=0` / `=-5` / `=10000` · Očekávané chování: ValidationException `rag.paging_invalid` (clamp NE — explicitní validace) · Mechanismus: `ListCollectionsValidator` `.WithErrorCode("rag.paging_invalid")`, ValidationBehavior · Severity: P2 · Test: assert 400 + errorCode pro každou hranici.
- **EC-23-01-03 — page za posledním** · Trigger: `page=999` při 3 kolekcích · Očekávané chování: 200 + prázdné items + `totalCount=3` (klient pozná přestřel) · Mechanismus: Skip/Take vrací prázdno, totalCount z `CountAsync` · Severity: P3 · Test: assert prázdné items, totalCount správný.
- **EC-23-01-04 — Cross-tenant izolace** · Trigger: uživatel tenantu A žádá; v DB existují kolekce tenantu B · Očekávané chování: kolekce B se NIKDY neobjeví · Mechanismus: EF global query filter `IsSystem ‖ TenantId==claim` (§4 Tenant scoping) · Severity: P0 · Test: dva tenanti → A nevidí B (assert count + žádné Id z B).
- **EC-23-01-05 — Privátní kolekce jiného uživatele v témž tenantu** · Trigger: user X žádá `scope=all`; user Y má privátní kolekci · Očekávané chování: kolekce Y se NEzobrazí X · Mechanismus: handler filtruje `Scope==Tenant || (Scope==User && OwnerUserId==me)`; rozhodnutí #2 (dvouvrstvé vlastnictví) · Severity: P0 · Test: assert privátní Y chybí u X, ale viditelná u Y.
- **EC-23-01-06 — Neplatný scope filtr** · Trigger: `scope=garbage` · Očekávané chování: 400 `rag.scope_invalid` · Mechanismus: validator enum-bind · Severity: P3 · Test: assert 400.
- **EC-23-01-07 — DocumentCount konzistence se soft-delete** · Trigger: kolekce má 5 dokumentů, 2 soft-deleted · Očekávané chování: DocumentCount=3 (počítá jen `!IsDeleted`) · Mechanismus: `ISoftDeletable` global filter na `Document` se aplikuje i v agregaci · Severity: P1 · Test: soft-delete 2 → assert count 3.
- **EC-23-01-08 — Rate-limit / DoS na výpis** · Trigger: agresivní polling · Očekávané chování: 429 + Retry-After po překročení · Mechanismus: globální partitioned rate-limiter per user (§4 Request-edge hardening) · Severity: P2 · Test: zaplav → assert 429.
- **EC-23-01-09 — Anonymní přístup** · Trigger: bez tokenu · Očekávané chování: 401 · Mechanismus: JWT auth na `/v1` group; `ITenantContext` nedostupný · Severity: P0 · Test: bez Authorization → 401.

## UC-23-02 — Výpis dokumentů v kolekci (paged, se statusem)
- **Actor / role:** user | tenant-admin
- **Precondition:** Existuje `KnowledgeCollection` viditelná pro volajícího.
- **Trigger:** `GET /v1/rag/collections/{collectionId}/documents?page=&pageSize=&status=`
- **Main flow:** Endpoint → `ListDocumentsQuery(CollectionId, Page, PageSize, StatusFilter)` → `ListDocumentsHandler` (read) → ověří viditelnost kolekce (jinak 404) → LINQ na `Document` filtrované CollectionId + RLS (`IUserOwned`→OwnerUserId) + soft-delete filter → projekce `DocumentSummaryDto` (Id, FileName, ContentType, Status, ChunkCount, LastIndexedAt, SizeBytes) → `Paged<T>`.
- **Postcondition / záruky:** 200; žádná mutace; status odráží aktuální stav ingest pipeline (Pending/Processing/Indexed/Failed/Superseded).
- **Tenancy / permissions:** RLS na dokumentech (privátní → jen vlastník); tenant dokumenty viditelné všem v tenantu; bez extra permission.
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12`; paging `Paged<T>` (totalCount fix).
- **Data dotčena:** `documents`, `chunks` (count) · **Eventy:** žádné
- **Priorita:** P0

### Edge cases UC-23-02
- **EC-23-02-01 — Neexistující collectionId** · Trigger: náhodné Guid · Očekávané chování: 404 `rag.collection_not_found` · Mechanismus: handler `FirstOrDefault==null → NotFoundException`; canonical 404 · Severity: P1 · Test: assert 404.
- **EC-23-02-02 — IDOR na cizí kolekci (jiný tenant)** · Trigger: validní collectionId tenantu B · Očekávané chování: 404 (NE 403 — neprozradit existenci) · Mechanismus: global query filter → řádek neviditelný → NotFound; zákon §10 (identity z tokenu) · Severity: P0 · Test: A žádá kolekci B → 404.
- **EC-23-02-03 — IDOR na privátní kolekci jiného usera v témž tenantu** · Trigger: collectionId privátní kolekce usera Y · Očekávané chování: 404 pro X · Mechanismus: viditelnostní filtr `Scope==User && OwnerUserId==me` · Severity: P0 · Test: X žádá Y privátní → 404.
- **EC-23-02-04 — Soft-deleted dokument** · Trigger: dokument byl smazán · Očekávané chování: nezobrazí se ve výpisu · Mechanismus: `ISoftDeletable` global filter · Severity: P1 · Test: soft-delete → assert chybí.
- **EC-23-02-05 — Filtr statusu Failed** · Trigger: `status=Failed` · Očekávané chování: jen selhané ingesty (podklad pro reprocess UC-23-08) · Mechanismus: LINQ `Where(Status==Failed)` · Severity: P2 · Test: 1 failed mezi 3 → assert jen 1.
- **EC-23-02-06 — ChunkCount u rozpracovaného (Processing)** · Trigger: dokument mid-ingest · Očekávané chování: ChunkCount odráží dosud zapsané chunky (může růst), Status=Processing jasně signalizuje neúplnost · Mechanismus: count `chunks WHERE DocumentId && IsCurrent`; žádná tichá polovina (zákon graceful degradation) · Severity: P2 · Test: částečný ingest → status Processing, count<final.
- **EC-23-02-07 — Neplatný status enum** · Trigger: `status=Bogus` · Očekávané chování: 400 `rag.status_invalid` · Mechanismus: validator · Severity: P3 · Test: assert 400.
- **EC-23-02-08 — Superseded dokumenty viditelnost** · Trigger: dokument byl nahrazen reindexem (UC-23-10) · Očekávané chování: zobrazí se se Status=Superseded jen při explicitním filtru; default výpis je skrývá NEBO zobrazuje s jasným labelem (rozhodnutí: zobrazit jen s `includeSuperseded=true`) · Mechanismus: LINQ filtr na status · Severity: P2 · Test: supersede → default výpis neobsahuje, s flagem obsahuje.

## UC-23-03 — Detail dokumentu + statistiky (chunk count, index size, last indexed)
- **Actor / role:** user | tenant-admin
- **Precondition:** Dokument existuje a je viditelný.
- **Trigger:** `GET /v1/rag/documents/{documentId}`
- **Main flow:** Endpoint → `GetDocumentStatsQuery(DocumentId)` → `GetDocumentStatsHandler` (read) → ověří RLS-viditelnost → agreguje LINQ: `ChunkCount` (`IsCurrent`), `TotalContentBytes`/`EmbeddingDim`, `GraphNodeCount` (uzly odvozené z dokumentu — sledováno přes provenienci na chunk/edge), `LastIndexedAt`, `EmbeddingModel`, `Status`, `IngestErrorReason?` → `DocumentStatsDto`.
- **Postcondition / záruky:** 200; bez mutace; čísla konzistentní v rámci jednoho snapshotu (čteno z read repliky — možná drobná replikační latence, dokumentováno).
- **Tenancy / permissions:** RLS-scoped; cizí → 404.
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12`; status pattern jako `GetOperationStatusEndpoint` (RLS, cizí id→404).
- **Data dotčena:** `documents`, `chunks`, `graph_nodes`/`graph_edges` (count) · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-23-03
- **EC-23-03-01 — Cizí documentId (jiný tenant/user)** · Trigger: validní cizí Guid · Očekávané chování: 404 · Mechanismus: RLS/global filter → NotFound · Severity: P0 · Test: cross-tenant → 404.
- **EC-23-03-02 — Dokument bez chunků (ingest selhal hned)** · Trigger: Status=Failed, 0 chunků · Očekávané chování: 200, ChunkCount=0, IngestErrorReason vyplněn · Mechanismus: agregace tolerantní k 0; error reason perzistován při selhání ingestu · Severity: P2 · Test: failed doc → stats s reason.
- **EC-23-03-03 — Soft-deleted dokument detail** · Trigger: `documentId` smazaného · Očekávané chování: 404 (filtr skrývá) · Mechanismus: `ISoftDeletable` filter · Severity: P1 · Test: po delete → 404.
- **EC-23-03-04 — GraphNodeCount u supernodu** · Trigger: dokument přispěl k uzlu s tisíci hran · Očekávané chování: stats vrátí počty bez timeoutu; case supernode řešen v graph oblasti, zde jen count · Mechanismus: agregační COUNT, žádný traverz · Severity: P2 · Test: velký graf → stats < threshold ms.
- **EC-23-03-05 — Embedding model drift indikace** · Trigger: dokument indexován starým modelem, tenant přepnul na nový · Očekávané chování: stats ukáže `EmbeddingModel` + flag `NeedsReindex=true` · Mechanismus: porovnání uloženého modelu vs `Rag:Embeddings:CurrentModel` v projekci · Severity: P1 · Test: drift → NeedsReindex true.
- **EC-23-03-06 — IndexSize vs replikační latence** · Trigger: čtení těsně po ingestu · Očekávané chování: čísla mohou být lehce stará (read replika), nikdy nekonzistentní v rámci jedné odpovědi · Mechanismus: jeden read snapshot; dokumentováno · Severity: P3 · Test: N/A (dokumentační), případně assert vnitřní konzistence.

## UC-23-04 — Reindexace jednoho dokumentu (re-chunk + re-embed)
- **Actor / role:** user (vlastní dokument) | tenant-admin (tenant dokumenty)
- **Precondition:** Dokument existuje, Status∈{Indexed, Failed, Superseded}; originální bytes dostupné přes `IFileStorage` (StorageKey).
- **Trigger:** `POST /v1/rag/documents/{documentId}/reindex` (volitelně body `{ targetModel?, chunkingProfile? }`)
- **Main flow:** Endpoint → `ReindexDocumentCommand(DocumentId, TargetModel?, ChunkingProfile?)` → `ReindexDocumentHandler` (mutate) → ověří viditelnost+právo → `IOperationStore.CreateAsync` (operations row, IUserOwned) → přes `IDbContextOutbox` publikuje `DocumentReindexRequested` (durable) + `SaveChangesAndFlushMessagesAsync` (commit) → vrací **202** + `Location: /v1/operations/{id}` → Worker spustí `IngestSaga` v reindex režimu: stáhne bytes, re-chunk, re-embed novým modelem, zapíše nové chunky `IsCurrent=true`, staré označí `IsCurrent=false` (atomicky v rámci kroku), přepočítá grafové příspěvky, dokončí operaci.
- **Postcondition / záruky:** 202 ihned; po dokončení nové chunky/vektory aktuální, staré supersedovány (ne smazány hned — atomický switch), operation→Succeeded; idempotentní dle `reindex:{documentId}:{requestKey}`.
- **Tenancy / permissions:** user→jen vlastní (OwnerUserId==me); tenant dokument vyžaduje `PlatformPermissions.RagManage` (tenant-admin). RLS chrání data.
- **Reuse / canonical pattern:** `StartDemoOperationHandler.cs:17` (202+IOperationStore+outbox); saga `CreditPurchaseSaga.cs:30`; outbox `RegisterUserHandler.cs:22`.
- **Data dotčena:** `documents`, `chunks`, `graph_nodes`, `graph_edges`, `ingest_sagas`, `operations` · **Eventy:** `DocumentReindexRequested`, po dokončení `DocumentIndexedIntegrationEvent`
- **Priorita:** P1

### Edge cases UC-23-04
- **EC-23-04-01 — Duplicate reindex (dvojí klik / retry)** · Trigger: dvě stejné požadavky během běhu · Očekávané chování: druhý vrátí stejnou existující operaci (202 + tentýž Location), NE druhá saga · Mechanismus: UNIQUE idempotency key `reindex:{documentId}` na běžící operaci; catch `DbUpdateException` → vrať existující (§4 idempotency) · Severity: P0 · Test: dvojí POST → jedna operace, jedna saga.
- **EC-23-04-02 — Reindex již běžícího dokumentu** · Trigger: dokument je Processing · Očekávané chování: 409 `rag.reindex_in_progress` (nebo idempotentní vrácení běžící operace) · Mechanismus: guard na Status==Processing; ConflictException · Severity: P1 · Test: reindex during ingest → 409.
- **EC-23-04-03 — Originální bytes chybí (blob smazán/orphan)** · Trigger: StorageKey neexistuje v `IFileStorage` · Očekávané chování: operation→Failed s reason `rag.source_blob_missing`, žádný částečný přepis (staré chunky zůstávají IsCurrent) · Mechanismus: saga ověří `GetAsync` na začátku, při chybě abandon bez mutace indexu; graceful degradation (žádná tichá polovina) · Severity: P0 · Test: smaž blob → reindex → Failed, staré chunky netknuté.
- **EC-23-04-04 — Embedding provider 429 během re-embed** · Trigger: OpenAI rate-limit · Očekávané chování: saga retry s Retry-After/backoff, ne ztráta; po vyčerpání → dead-letter + operation Failed (recoverable reprocess) · Mechanismus: Wolverine retry-with-cooldown + DLQ (§4 messaging resilience); respekt Retry-After · Severity: P1 · Test: fake gateway vrací 429 → assert retry pak success.
- **EC-23-04-05 — Embedding dimension drift (model mění 3072→jiné)** · Trigger: `targetModel` produkuje jiný počet dimenzí než sloupec `vector(3072)` · Očekávané chování: 400/Failed `rag.embedding_dim_mismatch` PŘED zápisem, žádný korupt index · Mechanismus: validace dim vs schema; targetModel allowlist (`Rag:Embeddings:AllowedModels`) · Severity: P0 · Test: model s jinou dim → Failed, žádné zapsané vektory.
- **EC-23-04-06 — Crash sagy mid-reindex (resume)** · Trigger: Worker spadne po re-chunk, před re-embed · Očekávané chování: saga obnoví z perzistovaného stavu, nedojde k duplicitním chunkům · Mechanismus: EF-persisted saga stav + idempotentní kroky (terminal-state guard) `CreditPurchaseSaga.cs:30` · Severity: P0 · Test: kill+restart Worker → saga dokončí bez duplicit.
- **EC-23-04-07 — Concurrent reindex + delete dokumentu** · Trigger: reindex běží, přijde delete (UC-23-06) · Očekávané chování: delete vyhraje terminálně (saga při dokončení zjistí Status=Deleted → no-op, žádné resurrected chunky) · Mechanismus: saga finalizace čte živý stav dokumentu, guard na Deleted; order-independent · Severity: P0 · Test: race delete↔reindex → žádné chunky po deletu.
- **EC-23-04-08 — Soft-deleted dokument reindex** · Trigger: `documentId` smazaného · Očekávané chování: 404 (filtr skrývá) · Mechanismus: `ISoftDeletable` filter v handleru · Severity: P1 · Test: po delete → reindex 404.
- **EC-23-04-09 — User reindexuje tenant dokument bez permission** · Trigger: běžný user, Scope=Tenant dokument · Očekávané chování: 403 `rag.manage_forbidden` · Mechanismus: `.RequirePermission(RagManage)` pro tenant scope; ForbiddenException · Severity: P0 · Test: user bez RagManage → 403.
- **EC-23-04-10 — Stale index window během reindexu** · Trigger: retrieval dotaz běží během switche IsCurrent · Očekávané chování: retrieval vidí buď staré, nebo nové chunky, nikdy mix/prázdno · Mechanismus: `IsCurrent` switch atomický (ExecuteUpdate guard, ledger-style), retrieval vždy `WHERE IsCurrent` · Severity: P0 · Test: souběžný dotaz během reindexu → vždy nenulové, konzistentní.
- **EC-23-04-11 — Token-window overflow při re-chunk** · Trigger: extrémně dlouhý odstavec · Očekávané chování: chunking ho rozdělí pod model limit, žádné odmítnutí celého dokumentu · Mechanismus: chunker respektuje max-token; edge-case taxonomie token-window · Severity: P2 · Test: oversized paragraph → více chunků.
- **EC-23-04-12 — Audit reindex akce** · Trigger: kdokoliv spustí reindex · Očekávané chování: audit zápis (kdo, kdy, document, targetModel) v `{module}_audit_entries` · Mechanismus: `AuditInterceptor` na SaveChanges změny operation/document; (ExecuteUpdate bypassuje audit → klíčové mutace stavu jdou přes tracked entity) · Severity: P1 · Test: reindex → audit entry existuje.

## UC-23-05 — Reindexace celé kolekce (hromadný re-embed/re-chunk)
- **Actor / role:** tenant-admin (tenant kolekce) | user (vlastní privátní kolekce)
- **Precondition:** Kolekce existuje, obsahuje ≥1 dokument; cílový model v allowlistu.
- **Trigger:** `POST /v1/rag/collections/{collectionId}/reindex` (body `{ targetModel?, onlyFailed?, onlyStale? }`)
- **Main flow:** Endpoint → `ReindexCollectionCommand` → handler → ověří právo → `IOperationStore.CreateAsync` (parent operation) → enumeruje dokumenty (LINQ, RLS-scoped, filtr onlyFailed/onlyStale) → pro každý publikuje `DocumentReindexRequested` přes outbox (fan-out, durable) → commit → **202** + Location na parent operation. Worker zpracovává per-dokument sagy paralelně (competing consumers); parent operation agreguje progress.
- **Postcondition / záruky:** 202; parent operation drží počet total/done/failed; každý dokument idempotentní; částečné selhání → parent=PartiallyFailed (explicit flag, ne tichá polovina).
- **Tenancy / permissions:** tenant kolekce → `RagManage`; privátní → vlastník. RLS limituje dokumenty na viditelné.
- **Reuse / canonical pattern:** `StartDemoOperationHandler.cs:17`; bulk fan-out přes outbox `RegisterUserHandler.cs:22`; operations agregace.
- **Data dotčena:** `documents`, `chunks`, `graph_*`, `operations`, `ingest_sagas` · **Eventy:** N× `DocumentReindexRequested`
- **Priorita:** P1

### Edge cases UC-23-05
- **EC-23-05-01 — Prázdná kolekce** · Trigger: 0 dokumentů · Očekávané chování: 202 + parent operation hned Succeeded (total=0), NE chyba · Mechanismus: handler tolerantní k 0; operation terminalizuje ihned · Severity: P2 · Test: prázdná kolekce → operation Succeeded total 0.
- **EC-23-05-02 — Partial failure (3 z 50 selžou)** · Trigger: 3 dokumenty mají chybějící blob · Očekávané chování: parent=PartiallyFailed, 47 done, 3 failed s důvody; žádné zamlčení · Mechanismus: per-doc saga výsledek agregován; explicit Degraded/Partial (zákon graceful degradation) · Severity: P0 · Test: 3 broken → assert PartiallyFailed + per-doc reasons.
- **EC-23-05-03 — Duplicate collection reindex** · Trigger: dvojí spuštění během běhu · Očekávané chování: druhý vrátí běžící parent operaci, žádné dvojí fan-out · Mechanismus: UNIQUE idempotency key `reindex-collection:{collectionId}` · Severity: P0 · Test: dvojí POST → jeden fan-out.
- **EC-23-05-04 — onlyStale filtr (model drift)** · Trigger: jen dokumenty s jiným než aktuálním modelem · Očekávané chování: reindexují se pouze stale; aktuální se přeskočí · Mechanismus: LINQ `Where(EmbeddingModel != CurrentModel)` · Severity: P1 · Test: mix stale/fresh → jen stale fan-out.
- **EC-23-05-05 — Velká kolekce (DoS/cost guard)** · Trigger: 100k dokumentů · Očekávané chování: per-run cap NEBO throttled fan-out; varování při překročení `Rag:Reindex:MaxBatch`; ne zahlcení provider quota · Mechanismus: cap per run (jako `ReconcileStripeCommand` per-run cap); zbytek další běh · Severity: P1 · Test: nad cap → enqueued v dávkách.
- **EC-23-05-06 — Embedding provider down během dlouhého běhu** · Trigger: OpenAI výpadek uprostřed · Očekávané chování: nedokončené dokumenty dead-letter/retry, parent zůstává Running až do reconcile; po zotavení dokončí · Mechanismus: Wolverine retry+DLQ; stale operation reconcile job (jako `ReconcileStaleOperations`) · Severity: P1 · Test: provider down → retry → eventual completion.
- **EC-23-05-07 — Concurrent delete kolekce během reindexu** · Trigger: delete (UC-23-07) přijde mid-reindex · Očekávané chování: per-doc sagy při finalizaci zjistí kolekci/dokument Deleted → no-op, žádné resurrected chunky · Mechanismus: live-state guard, order-independent handlery · Severity: P0 · Test: race → po deletu žádné chunky.
- **EC-23-05-08 — User reindexuje cizí/tenant kolekci bez práva** · Trigger: user bez RagManage · Očekávané chování: 403 (tenant) / 404 (privátní cizí) · Mechanismus: permission + RLS · Severity: P0 · Test: assert 403/404.
- **EC-23-05-09 — Audit hromadné akce** · Trigger: collection reindex · Očekávané chování: jeden audit zápis na management akci (kolekce, počet, model, actor) · Mechanismus: AuditInterceptor na parent operation · Severity: P2 · Test: audit entry existuje.

## UC-23-06 — Kaskádové smazání dokumentu (chunky + vektory + grafové uzly/hrany + aliasy)
- **Actor / role:** user (vlastní) | tenant-admin (tenant dokument)
- **Precondition:** Dokument existuje a je viditelný.
- **Trigger:** `DELETE /v1/rag/documents/{documentId}`
- **Main flow:** Endpoint → `DeleteDocumentCommand(DocumentId)` → `DeleteDocumentHandler` (mutate) → ověří viditelnost+právo → soft-delete dokument (`ISoftDeletable`, tracked → xmin/audit) → smaže/označí závislosti: `chunks` (DocumentId), grafové příspěvky odvozené z dokumentu (`graph_edges` se SourceDocumentId, případně `graph_nodes` které ztratily veškerou provenienci → degradace uzlu/alias), aliasy bez referencí → přes outbox publikuje `DocumentDeletedIntegrationEvent` → `SaveChangesAndFlushMessagesAsync` (commit). Blob bytes smaže Worker handlerem (`IFileStorage.DeleteAsync`) AFTER commit (kompenzovatelně).
- **Postcondition / záruky:** 200/204; dokument neviditelný; chunky/vektory pryč z retrievalu; grafové hrany odvozené smazány; idempotentní (druhý delete → 204 no-op); append-only audit zachován (NEmazat audit řádky — §4 GDPR/retention).
- **Tenancy / permissions:** user→vlastní; tenant dokument→`RagManage`. RLS chrání před cizím id (→404).
- **Reuse / canonical pattern:** soft-delete + cascade jako `UploadFileHandler.cs:21` (blob+metadata split, kompenzační delete obráceně); outbox `RegisterUserHandler.cs:22`; idempotency catch `DbUpdateException`.
- **Data dotčena:** `documents`, `chunks`, `graph_nodes`, `graph_edges`, `entity_aliases`, `file_objects?`/blob · **Eventy:** `DocumentDeletedIntegrationEvent`
- **Priorita:** P0

### Edge cases UC-23-06
- **EC-23-06-01 — Idempotentní druhý delete** · Trigger: DELETE už smazaného dokumentu · Očekávané chování: 204 no-op (NE 404, aby retry byl bezpečný) — rozhodnutí: idempotentní 204; mechanismus zachová idempotenci · Mechanismus: handler kontroluje již-deleted → vrací success bez další mutace · Severity: P1 · Test: dvojí DELETE → druhý 204.
- **EC-23-06-02 — Cizí documentId (IDOR)** · Trigger: validní cizí Guid · Očekávané chování: 404 · Mechanismus: RLS/global filter → NotFound · Severity: P0 · Test: cross-tenant DELETE → 404.
- **EC-23-06-03 — Concurrent double-delete (race)** · Trigger: dvě paralelní DELETE · Očekávané chování: jeden uspěje, druhý idempotentně 204; žádná chyba, žádná dvojí publikace eventu se škodlivým efektem · Mechanismus: xmin + `ConcurrencyRetryBehavior`; idempotentní handler · Severity: P1 · Test: 2 paralelní → konzistentní finální stav.
- **EC-23-06-04 — Blob delete selže po commitu** · Trigger: `IFileStorage.DeleteAsync` hodí (S3 down) · Očekávané chování: metadata už soft-deleted (správně), blob delete retry přes Worker/DLQ; orphan blob později uklizen reconcile sweepem; NIKDY se nezotaví viditelnost dokumentu · Mechanismus: blob delete v durable handleru AFTER commit (retry/DLQ); orphan sweep job · Severity: P1 · Test: storage down → metadata pryč, blob delete retried.
- **EC-23-06-05 — Sdílený grafový uzel (provenience z více dokumentů)** · Trigger: uzel "ACME Corp" odvozen z dokumentů D1+D2; mažu D1 · Očekávané chování: uzel NEzmizí (D2 ho stále drží), smažou se jen hrany unikátní pro D1; Weight/PropsJson přepočítány · Mechanismus: edge má provenienci na dokument; uzel mazán jen když ztratí veškerou provenienci · Severity: P0 · Test: smaž D1 → uzel žije, D1-only hrany pryč.
- **EC-23-06-06 — Supernode delete (tisíce hran)** · Trigger: dokument přispěl k uzlu s 50k hranami · Očekávané chování: smazání hran bez timeoutu requestu — odsunuto do durable Worker fáze (delete je 202-like nebo batch v handleru s capem) · Mechanismus: pokud velké → publikuj durable cleanup místo inline; zákon long-running 202 · Severity: P1 · Test: supernode → request neblokuje, cleanup async.
- **EC-23-06-07 — Stale retrieval po deletu** · Trigger: dotaz těsně po deletu · Očekávané chování: smazané chunky se NIKDY nevrátí jako citace · Mechanismus: retrieval `WHERE IsCurrent && !IsDeleted`; soft-delete filtr · Severity: P0 · Test: delete pak query → žádná citace ze smazaného.
- **EC-23-06-08 — Delete během běžícího reindexu** · Trigger: viz EC-23-04-07 z druhé strany · Očekávané chování: delete terminálně vyhraje · Mechanismus: live-state guard v sáze · Severity: P0 · Test: race → žádné resurrected chunky.
- **EC-23-06-09 — Audit + PII při deletu** · Trigger: chunky obsahují `[Encrypted][PersonalData]` Content · Očekávané chování: audit zachycuje akci; PII v auditu zůstává crypto-shred-ovatelné, ne plaintext · Mechanismus: AuditInterceptor + PersonalDataProtector; delete nepíše plaintext PII do auditu · Severity: P0 · Test: delete → audit bez plaintext Content.
- **EC-23-06-10 — User maže tenant dokument bez práva** · Trigger: user bez RagManage · Očekávané chování: 403 · Mechanismus: permission gate · Severity: P0 · Test: 403.

## UC-23-07 — Kaskádové smazání celé kolekce
- **Actor / role:** tenant-admin (tenant kolekce) | user (vlastní privátní)
- **Precondition:** Kolekce existuje a je viditelná.
- **Trigger:** `DELETE /v1/rag/collections/{collectionId}?confirm=true`
- **Main flow:** Endpoint → `DeleteCollectionCommand(CollectionId, Confirm)` → handler → ověří právo + `confirm==true` → soft-delete kolekce → publikuje `CollectionDeletionRequested` (durable) → commit → **202** + Location operation. Worker fan-out maže všechny dokumenty kolekce přes `DeleteDocumentCommand` (reuse UC-23-06 — žádná duplicitní logika), poté grafové uzly bez provenience, aliasy, nakonec blob bytes; operation agreguje progress.
- **Postcondition / záruky:** 202; kolekce + obsah postupně mizí; idempotentní; audit zachován; per-dokument delete reuse (DRY).
- **Tenancy / permissions:** tenant→`RagManage`; privátní→vlastník. RLS.
- **Reuse / canonical pattern:** chaining `dispatcher.Send(DeleteDocumentCommand)` (§5 feature chaining); 202 `StartDemoOperationHandler.cs:17`.
- **Data dotčena:** `knowledge_collections`, `documents`, `chunks`, `graph_*`, `entity_aliases`, blob · **Eventy:** `CollectionDeletionRequested`, N× `DocumentDeletedIntegrationEvent`
- **Priorita:** P0

### Edge cases UC-23-07
- **EC-23-07-01 — Chybějící confirm flag** · Trigger: `DELETE` bez `confirm=true` · Očekávané chování: 400 `rag.delete_confirmation_required` (ochrana před nechtěným destruktivním smazáním) · Mechanismus: validator vyžaduje confirm · Severity: P1 · Test: bez confirm → 400.
- **EC-23-07-02 — Prázdná kolekce delete** · Trigger: 0 dokumentů · Očekávané chování: 202, operation rychle Succeeded; kolekce pryč · Mechanismus: fan-out 0 → terminal · Severity: P2 · Test: prázdná → Succeeded.
- **EC-23-07-03 — Duplicate collection delete** · Trigger: dvojí DELETE · Očekávané chování: druhý idempotentně vrací běžící/hotovou operaci, žádné dvojí fan-out · Mechanismus: UNIQUE idempotency `delete-collection:{id}` · Severity: P1 · Test: dvojí → jeden fan-out.
- **EC-23-07-04 — Partial cascade failure** · Trigger: 2 dokumenty mají blob delete chybu · Očekávané chování: kolekce metadata smazána, problematické bloby retried; operation PartiallyFailed dokud cleanup nedoběhne; nikdy tichá nekompletní degradace · Mechanismus: per-doc durable delete + DLQ; explicit Partial flag · Severity: P1 · Test: broken bloby → PartiallyFailed pak Succeeded po retry.
- **EC-23-07-05 — Concurrent ingest do mazané kolekce** · Trigger: nový dokument se nahrává během deletu kolekce · Očekávané chování: ingest buď selže (kolekce Deleted → 404/Conflict), nebo nový dokument je následně také uklizen; žádný orphan v "neexistující" kolekci · Mechanismus: ingest ověřuje kolekci `!IsDeleted`; delete fan-out re-sweep nebo ingest guard · Severity: P1 · Test: race ingest↔delete → žádný orphan.
- **EC-23-07-06 — Cizí/privátní kolekce (IDOR)** · Trigger: cizí collectionId · Očekávané chování: 404 · Mechanismus: RLS/global filter · Severity: P0 · Test: cross-tenant → 404.
- **EC-23-07-07 — User maže tenant kolekci bez RagManage** · Trigger: běžný user · Očekávané chování: 403 · Mechanismus: permission gate · Severity: P0 · Test: 403.
- **EC-23-07-08 — Crash Workeru během fan-out** · Trigger: Worker spadne po smazání 30/100 dokumentů · Očekávané chování: zbylých 70 dokončeno po restartu (durable, idempotentní per-doc delete) · Mechanismus: outbox/inbox dedup; idempotentní DeleteDocument · Severity: P0 · Test: kill+restart → všech 100 nakonec smazáno.

## UC-23-08 — Reprocess selhaného ingestu
- **Actor / role:** user (vlastní) | tenant-admin (tenant dokument)
- **Precondition:** Dokument Status=Failed; `IngestErrorReason` perzistován; originální bytes dostupné.
- **Trigger:** `POST /v1/rag/documents/{documentId}/reprocess`
- **Main flow:** Endpoint → `ReprocessIngestCommand(DocumentId)` → handler → ověří Status==Failed + právo → resetuje stav na Pending → `IOperationStore.CreateAsync` → publikuje `DocumentIngestRequested` (znovu) přes outbox → commit → **202**. Worker spustí `IngestSaga` od začátku (re-chunk, re-embed, graf extrakce), idempotentně dle ingest key.
- **Postcondition / záruky:** 202; při úspěchu Status→Indexed; idempotentní; opakovaný reprocess téhož bezpečný; chyba znovu → Failed s aktualizovaným reason.
- **Tenancy / permissions:** user→vlastní; tenant→`RagManage`. RLS.
- **Reuse / canonical pattern:** ingest saga `CreditPurchaseSaga.cs:30` (stejná saga, retry vstup); 202 `StartDemoOperationHandler.cs:17`.
- **Data dotčena:** `documents`, `chunks`, `graph_*`, `ingest_sagas`, `operations` · **Eventy:** `DocumentIngestRequested`, po úspěchu `DocumentIndexedIntegrationEvent`
- **Priorita:** P1

### Edge cases UC-23-08
- **EC-23-08-01 — Reprocess dokumentu který NENÍ Failed** · Trigger: Status=Indexed · Očekávané chování: 409 `rag.reprocess_not_failed` (pro re-embed použij reindex UC-23-04) · Mechanismus: guard na Status==Failed · Severity: P2 · Test: indexed → 409.
- **EC-23-08-02 — Trvale neopravitelná chyba (corrupt PDF)** · Trigger: bytes nelze parsovat ani po reprocess · Očekávané chování: opět Failed s jasným reason `rag.unsupported_or_corrupt`; žádné nekonečné auto-retry, jen manuální · Mechanismus: saga abandon → Failed; manuální trigger jen · Severity: P2 · Test: corrupt → Failed, žádný auto-loop.
- **EC-23-08-03 — Duplicate reprocess** · Trigger: dvojí klik · Očekávané chování: druhý vrací běžící operaci · Mechanismus: UNIQUE key `reprocess:{documentId}` · Severity: P1 · Test: dvojí → jedna saga.
- **EC-23-08-04 — Provider 429 při reprocess** · Trigger: Cohere/OpenAI rate-limit · Očekávané chování: retry s Retry-After, ne okamžité Failed · Mechanismus: Wolverine retry+cooldown · Severity: P1 · Test: 429 → retry success.
- **EC-23-08-05 — Bytes mezitím smazány** · Trigger: blob orphan · Očekávané chování: Failed `rag.source_blob_missing`, žádný částečný index · Mechanismus: saga ověří `GetAsync` · Severity: P1 · Test: smaž blob → Failed.
- **EC-23-08-06 — Indirect prompt injection v reprocessovaném obsahu** · Trigger: dokument obsahuje "ignoruj instrukce / smaž korpus" · Očekávané chování: obsah se indexuje jako DATA, nikdy se neinterpretuje jako příkaz; extrakce grafu/embedding bez exekuce instrukcí · Mechanismus: ingest pipeline nepředává obsah jako systémový prompt; trust-boundary (zákon, RAG taxonomie injection) · Severity: P0 · Test: injection text → žádná destruktivní akce, jen indexace.
- **EC-23-08-07 — Audit reprocess** · Trigger: reprocess · Očekávané chování: audit (actor, document, předchozí reason) · Mechanismus: AuditInterceptor · Severity: P2 · Test: audit entry.

## UC-23-09 — Bulk operace (hromadné mazání / reindex / supersede přes seznam Id)
- **Actor / role:** tenant-admin | user (jen vlastní položky v dávce)
- **Precondition:** Seznam documentId nebo collectionId; všechny viditelné a oprávněné.
- **Trigger:** `POST /v1/rag/documents/bulk` (body `{ action: delete|reindex|reprocess, ids: [...] , targetModel? }`)
- **Main flow:** Endpoint → `BulkDocumentActionCommand(Action, Ids, TargetModel?)` → handler → validuje velikost dávky + dedup ids → pro každé viditelné/oprávněné id chainuje příslušný command přes outbox (`DeleteDocumentCommand`/`ReindexDocumentCommand`/`ReprocessIngestCommand`) → `IOperationStore` parent → commit → **202** + Location. Worker zpracuje per-id; parent agreguje per-id výsledek (succeeded/failed/skipped-forbidden).
- **Postcondition / záruky:** 202; parent operation drží podrobný per-id výsledek; položky bez práva → skipped (ne celá dávka 403); idempotentní per-id; reuse jednotlivých commandů (DRY).
- **Tenancy / permissions:** každé id ověřeno individuálně (RLS + permission); cizí id → skipped/404 v per-id výsledku, neúnik existence.
- **Reuse / canonical pattern:** chaining §5; fan-out `RegisterUserHandler.cs:22`; per-item reuse jako UC-23-05.
- **Data dotčena:** dle action (documents/chunks/graph/operations) · **Eventy:** N× dle action
- **Priorita:** P2

### Edge cases UC-23-09
- **EC-23-09-01 — Prázdný seznam ids** · Trigger: `ids:[]` · Očekávané chování: 400 `rag.bulk_empty` · Mechanismus: validator min count 1 · Severity: P3 · Test: prázdný → 400.
- **EC-23-09-02 — Dávka nad limit** · Trigger: 10000 ids · Očekávané chování: 400 `rag.bulk_too_large` (limit `Rag:Bulk:MaxIds`) · Mechanismus: validator max count · Severity: P2 · Test: nad limit → 400.
- **EC-23-09-03 — Duplicitní id v dávce** · Trigger: stejné id 3×· Očekávané chování: dedup, akce jen jednou (idempotentní) · Mechanismus: handler `Distinct`; per-id idempotency key · Severity: P2 · Test: duplikáty → jediné provedení.
- **EC-23-09-04 — Mix vlastní + cizí id** · Trigger: user pošle 5 svých + 3 cizí · Očekávané chování: 5 provedeno, 3 skipped (forbidden/not-found) v per-id výsledku; NE celá dávka odmítnuta · Mechanismus: per-id RLS/permission check; explicit per-id status · Severity: P0 · Test: mix → 5 done, 3 skipped, žádný únik cizích dat.
- **EC-23-09-05 — Mix přes scope (privátní + tenant)** · Trigger: user bez RagManage pošle vlastní privátní + tenant dokument · Očekávané chování: privátní provedeno, tenant skipped-forbidden · Mechanismus: per-id permission · Severity: P0 · Test: tenant položka skipped.
- **EC-23-09-06 — Neplatná action** · Trigger: `action:"nuke"` · Očekávané chování: 400 `rag.bulk_action_invalid` · Mechanismus: enum validator · Severity: P3 · Test: 400.
- **EC-23-09-07 — Částečné selhání v dávce** · Trigger: 2 z 50 reindexů selžou (provider) · Očekávané chování: parent=PartiallyFailed, per-id reasons; ne tiché spolknutí · Mechanismus: agregace; graceful degradation explicit · Severity: P1 · Test: 2 fail → PartiallyFailed.
- **EC-23-09-08 — Duplicate bulk submit** · Trigger: dvojí POST stejné dávky · Očekávané chování: druhý vrací existující parent operaci (idempotentní dle hash dávky) · Mechanismus: UNIQUE `bulk:{actionHash}` · Severity: P2 · Test: dvojí → jedna operace.
- **EC-23-09-09 — Audit bulk akce** · Trigger: bulk delete 50 · Očekávané chování: auditní stopa s počty a actor; per-dokument audit přes chainované commandy · Mechanismus: AuditInterceptor na parent + per-id · Severity: P2 · Test: audit entry.

## UC-23-10 — Manuální supersede dokumentu (nahrazení novou verzí)
- **Actor / role:** user (vlastní) | tenant-admin (tenant dokument)
- **Precondition:** Cílový (starý) dokument existuje, Indexed; nahrává se nová verze (nový blob).
- **Trigger:** `POST /v1/rag/documents/{documentId}/supersede` (multipart nová verze NEBO `{ newDocumentId }`)
- **Main flow:** Endpoint → `SupersedeDocumentCommand(OldDocumentId, NewSource)` → handler → ověří právo → nová verze ingestuje (saga, jako UC-23-08) → po úspěšné indexaci nové verze atomicky: starý dokument Status→Superseded, jeho chunky `IsCurrent=false`, nové `IsCurrent=true`, grafové příspěvky přemapovány → publikuje `DocumentSupersededIntegrationEvent` → commit. Vrací **202** + Location (probíhá přes operaci, protože ingest nové verze je durable).
- **Postcondition / záruky:** 202; po dokončení retrieval vidí jen novou verzi; stará dohledatelná jako Superseded (historie/citace zpětně); atomický switch (žádné okno prázdna ani duplikace); idempotentní.
- **Tenancy / permissions:** user→vlastní; tenant→`RagManage`. RLS.
- **Reuse / canonical pattern:** supersede switch `IsCurrent` jako ledger-style atomic guard; saga ingest `CreditPurchaseSaga.cs:30`; 202 `StartDemoOperationHandler.cs:17`.
- **Data dotčena:** `documents`, `chunks`, `graph_*`, `ingest_sagas`, `operations` · **Eventy:** `DocumentSupersededIntegrationEvent`
- **Priorita:** P2

### Edge cases UC-23-10
- **EC-23-10-01 — Nová verze selže při ingestu** · Trigger: nový blob corrupt · Očekávané chování: starý dokument ZŮSTÁVÁ Indecxed/IsCurrent (žádný předčasný supersede), operation Failed · Mechanismus: switch až PO úspěšné indexaci nové verze (atomická finalizace); graceful degradation · Severity: P0 · Test: corrupt nová verze → stará žije.
- **EC-23-10-02 — Supersede již superseded dokumentu** · Trigger: starý je Superseded · Očekávané chování: 409 `rag.already_superseded` (supersedovat se má aktuální verze) · Mechanismus: guard na Status · Severity: P2 · Test: 409.
- **EC-23-10-03 — Atomický switch okno** · Trigger: souběžný retrieval během switche · Očekávané chování: vidí buď starou, nebo novou verzi, nikdy mix/prázdno/obě jako current · Mechanismus: atomický `ExecuteUpdate` switch `IsCurrent`; retrieval `WHERE IsCurrent` · Severity: P0 · Test: souběžný dotaz → vždy přesně jedna verze.
- **EC-23-10-04 — Duplicate supersede** · Trigger: dvojí submit · Očekávané chování: druhý idempotentně vrací operaci · Mechanismus: UNIQUE `supersede:{oldDocId}` · Severity: P1 · Test: dvojí → jedna operace.
- **EC-23-10-05 — Citace na starou verzi po supersede** · Trigger: dřívější odpověď citovala starý chunk · Očekávané chování: historická citace zůstává dohledatelná (Superseded, ne hard-deleted), nové dotazy citují novou · Mechanismus: Superseded zachován, ne smazán; retrieval jen IsCurrent · Severity: P1 · Test: stará citace dohledatelná, nový dotaz nová.
- **EC-23-10-06 — Crash mid-supersede** · Trigger: Worker spadne po indexaci nové, před switchem · Očekávané chování: saga resume dokončí switch idempotentně; žádné dvě current verze · Mechanismus: EF-persisted saga + terminal guard · Severity: P0 · Test: kill+restart → přesně jedna current.
- **EC-23-10-07 — User supersede tenant dokumentu bez práva** · Trigger: bez RagManage · Očekávané chování: 403 · Mechanismus: permission gate · Severity: P0 · Test: 403.
- **EC-23-10-08 — Audit supersede** · Trigger: supersede · Očekávané chování: audit (starý→nový, actor) · Mechanismus: AuditInterceptor · Severity: P2 · Test: audit entry.

## UC-23-11 — Reconcile orphanů a stuck management operací (system job)
- **Actor / role:** system/worker (cron)
- **Precondition:** Jobs host běží; existují potenciální orphan bloby (po nedokončeném deletu), stuck operations (Pending/Running po expiraci), nebo orphan grafové uzly bez provenience.
- **Trigger:** Quartz cron `Modules:Rag:Jobs:ReconcileCron`
- **Main flow:** Jobs host → `RagReconcileJob` (IJob) → `dispatcher.Send(ReconcileRagStateCommand)` → handler (system tenant context) → LINQ identifikuje: (a) operations Pending/Running starší než threshold bez terminalizace → Failed; (b) blob klíče bez `Document` (orphan) → enqueue delete; (c) grafové uzly bez provenience → mark/cleanup; (d) chunky bez živého dokumentu → cleanup. Per-run cap; drift → WARN + metrika `platform.rag.reconcile_fixed`.
- **Postcondition / záruky:** stuck operace terminalizovány; orphany uklizeny postupně; idempotentní; observabilní (counter + WARN).
- **Tenancy / permissions:** běží jako SYSTEM (`SystemTenantContext`/`HttpTenantContext` bez HttpContext); napříč tenanty platformně, ale per-modul data.
- **Reuse / canonical pattern:** `ReconcileStaleOperationsCommand` + `ReconcileStripeCommand` (per-run cap, two-pass, WARN+counter); cron `BillingExpireCreditsJob`.
- **Data dotčena:** `operations`, `documents`, `chunks`, `graph_*`, blob · **Eventy:** interní cleanup commandy
- **Priorita:** P2

### Edge cases UC-23-11
- **EC-23-11-01 — Stuck operation reconcile** · Trigger: operation Running 24h (saga umřela) · Očekávané chování: po threshold → Failed s `rag.operation_stale`; klient pozná konec · Mechanismus: aging jako `ReconcileStaleOperations` · Severity: P1 · Test: aged op → Failed.
- **EC-23-11-02 — Orphan blob (delete neproběhl)** · Trigger: blob bez Document · Očekávané chování: enqueue `IFileStorage.DeleteAsync`; per-run cap · Mechanismus: two-pass sweep · Severity: P2 · Test: orphan → smazán.
- **EC-23-11-03 — False-positive orphan (race s běžícím ingestem)** · Trigger: blob existuje, ale Document se právě vytváří · Očekávané chování: NEsmazat — grace window (jen bloby starší než X min) · Mechanismus: threshold na CreatedAt; konzervativní cleanup · Severity: P0 · Test: čerstvý blob → nesmazán.
- **EC-23-11-04 — Orphan grafový uzel** · Trigger: uzel ztratil veškerou provenienci · Očekávané chování: cleanup uzlu + jeho hran/aliasů · Mechanismus: LINQ join na provenienci · Severity: P2 · Test: orphan uzel → smazán.
- **EC-23-11-05 — Job běží na více Jobs instancích** · Trigger: dva Jobs hosty · Očekávané chování: žádná dvojí destrukce; idempotentní cleanup, leadership/Quartz cluster zabraňuje souběhu · Mechanismus: Quartz clustering + idempotentní operace · Severity: P1 · Test: 2 instance → žádný double-delete.
- **EC-23-11-06 — Per-run cap přetečení** · Trigger: 1M orphanů · Očekávané chování: zpracuje cap, zbytek další běh; WARN + counter · Mechanismus: per-run cap jako `ReconcileStripe` · Severity: P2 · Test: nad cap → zbytek příště.
- **EC-23-11-07 — Reconcile nesmí mazat audit** · Trigger: cleanup chunků/dokumentů · Očekávané chování: append-only audit řádky zachovány (AML/GDPR retention) · Mechanismus: §4 GDPR — nemazat audit/ledger · Severity: P0 · Test: po cleanup audit existuje.

## UC-23-12 — GDPR erasure interakce s management daty (smaž chunky+vektory, retain audit)
- **Actor / role:** system/worker (GDPR fan-out)
- **Precondition:** Přišel `UserErasureRequested` pro uživatele X; X má privátní dokumenty/chunky s `[Encrypted][PersonalData]`.
- **Trigger:** Integration event `UserErasureRequested` (Worker)
- **Main flow:** GDPR fan-out → `RagErasePersonalDataHandler` (impl `IErasePersonalData`) → enumeruje data uživatele X (Scope=User, OwnerUserId=X) → soft-delete/anonymizace dokumentů, hard-delete chunků + vektorů (PII obsah), grafové uzly/hrany odvozené z privátních dokumentů X → DEK shred (crypto-shred) → audit řádky ZACHOVÁ (PII v auditu už crypto-shred-nutelná, po shredu `[erased]`) → commit.
- **Postcondition / záruky:** privátní RAG data X nečitelná/smazaná; audit zachován ale PII `[erased]`; idempotentní (opakovaný erasure no-op); tenant data X (sdílená) NEmazána, jen odanonymizována vazba na X.
- **Tenancy / permissions:** SYSTEM; per-modul `IErasePersonalData` registrovaný v `RegisterServices`.
- **Reuse / canonical pattern:** GDPR §4 (`IExport`/`IErasePersonalData` + crypto-shred); Billing/Notifications eraser jako vzor.
- **Data dotčena:** `documents`, `chunks`, `graph_*`, `subject_keys` (shred), `{module}_audit_entries` (retain) · **Eventy:** žádné nové (reakce na erasure)
- **Priorita:** P0

### Edge cases UC-23-12
- **EC-23-12-01 — Erasure mazání PII chunků** · Trigger: X má privátní chunky s `[Encrypted][PersonalData]` Content · Očekávané chování: chunky+vektory hard-deleted, DEK shred → ciphertext nečitelný · Mechanismus: crypto-shred (`ShredSubjectKey`) + delete; §4 PII at rest · Severity: P0 · Test: po erasure chunk obsah nečitelný/pryč.
- **EC-23-12-02 — Audit zachován po erasure** · Trigger: management akce X v auditu · Očekávané chování: audit řádky zůstanou, PII hodnoty `[erased]` po DEK shredu · Mechanismus: §4 — nemazat audit; PersonalDataProtector po shredu vrací `[erased]` · Severity: P0 · Test: audit existuje, PII `[erased]`.
- **EC-23-12-03 — Tenant (sdílené) dokumenty X** · Trigger: X nahrál dokument Scope=Tenant (firemní) · Očekávané chování: dokument NEsmazán (patří firmě), jen vazba OwnerUserId anonymizována/odpojena · Mechanismus: erasure rozlišuje Scope; sdílená data anonymizována ne smazána · Severity: P1 · Test: tenant doc přežije erasure, owner odpojen.
- **EC-23-12-04 — Idempotentní opakovaný erasure** · Trigger: erasure event doručen 2× (inbox neměl dedup?) · Očekávané chování: druhý no-op, žádná chyba · Mechanismus: inbox dedup UNIQUE MessageId + idempotentní handler · Severity: P1 · Test: dvojí event → konzistentní.
- **EC-23-12-05 — Erasure během běžícího reindexu/ingestu X** · Trigger: saga běží + přijde erasure · Očekávané chování: erasure terminálně vyhraje; saga při finalizaci zjistí data pryč → no-op, žádné resurrected PII chunky · Mechanismus: live-state guard, order-independent · Severity: P0 · Test: race → po erasure žádné PII.
- **EC-23-12-06 — Grafové uzly z privátních dat X** · Trigger: uzel odvozen jen z X privátních dokumentů · Očekávané chování: uzel+hrany smazány; pokud uzel sdílený s tenant daty, zůstává ale anonymizovaný od X · Mechanismus: provenience-based cleanup · Severity: P1 · Test: privátní-only uzel pryč, sdílený žije.
- **EC-23-12-07 — Re-mint guard po erasure** · Trigger: po shredu by mohl pozdní zápis PII obnovit čitelný klíč · Očekávané chování: shredded `subject_keys` tombstone retained permanentně → nelze re-mintnout · Mechanismus: §4 retention sweep tombstone guard · Severity: P0 · Test: post-erasure zápis nevytvoří čitelný klíč.
