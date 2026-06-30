# Oblast 01 — Document ingestion (upload & text extraction)
Tato oblast pokrývá vstupní bránu celé HybridRag pipeline: nahrání zdrojového dokumentu do kolekce (`KnowledgeCollection`), jeho durabilní uložení přes `IFileStorage`, extrakci čistého textu přes `IDocumentTextExtractor` (PdfPig pro PDF, docx, md, txt, html) a posun stavového automatu `Document.Status` od `Uploaded` po předání do chunkování. Mapuje se na build fázi **F1 — Ingest pipeline (upload + extract + IngestSaga skeleton)**, která je předpokladem pro chunkování (Oblast 02), embedding (Oblast 03) a graf (Oblast 04). Ingest je durabilní, idempotentní, per-tenant/per-user izolovaný a degraduje explicitně (nikdy tichá půlka).

## UC-01-01 — Upload dokumentu do kolekce (multipart, happy path)
- **Actor / role:** user (nebo tenant-admin pro tenant-scoped korpus)
- **Precondition:** existuje `KnowledgeCollection` ve scope, který volající vlastní (User korpus s `OwnerUserId == ITenantContext.UserId`, nebo Tenant korpus, na který má volající `rag.ingest`); modul `HybridRag` je entitled pro tenant; volající má platný token.
- **Trigger:** `POST /v1/rag/collections/{collectionId}/documents` (multipart/form-data, pole `file`)
- **Main flow:**
  1. Endpoint `UploadDocumentEndpoint` přijme multipart, ověří `RequestSizeLimit` (≥10 MB cap na úrovni endpointu), namapuje `Request` → `UploadDocumentCommand { CollectionId, FileName, ContentType, Stream }`. Identita NIKDY z body — `OwnerUserId = ITenantContext.UserId`.
  2. `IDispatcher.Send` → pipeline Telemetry → Logging → `UploadDocumentValidator` (allowlist MIME, velikost, prázdné jméno) → ConcurrencyRetry → `UploadDocumentHandler`.
  3. Handler ověří existenci a vlastnictví kolekce (`IReadDbContextFactory` dotaz; cizí/neexistující → `NotFoundException`), zdědí `Scope`/`OwnerUserId` z kolekce.
  4. Vygeneruje server-side `StorageKey` = `{userId:N}/{documentId:N}` (Guid.CreateVersion7), nahraje bytes přes `IFileStorage.PutAsync(key, stream, contentType)`.
  5. Vloží `Document { Status = Uploaded, ContentHash, ByteSize, ... }` do module DbContextu, vypočítá `ContentHash` (SHA-256 obsahu) pro idempotenci/supersede, `PublishAsync(DocumentUploadedIntegrationEvent)` přes `IDbContextOutbox`, `SaveChangesAndFlushMessagesAsync()` = atomický commit (zápis řádku + výlet zprávy do outboxu).
  6. Pokud commit selže po `PutAsync`, kompenzace: `IFileStorage.DeleteAsync(key)` (orphan blob cleanup).
  7. `DocumentUploadedIntegrationEvent` spustí `IngestSaga` (Oblast 05), která řídí `Extracting → Chunking → Embedding → Indexed`.
- **Postcondition / záruky:** 201 Created + `Location: /v1/rag/documents/{id}`, `ApiResponse<DocumentDto>`; `Document.Status = Uploaded`; blob v storage; event v outboxu; žádný orphan blob při selhání commitu.
- **Tenancy / permissions:** Scope dle kolekce. User korpus → `IUserOwned` RLS (`OwnerUserId`). Tenant korpus → `ITenantScoped` filtr + permission `rag.ingest`. RLS: cizí kolekce neviditelná → 404.
- **Reuse / canonical pattern:** `UploadFileHandler.cs:21` (blob+metadata split + compensating delete), `RegisterUserHandler.cs:22` (outbox commit), `FilesModule.cs` (IModule wiring), `Features/Users/RegisterUser/*` (slice tvar).
- **Data dotčena:** `KnowledgeCollection` (read), `Document` (insert), blob via `IFileStorage` · **Eventy:** `DocumentUploadedIntegrationEvent`
- **Priorita:** P0

### Edge cases UC-01-01
- **EC-01-01-01 — Unsupported MIME** · Trigger: upload `application/x-msdownload` / `image/png` mimo allowlist · Očekávané chování: 422/400 `rag.document.unsupported_content_type`, žádný blob, žádný řádek · Mechanismus: `UploadDocumentValidator` allowlist (deny-by-default) à la `UploadFileValidator`; `throw ValidationException` → `GlobalExceptionMiddleware` · Severity: P1 · Test: integration POST png → 422 + DB prázdné + storage prázdné.
- **EC-01-01-02 — Oversized (>cap)** · Trigger: soubor 25 MB při capu 10 MB · Očekávané chování: 413/422 `rag.document.too_large`, stream se nestáhne celý do paměti · Mechanismus: endpoint `RequestSizeLimit` + validator velikostní limit; streamované čtení · Severity: P0 (DoS/paměť) · Test: 413 + 0 alokace orphan blobu.
- **EC-01-01-03 — Prázdný soubor (0 B)** · Trigger: multipart s prázdným streamem · Očekávané chování: 422 `rag.document.empty`, žádný řádek · Mechanismus: validator `ByteSize > 0` · Severity: P1 · Test: 0 B → 422.
- **EC-01-01-04 — Chybějící `file` field / prázdné FileName** · Trigger: multipart bez části nebo `filename=""` · Očekávané chování: 422 `rag.document.file_required` / `rag.document.filename_required` · Mechanismus: validator NotEmpty · Severity: P2 · Test: chybí část → 422.
- **EC-01-01-05 — IDOR na cizí kolekci** · Trigger: `collectionId` patřící jinému uživateli/tenantovi · Očekávané chování: 404 (ne 403 — neprozradit existenci) · Mechanismus: RLS na `IUserOwned`/`ITenantScoped` → read vrátí prázdno → `NotFoundException`; Zákon §10 (identita z tokenu) · Severity: P0 · Test: user B uploaduje do kolekce usera A → 404.
- **EC-01-01-06 — Cross-tenant kolekce** · Trigger: token tenantu T2, kolekce tenantu T1 · Očekávané chování: 404, žádný leak · Mechanismus: `ITenantScoped` global filter (`TenantId == claim`) · Severity: P0 · Test: cross-tenant → 404 + audit nezapsán do cizího tenantu.
- **EC-01-01-07 — Storage `PutAsync` selže (S3 5xx/timeout)** · Trigger: blob store nedostupný · Očekávané chování: žádný `Document` řádek (nepřišel commit), 503/500 `rag.storage.unavailable`, retry-safe · Mechanismus: `PutAsync` před DB commitem; výjimka → middleware; žádný orphan řádek · Severity: P0 · Test: fake storage hodí → 503 + DB prázdné.
- **EC-01-01-08 — DB commit selže po `PutAsync` (orphan blob)** · Trigger: `SaveChangesAndFlushMessagesAsync` vyhodí · Očekávané chování: kompenzační `DeleteAsync(key)`, žádný orphan blob, 500 · Mechanismus: try/catch kolem commitu s compensating delete (`UploadFileHandler.cs:21`) · Severity: P0 · Test: injektuj commit fail → ověř `DeleteAsync` volán.
- **EC-01-01-09 — Content-Type vs reálný obsah (spoofed MIME)** · Trigger: `.exe` přejmenovaný na `report.pdf` s `application/pdf` · Očekávané chování: upload projde (MIME header v allowlistu), ALE extrakce ho odhalí a označí `Failed` magic-byte kontrolou · Mechanismus: `IDocumentTextExtractor` validuje magic bytes (PdfPig odmítne ne-PDF); sniff content type při extrakci, ne jen header · Severity: P1 · Test: fake PDF s exe payloadem → `Status=Failed`, `FailureReason=content_type_mismatch`.
- **EC-01-01-10 — Path traversal v FileName** · Trigger: `FileName = "../../etc/passwd"` · Očekávané chování: FileName se NIKDY nepoužije jako storage key; uloží se jen jako display metadata (sanitizováno) · Mechanismus: server-generated `StorageKey`, `StorageKey.Validate` guard (Zákon — key = `{userId:N}/{id:N}`) · Severity: P0 · Test: traversal filename → key zůstane GUID-based.
- **EC-01-01-11 — Souběžný upload téhož obsahu (idempotency race)** · Trigger: 2× stejný `ContentHash` do téže kolekce paralelně · Očekávané chování: deduplikace přes UNIQUE `(collection_id, content_hash, is_current)` — druhý buď supersede no-op nebo 200 s existujícím id · Mechanismus: UNIQUE index + catch `DbUpdateException` → vrať existující (RegisterUser idempotency vzor) · Severity: P1 · Test: 20-way paralelní stejný soubor → 1 `Document`.
- **EC-01-01-12 — Rate-limit / DoS na upload** · Trigger: 1000 uploadů/min · Očekávané chování: 429 + `Retry-After` · Mechanismus: partitioned rate limiter (per-user `NameIdentifier`), policy `rag-ingest` · Severity: P1 · Test: burst → 429.
- **EC-01-01-13 — Token bez `rag.ingest` na Tenant korpus** · Trigger: běžný user uploaduje do tenant-scoped kolekce · Očekávané chování: 403 `forbidden` (kolekce je viditelná v tenantu, ale write gated) · Mechanismus: `.RequirePermission(PlatformPermissions.RagIngest)` na endpointu pro tenant scope; rozlišení 403 vs 404 — tenant kolekce JE viditelná, takže 403 · Severity: P1 · Test: user bez permission → 403.
- **EC-01-01-14 — PII v názvu/obsahu** · Trigger: soubor obsahuje rodná čísla · Očekávané chování: blob bytes uloženy jak jsou (storage je interní), ale extrahovaný text → `Chunk.Content` je `[Encrypted][PersonalData]` (Oblast 02); `Document` je `IDataSubject` · Mechanismus: `[Encrypted]` interceptor na chunk úrovni; `Document` IDataSubject pro crypto-shred · Severity: P1 · Test: ověř že chunk content je `penc:v2` envelope.
- **EC-01-01-15 — Zrušené spojení během uploadu** · Trigger: klient přeruší multipart stream · Očekávané chování: žádný řádek, žádný (nebo uklizený) blob; `CancellationToken` respektován · Mechanismus: ct propagace; commit neproběhl · Severity: P2 · Test: cancel mid-stream → DB prázdné.

## UC-01-02 — Text extrakce z dokumentu (worker, IDocumentTextExtractor)
- **Actor / role:** system/worker
- **Precondition:** existuje `Document` ve stavu `Uploaded`; `DocumentUploadedIntegrationEvent` doručen; `IngestSaga` ve fázi `Extracting`.
- **Trigger:** durable Wolverine message `ExtractDocumentTextCommand` (vydaná sagou) → Worker handler
- **Main flow:**
  1. Worker handler (public `Handle(ExtractDocumentTextCommand, IDispatcher, ct)`) dispatchne interní command.
  2. Handler načte `Document` (system tenant context — worker), atomicky posune `Uploaded → Extracting` (xmin guard).
  3. Stáhne bytes přes `IFileStorage.GetAsync(StorageKey)`, vybere extraktor podle `ContentType` (`IDocumentTextExtractor` factory: PdfPig / docx / md / html / txt).
  4. Extrahuje plain text + per-page/section offsety (pro pozdější citace), normalizuje encoding na UTF-8.
  5. Uloží `Document.ExtractedText` (nebo do staging blob klíče `{key}.txt`) + `Document.Status = Chunking`, `PublishAsync(DocumentTextExtractedIntegrationEvent)`, `SaveChangesAndFlushMessagesAsync`.
- **Postcondition / záruky:** `Status = Chunking`; extrahovaný text persistován; event do outboxu; idempotentní (re-doručení command → no-op pokud už `Chunking+`).
- **Tenancy / permissions:** worker = system context, ale entity nesou `Scope/OwnerUserId/TenantId` → izolace zachována přes stamping; žádná HTTP identita.
- **Reuse / canonical pattern:** `ProvisionCreditAccountHandler.cs:13` (worker handler shell), `RunDemoOperationHandler` (durable work + status transition), `IFileStorage.GetAsync` (Ports.cs:166).
- **Data dotčena:** `Document` (update), blob read · **Eventy:** `DocumentTextExtractedIntegrationEvent`
- **Priorita:** P0

### Edge cases UC-01-02
- **EC-01-02-01 — Poškozený/nečitelný PDF** · Trigger: truncated PDF, PdfPig vyhodí · Očekávané chování: `Status = Failed`, `FailureReason = extract_corrupt`, žádný retry storm po max pokusech → dead-letter, explicitní stav (NE ticho) · Mechanismus: catch v handleru → set Failed; Wolverine retry-with-cooldown → DLQ; Zákon „graceful degradation = explicit flag" · Severity: P0 · Test: corrupt PDF → `Status=Failed` + `platform.rag.extract_failed` metrika.
- **EC-01-02-02 — Scan PDF bez textové vrstvy (OCR-needed)** · Trigger: PDF jen obrázky, PdfPig vrátí ~0 znaků · Očekávané chování: `Status = NeedsOcr` (nebo `Failed` s `FailureReason=no_text_layer` pokud OCR není entitled), explicitní, neposílat prázdný dokument dál · Mechanismus: handler detekuje `extractedChars < threshold` → větvení; OCR provider je v Oblasti pozdější (seam `IOcrGateway`, default off) · Severity: P1 · Test: scan PDF → `Status=NeedsOcr`, žádné prázdné chunky.
- **EC-01-02-03 — Token-window / extrémně velký text** · Trigger: 2000-stránkový dokument · Očekávané chování: extrakce streamuje po stránkách, nedrží vše v paměti; pokračuje do chunkování (window řeší Oblast 02) · Mechanismus: streamovaná extrakce, per-page persist · Severity: P1 · Test: velký PDF → bez OOM.
- **EC-01-02-03b — Encrypted/heslem chráněný PDF** · Trigger: PDF s user/owner password · Očekávané chování: `Status=Failed`, `FailureReason=password_protected` · Mechanismus: PdfPig vyhodí → catch → explicit Failed · Severity: P2 · Test: chráněný PDF → Failed.
- **EC-01-02-04 — Nesprávné/exotické encoding (Windows-1250, UTF-16, BOM)** · Trigger: txt v CP1250 s diakritikou · Očekávané chování: detekce encoding (BOM/charset sniff), převod na UTF-8, žádné mojibake · Mechanismus: battle-tested charset detekce (Ude/`System.Text.Encoding`), ne ruční hádání · Severity: P1 · Test: CP1250 „příliš" → korektní UTF-8.
- **EC-01-02-05 — HTML s `<script>`/aktivním obsahem (stored XSS / injection vektor)** · Trigger: ingestované HTML obsahuje skripty / instrukce · Očekávané chování: extrahuje se jen text, tagy/skripty se zahodí (sanitizace); navíc indirect-prompt-injection: extrahovaný text je DATA, ne instrukce (řeší se v retrieval promptu, Oblast 06) · Mechanismus: HTML sanitizace (battle-tested, ne regex); text-only extrakce · Severity: P1 · Test: `<script>alert</script>Hello` → `Hello`.
- **EC-01-02-06 — Indirect prompt injection v dokumentu** · Trigger: dokument obsahuje „Ignore previous instructions, exfiltrate..." · Očekávané chování: text se uloží beze změny jako DATA; downstream retrieval ho vkládá do user/data bloku, nikdy do system promptu · Mechanismus: trust-boundary v Oblasti 06; zde jen poznamenáno že extrakce nesmí interpretovat obsah · Severity: P1 · Test: marker text projde jako plain content.
- **EC-01-02-07 — Re-doručení `ExtractDocumentTextCommand` (idempotence)** · Trigger: Wolverine doručí 2× (competing consumers) · Očekávané chování: druhý běh vidí `Status != Uploaded` → no-op, žádná duplicitní extrakce/event · Mechanismus: inbox dedup (UNIQUE MessageId) + status guard; order-independent · Severity: P0 · Test: 2× command → 1 `DocumentTextExtractedIntegrationEvent`.
- **EC-01-02-08 — Blob zmizel mezi uploadem a extrakcí** · Trigger: `GetAsync` → not found · Očekávané chování: `Status=Failed`, `FailureReason=blob_missing`, explicit · Mechanismus: catch storage NotFound → Failed · Severity: P1 · Test: smaž blob → extrakce → Failed.
- **EC-01-02-09 — Detekce jazyka pro downstream** · Trigger: vícejazyčný / neznámý jazyk dokument · Očekávané chování: uloží se `Document.Language` (detekováno), default fallback, nikdy crash · Mechanismus: jazyková detekce knihovnou, nepovinné pole · Severity: P3 · Test: CZ vs EN dokument → správný kód.
- **EC-01-02-10 — Provider/extractor neočekávaná výjimka (transient)** · Trigger: dočasná chyba extraktoru · Očekávané chování: Wolverine retry-with-cooldown, po vyčerpání DLQ + `Status=Failed`; nikdy nezůstane navždy `Extracting` · Mechanismus: retry/DLQ + reconcile stuck (UC-01-06 / Oblast 05) · Severity: P0 · Test: 2 transient + 1 success → `Chunking`.
- **EC-01-02-11 — Soft-deleted dokument doručen do extrakce** · Trigger: dokument smazán mezi upload a extract · Očekávané chování: handler vidí `ISoftDeletable` filtr → no-op, žádné chunky · Mechanismus: global soft-delete filter; status guard · Severity: P1 · Test: delete pak extract command → no-op.

## UC-01-03 — Re-upload téhož dokumentu (supersede vs duplicate)
- **Actor / role:** user / tenant-admin
- **Precondition:** v kolekci už existuje `Document` se stejným `FileName` nebo `ContentHash`.
- **Trigger:** `POST /v1/rag/collections/{collectionId}/documents` se stejným souborem nebo `PUT /v1/rag/documents/{id}/content` (replace)
- **Main flow:**
  1. Handler spočítá `ContentHash`, dotáže se na existující `is_current=true` dokument se stejným hashem v kolekci.
  2. **Identický obsah (hash shoda):** idempotentní — vrať existující `Document` (200), žádný nový blob/řádek/reindex.
  3. **Stejné FileName, jiný obsah (nová verze):** vytvoř nový `Document` (nový `documentId`, `is_current=true`), starý označ `is_current=false` (supersede), spusť reindex; staré chunky pro starou verzi se po reindexu označí `IsCurrent=false` / smažou (stale index guard, Oblast 02/03).
  4. `PublishAsync(DocumentSupersededIntegrationEvent)` nese `oldDocumentId` + `newDocumentId` → downstream invaliduje staré vektory/grafové uzly.
- **Postcondition / záruky:** vždy jen jedna `is_current` verze; staré chunky/vektory nezůstanou v retrievalu; idempotence při identickém obsahu.
- **Tenancy / permissions:** stejné jako UC-01-01 (RLS/permission); supersede smí jen vlastník/tenant s `rag.ingest`.
- **Reuse / canonical pattern:** UNIQUE idempotency + catch `DbUpdateException` (`RegisterUserHandler.cs:22`), `Chunk.IsCurrent` flag (entity model).
- **Data dotčena:** `Document` (insert + update is_current), `Chunk` (invalidace) · **Eventy:** `DocumentSupersededIntegrationEvent`
- **Priorita:** P1

### Edge cases UC-01-03
- **EC-01-03-01 — Identický obsah (hash match) = no-op** · Trigger: bit-identický re-upload · Očekávané chování: 200 + stejný `documentId`, žádný reindex, žádný nový blob · Mechanismus: `ContentHash` lookup → return existing; idempotence · Severity: P1 · Test: 2× stejný soubor → 1 řádek, žádný 2. embedding.
- **EC-01-03-02 — Stale index po supersede** · Trigger: nová verze nahrazuje starou · Očekávané chování: staré chunky/vektory/grafové uzly označeny `IsCurrent=false` (nebo smazány) PŘED zveřejněním nových; retrieval nikdy nesmí vracet mix verzí · Mechanismus: `DocumentSupersededIntegrationEvent` → invalidační command; `IsCurrent` filtr v retrievalu · Severity: P0 · Test: supersede → query nevrací staré chunky.
- **EC-01-03-03 — Souběžné dvě nové verze (race)** · Trigger: 2 paralelní re-uploady jiného obsahu · Očekávané chování: serializace přes UNIQUE `(collection_id, file_name, is_current)` partial index → poslední vyhrává deterministicky, žádné dvě `is_current` · Mechanismus: UNIQUE partial index + catch `DbUpdateException` + xmin · Severity: P0 · Test: 2-way race → právě 1 `is_current`.
- **EC-01-03-04 — Supersede během rozpracované ingestace předchozí verze** · Trigger: stará verze ještě `Embedding`, přijde nová · Očekávané chování: stará saga se bezpečně abandonuje/dokončí, nové chunky nepřekrývají staré; žádný half-index · Mechanismus: saga terminal-state guard (`CreditPurchaseSaga.cs:30`), `IngestSaga` koreluje na `documentId` · Severity: P1 · Test: rapid supersede → konzistentní finální index.
- **EC-01-03-05 — Reindex selže po supersede (částečný stav)** · Trigger: embedding nové verze spadne · Očekávané chování: stará verze NESMÍ být odstraněna dřív, než je nová `Indexed`; při selhání zůstane stará dostupná, nová `Failed` (no silent gap) · Mechanismus: invalidace staré až po úspěšném indexu nové; explicit Failed · Severity: P0 · Test: nová verze fail → stará stále v retrievalu.
- **EC-01-03-06 — Embedding model drift mezi verzemi** · Trigger: nová verze embedována jiným modelem/dim než stará · Očekávané chování: reindex přepočítá vektory novým modelem; nemíchat dimenze v jedné kolekci · Mechanismus: `Chunk.Embedding vector(3072)` fixní dim + model verze na kolekci (Oblast 03) · Severity: P1 · Test: drift → konzistentní dim.

## UC-01-04 — Dotaz na stav ingestace dokumentu (status lifecycle)
- **Actor / role:** user / tenant-admin
- **Precondition:** `Document` existuje a patří volajícímu (RLS).
- **Trigger:** `GET /v1/rag/documents/{id}` a `GET /v1/rag/collections/{collectionId}/documents?status=&page=`
- **Main flow:**
  1. Endpoint → `GetDocumentStatusQuery` / `ListDocumentsQuery` → read-only handler (`IReadDbContextFactory`, NIKDY transakce/event).
  2. Vrátí `DocumentDto { Id, FileName, Status, FailureReason?, ChunkCount?, Language?, CreatedAt, UpdatedAt }`; list je paginovaný (`totalCount`).
  3. Stavy: `Uploaded → Extracting → Chunking → Embedding → GraphExtracting → Indexed`; chybové: `Failed`, `NeedsOcr`, `PartiallyIndexed`.
- **Postcondition / záruky:** 200 + aktuální stav; žádná mutace; cizí id → 404.
- **Tenancy / permissions:** Scope dle dokumentu; RLS owner-scoped; tenant list gated `rag.read`.
- **Reuse / canonical pattern:** `GetProfileHandler.cs:12` (read query), `GetOperationStatusEndpoint` (status + RLS 404).
- **Data dotčena:** `Document` (read) · **Eventy:** žádné
- **Priorita:** P1

### Edge cases UC-01-04
- **EC-01-04-01 — Cizí dokument id** · Trigger: `id` jiného uživatele/tenantu · Očekávané chování: 404 · Mechanismus: RLS → prázdno → `NotFoundException` (`GetOperationStatusEndpoint` vzor) · Severity: P0 · Test: cross-user GET → 404.
- **EC-01-04-02 — Stav `Failed` musí nést důvod** · Trigger: GET na failed dokument · Očekávané chování: `Status=Failed` + lokalizovaný `FailureReason` (errorCode), ne prázdné · Mechanismus: `Document.FailureReason` + resx · Severity: P1 · Test: failed → ne-null reason v en/cs.
- **EC-01-04-03 — `PartiallyIndexed` explicitně viditelný** · Trigger: část chunků embedována, část selhala · Očekávané chování: stav `PartiallyIndexed` + `indexedChunks/totalChunks`, nikdy se netváří `Indexed` · Mechanismus: explicit degradace flag (Zákon „nikdy tichá půlka") · Severity: P0 · Test: 8/10 chunků → `PartiallyIndexed`.
- **EC-01-04-04 — Soft-deleted dokument** · Trigger: GET na smazaný · Očekávané chování: 404 (filtr `ISoftDeletable`) · Mechanismus: global query filter · Severity: P2 · Test: delete → GET → 404.
- **EC-01-04-05 — List paging / řazení** · Trigger: kolekce s 5000 dokumenty · Očekávané chování: paginovaný `totalCount`, deterministické řazení (CreatedAt desc), žádné N+1 · Mechanismus: read factory + `Skip/Take` + `$orderby` · Severity: P2 · Test: page 2 stabilní.
- **EC-01-04-06 — Stuck stav (navždy `Extracting`)** · Trigger: worker spadl uprostřed · Očekávané chování: reconcile job po prahu označí stuck → `Failed` (re-ingest možný); GET ho ukáže Failed, ne věčně Extracting · Mechanismus: `ReconcileStaleOperationsCommand` analog pro ingest (Oblast 05) · Severity: P1 · Test: stuck > threshold → Failed.

## UC-01-05 — Ingest existujícího Files-modul souboru (link / by-reference)
- **Actor / role:** user / tenant-admin
- **Precondition:** uživatel už má `FileObject` v Files modulu (nahraný dřív) a chce ho zaindexovat do RAG bez re-uploadu.
- **Trigger:** `POST /v1/rag/collections/{collectionId}/documents/from-file` body `{ fileId }`
- **Main flow:**
  1. Handler ověří vlastnictví `fileId` — NE přes JOIN do Files Core (Zákon §3), ale přes Files **query/contract** (`IDispatcher.Query(GetFileMetadataQuery)` z `Files.Contracts`) nebo přes `IFileStorage` re-stream do vlastního RAG blobu.
  2. Zkopíruje/odkáže bytes, vytvoří `Document` se Scope/Owner z kolekce, `Status=Uploaded`, publikuje `DocumentUploadedIntegrationEvent`.
  3. RAG si drží VLASTNÍ kopii/StorageKey (denormalizace) — nezávisí na životním cyklu Files objektu.
- **Postcondition / záruky:** 201; RAG `Document` nezávislý na Files; žádný cross-module JOIN.
- **Tenancy / permissions:** vlastnictví fileId přes token (`ITenantContext.UserId`), ne z body; cizí fileId → 404.
- **Reuse / canonical pattern:** cross-module query přes Contracts (CLAUDE.md §5), `IFileStorage` (Ports.cs:166).
- **Data dotčena:** `Document` (insert), Files čte přes Contracts · **Eventy:** `DocumentUploadedIntegrationEvent`
- **Priorita:** P2

### Edge cases UC-01-05
- **EC-01-05-01 — Cizí fileId (IDOR)** · Trigger: `fileId` jiného uživatele · Očekávané chování: 404; Files RLS i RAG ověření brání leaku · Mechanismus: Files query RLS-scoped + identita z tokenu · Severity: P0 · Test: cizí fileId → 404.
- **EC-01-05-02 — fileId smazán ve Files mezi ověřením a kopií** · Trigger: race delete · Očekávané chování: explicit `Failed`/`NotFound`, žádný prázdný Document · Mechanismus: `GetAsync` NotFound → catch · Severity: P1 · Test: delete pak ingest → 404/Failed.
- **EC-01-05-03 — Files objekt v nepodporovaném MIME** · Trigger: `fileId` ukazuje na png · Očekávané chování: 422 `unsupported_content_type` (stejný allowlist) · Mechanismus: validator na ContentType z Files metadat · Severity: P1 · Test: png file → 422.
- **EC-01-05-04 — Cross-module reference porušení** · Trigger: pokušení JOINovat `file_objects` · Očekávané chování: zakázáno — jen přes `Files.Contracts` query / Id · Mechanismus: ArchUnitNET boundary rule · Severity: P0 · Test: arch test zelený (žádný ref na Files Core).
- **EC-01-05-05 — Duplicitní ingest téhož fileId** · Trigger: 2× from-file stejný soubor do kolekce · Očekávané chování: idempotence dle `ContentHash` (UC-01-03) → 1 Document · Mechanismus: UNIQUE hash · Severity: P2 · Test: 2× → 1 řádek.

## UC-01-06 — Reconcile zaseknutých ingestů (cron, self-healing)
- **Actor / role:** system (Jobs host, Quartz cron)
- **Precondition:** existují `Document` ve stavu `Uploaded/Extracting/Chunking/Embedding` starší než `Rag:Ingest:StuckThreshold`, jejichž durable práce neterminalizovala.
- **Trigger:** Quartz cron `Modules:HybridRag:Jobs:ReconcileStuckIngestCron` → `IJob` → `ReconcileStuckIngestCommand`
- **Main flow:**
  1. Job (thin) dispatchne command (pure publisher, žádná logika v jobu).
  2. Handler najde stuck dokumenty (LINQ `WHERE Status in (...) AND UpdatedAt < now - threshold`), per řádek: buď re-queue durable work (outbox) nebo po max stáří → `Failed` (`FailureReason=ingest_timeout`).
  3. Emituje `platform.rag.ingest_stuck` counter + WARN log; cap per běh.
- **Postcondition / záruky:** žádný věčně-pending dokument; explicit Failed; observabilní.
- **Tenancy / permissions:** system context (worker/jobs); per-řádek izolace zachována stampingem.
- **Reuse / canonical pattern:** `ReconcileStaleOperationsCommand` + `BillingExpireCreditsJob` (cron→command), `MessagingHealthJob` (gauge/WARN).
- **Data dotčena:** `Document` (update), outbox · **Eventy:** případně re-publikace `ExtractDocumentTextCommand`
- **Priorita:** P1

### Edge cases UC-01-06
- **EC-01-06-01 — False-positive (práce právě dobíhá)** · Trigger: dokument těsně pod prahem · Očekávané chování: práh dostatečně velký; reconcile nezabije běžící ingest; re-queue je idempotentní (status guard) · Mechanismus: threshold + idempotentní handlery · Severity: P1 · Test: běžící ingest neoznačen Failed.
- **EC-01-06-02 — Reconcile běží na více Jobs instancích** · Trigger: 2 Jobs pody · Očekávané chování: žádná dvojí akce — Quartz cluster/lock nebo idempotentní update (`ExecuteUpdate WHERE Status=...`) · Mechanismus: atomic conditional update + Quartz clustering · Severity: P1 · Test: 2 instance → 1 efektivní transition.
- **EC-01-06-03 — Cap per běh (DoS ochrana)** · Trigger: 100k stuck řádků · Očekávané chování: zpracuje se jen N per běh, zbytek příště; žádné zahlcení · Mechanismus: `Take(cap)` · Severity: P2 · Test: >cap → batchováno.
- **EC-01-06-04 — Re-queue saga, která už mezitím dokončila** · Trigger: late completion vs reconcile · Očekávané chování: status guard / saga `NotFound` static → žádný regres do nižšího stavu · Mechanismus: order-independent guard (`CreditPurchaseSaga.cs:30`) · Severity: P1 · Test: late + reconcile → konzistentní.

## UC-01-07 — GDPR export & erasure ingestovaných dokumentů
- **Actor / role:** system/worker (vyvolá Gdpr modul)
- **Precondition:** uživatel požádal o export nebo erasure; HybridRag je registrovaný `IExportPersonalData` + `IErasePersonalData`.
- **Trigger:** `UserErasureRequested` / export fan-out event (Gdpr Worker)
- **Main flow:**
  1. `HybridRagExporter` (impl `IExportPersonalData`) shromáždí dokumenty/chunky daného subjektu (jeho `OwnerUserId`), vrátí metadata + dešifrovaný obsah do exportního balíku (před erasure).
  2. `HybridRagEraser` (impl `IErasePersonalData`): smaže/odšifruje user-owned `Document` blob (`IFileStorage.DeleteAsync`), označí chunky, smaže vektory a grafové uzly subjektu; PII v auditních záznamech se nemaže fyzicky — crypto-shred DEK (`Document : IDataSubject`).
  3. Audit řádky zůstávají (retence), ale `[Encrypted][PersonalData]` hodnoty se stanou `[erased]` po shredu klíče.
- **Postcondition / záruky:** chunky+vektory+grafové uzly subjektu pryč/nečitelné; audit zachován (anonymizovaný); blob smazán.
- **Tenancy / permissions:** Gdpr volá přes porty, nikdy nesahá na RAG Core přímo.
- **Reuse / canonical pattern:** `IExport/IErasePersonalData` registrace v `RegisterServices`, `UserErasureRequested` fan-out, crypto-shred `CryptoShredder`/`SubjectKey`.
- **Data dotčena:** `Document`, `Chunk`, `GraphNode`, blob · **Eventy:** konzumuje `UserErasureRequested`
- **Priorita:** P0

### Edge cases UC-01-07
- **EC-01-07-01 — Export PŘED erase pořadí** · Trigger: erase přijde dřív než export dokončí · Očekávané chování: export musí běžet před shredem klíče, jinak `[erased]`; pořadí garantováno Gdpr orchestrací · Mechanismus: Gdpr fan-out pořadí; HybridRag jen reaguje · Severity: P1 · Test: export pak erase → export má plný text.
- **EC-01-07-02 — Tenant-scoped dokument vs user erasure** · Trigger: user smazán, ale dokument je Tenant-scoped (firemní korpus) · Očekávané chování: tenant dokument se NEMAŽE jen proto, že jeden user odešel; smažou se jen `Scope=User, OwnerUserId=subject` data · Mechanismus: erasure filtruje `Scope=User AND OwnerUserId` · Severity: P0 · Test: erasure usera → tenant korpus nedotčen.
- **EC-01-07-03 — Erase chunků nesmí nechat orphan vektor v indexu** · Trigger: smazání chunku, ale vektor/grafový uzel zůstane v retrievalu · Očekávané chování: smazat/označit i embeddings + graph nodes + edges téhož subjektu atomicky · Mechanismus: kaskáda v eraseru (po Id, žádné navigace) · Severity: P0 · Test: po erase query nevrací subjektovy chunky.
- **EC-01-07-04 — Idempotentní erase (re-doručení)** · Trigger: `UserErasureRequested` 2× · Očekávané chování: druhý běh no-op, žádná chyba · Mechanismus: inbox dedup + idempotentní delete · Severity: P1 · Test: 2× erase → bez výjimky.
- **EC-01-07-05 — Audit PII po erasure čitelná adminem?** · Trigger: admin forensic read po erasure · Očekávané chování: `[erased]` (DEK shredded), předtím čitelné s `AuditRead` permission · Mechanismus: `IPersonalDataProtector` + shred · Severity: P1 · Test: erase → admin audit read → `[erased]`.

## UC-01-08 — Vytvoření kolekce před ingestem (race-safe)
- **Actor / role:** user / tenant-admin
- **Precondition:** žádná / volající chce nový korpus.
- **Trigger:** `POST /v1/rag/collections` body `{ name, scope }`
- **Main flow:**
  1. `CreateCollectionCommand` → validator (name NotEmpty, scope ∈ {Tenant,User}, Tenant scope vyžaduje `rag.manage`).
  2. Handler vloží `KnowledgeCollection` se `Scope`, `OwnerUserId = UserId` (pro User scope) / null (Tenant), idempotence přes UNIQUE `(tenant_id, scope, owner_user_id, name)`.
  3. 201 + Location.
- **Postcondition / záruky:** kolekce existuje; UNIQUE jméno ve scope; žádné duplicitní kolekce při race.
- **Tenancy / permissions:** Tenant scope gated `rag.manage`; User scope = self.
- **Reuse / canonical pattern:** `RegisterUserHandler.cs:22` (UNIQUE + catch DbUpdateException), slice tvar `Features/Users/RegisterUser/*`.
- **Data dotčena:** `KnowledgeCollection` (insert) · **Eventy:** `CollectionCreatedIntegrationEvent` (volitelně)
- **Priorita:** P2

### Edge cases UC-01-08
- **EC-01-08-01 — Race create stejného jména** · Trigger: 2 paralelní create téhož jména ve scope · Očekávané chování: 1 uspěje, druhý 409 `rag.collection.name_taken` (ne 500) · Mechanismus: UNIQUE index + catch `DbUpdateException` → ConflictException · Severity: P1 · Test: 2-way race → 1×201, 1×409.
- **EC-01-08-02 — User scope s pokusem nastavit cizí OwnerUserId** · Trigger: body obsahuje `ownerUserId` jiného usera · Očekávané chování: ignorováno — owner vždy z tokenu · Mechanismus: identita z `ITenantContext.UserId` (Zákon §10) · Severity: P0 · Test: cizí owner v body → kolekce patří volajícímu.
- **EC-01-08-03 — Tenant scope bez `rag.manage`** · Trigger: běžný user vytvoří tenant korpus · Očekávané chování: 403 · Mechanismus: `.RequirePermission` · Severity: P1 · Test: bez perm → 403.
- **EC-01-08-04 — Neplatný scope enum** · Trigger: `scope="global"` · Očekávané chování: 422 `rag.collection.invalid_scope` · Mechanismus: validator enum · Severity: P2 · Test: bad scope → 422.
- **EC-01-08-05 — Modul HybridRag není entitled pro tenant** · Trigger: tenant bez entitlementu volá endpoint · Očekávané chování: 404 (ModuleEntitlementGuard) · Mechanismus: `ModuleEntitlementGuard` → 404 (multitenancy doc) · Severity: P1 · Test: neentitled tenant → 404.


---

## Doplňky z completeness review
- **EC-01-01-16 — Malware/virus v nahraném souboru** · Trigger: uživatel nahraje soubor s embedded malware (PDF s exploit payloadem, makro-DOCX) v POVOLENÉM MIME · Očekávané chování: blob projde MIME allowlistem, ale před extrakcí/zpřístupněním proběhne AV sken (port `IMalwareScanner`, no-op v Dev, ClamAV/cloud v prod); detekce → `Document.Status=Failed`, `FailureReason=malware_detected`, blob karanténován/smazán, žádné chunky · Mechanismus: AV sken seam mezi `PutAsync` a extrakcí (analogie content-type allowlist u Files); fail-closed v prod · Severity: P1 · Test: integ — EICAR test soubor → Failed + blob karanténa, žádná extrakce.
- **EC-01-02-12 — SSRF přes externí reference v dokumentu (HTML/SVG/PDF)** · Trigger: ingestované HTML/SVG/PDF obsahuje `<img src=http://169.254.169.254/...>` nebo remote resource; extraktor by je mohl fetchnout · Očekávané chování: extraktor NIKDY nestahuje externí zdroje — parsuje jen lokální bytes, žádný outbound HTTP; URL zůstanou jen jako text · Mechanismus: extraktory nakonfigurované offline (žádný resource resolver / network stack), HTML sanitizace bez resource fetch · Severity: P1 · Test: integ — dokument s odkazem na interní IP → 0 outbound requestů (network spy), jen text extrahován.
