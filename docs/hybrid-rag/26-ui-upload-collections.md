# Oblast 26 — UI — Document upload & collection management

Frontend vrstva (Next.js 16 App Router, Base UI + owned shadcn komponenty, BFF auth Model A) nad existujícími backend endpointy HybridRag pro nahrávání dokumentů a správu znalostních kolekcí. **Tato oblast NEDEFINUJE business logiku** — veškerá validace (allowlist, size cap, server-generated key), izolace (Scope Tenant|User, RLS), idempotence a stavový automat ingestu žijí na backendu (oblasti 00 collections, 01 ingest/upload, 02 chunk, 04 saga/202-status, 09 freshness, 23 admin). UI je tenký konzument: fetch jednou přes TanStack Query, reuse, `invalidateQueries` po mutaci; jeden SSE realtime provider pro live status; centrální RFC9457 → toast/error-boundary; i18n en+cs (next-intl, nested namespace `hybridrag.*`); a11y + responsive povinné. Identita/tenant se řeší výhradně server-side v BFF (route handlery / server actions) — token NIKDY v prohlížeči.

Konvence napříč oblastí: route base `/v1/hybridrag/...`, permission-gating dle `PlatformPermissions.Rag*` (claim v tokenu, BFF ověří server-side, UI jen skrývá/disabluje prvky pro UX — autorita je backend → cizí akce vrací 403/404), všechny časy zobrazené v UTC zdroji a formátované do lokální TZ na klientu, optimistic update + rollback při mutacích, bounded vše (upload cap, počet souborů, délka chunk preview).

---

## UC-26-01 — Drag-and-drop multi-file upload do kolekce
- **Actor / role:** přihlášený uživatel s `rag.documents.upload` (resp. `PlatformPermissions.RagDocumentsUpload`). · **Precondition:** existuje cílová kolekce, na kterou má uživatel zápisové právo dle jejího Scope (Tenant kolekce → tenant-write perm; User kolekce → vlastník). · **Trigger:** uživatel přetáhne 1..N souborů na dropzone na stránce detailu kolekce nebo klikne „Vybrat soubory" a otevře OS file dialog. · **Main flow:**
  1. Dropzone přijme `drop`/`change` event, klient načte `FileList`, pro každý soubor lokálně předvaliduje proti **klientskému zrcadlu** allowlistu + size cap (UX gate; reuse politiky z Files modulu — MIME allowlist deny-by-default, 10 MB cap, případně RAG-specifický override z config oblast 24 `Rag:Upload:*`).
  2. Soubory, které projdou předvalidací, se zařadí do front-end upload fronty s per-soubor řádkem (název, velikost, MIME, stav `Pending`).
  3. Pro každý soubor klient zavolá BFF route handler, který multipart-POSTne na `/v1/hybridrag/collections/{collectionId}/documents` (oblast 01). BFF přidá token server-side; **storage key generuje backend** (server-generated `{userId:N}/{id:N}` reuse Files pattern), klient NIKDY neposílá storage key, jen původní filename jako metadatum.
  4. Backend uloží bytes přes `IFileStorage`, založí `Document` (status `Queued`), spustí ingest sagu (202 + `Location: /v1/hybridrag/operations/{operationId}`, oblast 04). Response nese `documentId` + `operationId`.
  5. Klient přepne řádek na `Queued` a zaregistruje sledování ingest statusu (UC-26-04) — předá `operationId`/`documentId` SSE provideru.
  6. Po dokončení všech uploadů klient `invalidateQueries(['hybridrag','collections',collectionId,'documents'])` → list (UC-26-05) se refetchne a ukáže nové dokumenty na správném místě dle řazení.
- **Postcondition / záruky:** každý úspěšně nahraný soubor → 1 `Document` row + spuštěná ingest saga; UI ukáže aktuální stav (žádná stale cache). Při dílčím selhání části souborů zbytek pokračuje (per-soubor nezávislost). · **Tenancy / permissions:** tlačítko/dropzone skryté/disabled bez `rag.documents.upload`; cílová kolekce ověřena server-side proti Scope; vlastnictví z tokenu (`ITenantContext.UserId`), NE z těla. · **Reuse / canonical pattern:** oblast 01 (upload slice), Files modul (`UploadFileValidator`, server-generated key, content-type allowlist + size cap), `frontend-feature-slice` skill (single data source), BFF Model A. · **Data dotčena:** `hybridrag_documents`, blob storage (`IFileStorage`), nepřímo ingest saga. · **Eventy:** žádný klientský; backend publikuje ingest start (oblast 04). · **Priorita:** P0

### Edge cases UC-26-01
- **EC-26-01-01 — Soubor mimo MIME allowlist** · Trigger: uživatel přetáhne `.exe`/`.zip`/neznámý typ. · Očekávané chování: klient řádek okamžitě označí `Rejected` s důvodem „nepodporovaný typ" (i18n klíč), soubor se NEPOSÍLÁ; i kdyby klient obešel, backend `UploadFileValidator` vrátí RFC9457 `rag.document.unsupported_type` → toast. · Mechanismus: klientské zrcadlo allowlistu + **autoritativní backend** validace (oblast 01, Files pattern). · Severity: P1 · Test: drop `.exe`, assert řádek Rejected + 0 network POST; force-POST přes BFF → 400 mapováno na toast.
- **EC-26-01-02 — Soubor nad size cap** · Trigger: soubor > cap (10 MB resp. `Rag:Upload:MaxBytes`). · Očekávané chování: klient řádek `Rejected` s „překročena velikost", neposílá; backend request-body size limit + validator je druhá obrana. · Mechanismus: klient size check + backend cap (oblast 01). · Severity: P1 · Test: 11 MB soubor → Rejected, 0 POST; obejití → 413/400 toast.
- **EC-26-01-03 — Příliš mnoho souborů najednou** · Trigger: drop 500 souborů. · Očekávané chování: fronta bounded (`Rag:Upload:MaxBatch`, např. 50) — nadbytek odmítnut s upozorněním, uploady běží s omezenou konkurencí (např. 3–4 paralelně, zbytek čeká), UI nezamrzne. · Mechanismus: klientská bounded fronta + concurrency limiter; backend per-IP/per-user rate-limit (oblast 22). · Severity: P1 · Test: drop 200 souborů → max N queued, ostatní hlášeny; concurrency ≤ limit.
- **EC-26-01-04 — XSS v názvu souboru** · Trigger: filename `<img onerror=…>.pdf`. · Očekávané chování: filename se renderuje jen jako text (React escaping), nikdy `dangerouslySetInnerHTML`; storage key je server-generated, takže filename neovlivní cestu. · Mechanismus: React auto-escape + server key (Files pattern). · Severity: P1 · Test: upload souboru s HTML v názvu → v DOM zobrazen doslovně, žádné spuštění.
- **EC-26-01-05 — Drop na kolekci bez zápisového práva** · Trigger: uživatel vidí cizí/Tenant kolekci read-only a přetáhne soubor. · Očekávané chování: dropzone disabled (permission-gated), případně backend vrátí 403/404 → toast; žádný osamělý blob. · Mechanismus: permission-gating UI + backend Scope check (oblast 16 isolation). · Severity: P0 · Test: bez `rag.documents.upload` → dropzone disabled; force POST → 403.

---

## UC-26-02 — Zrušení probíhajícího uploadu
- **Actor / role:** uživatel, který spustil upload. · **Precondition:** běží alespoň jeden upload ve stavu `Pending`/`Uploading` (bytes se ještě přenášejí). · **Trigger:** uživatel klikne „Zrušit" na řádku souboru nebo „Zrušit vše". · **Main flow:**
  1. Klient zavolá `AbortController.abort()` na příslušném fetchi → HTTP přenos se přeruší.
  2. Řádek přejde do stavu `Canceled`, progress bar zmrazí na poslední hodnotě a zešedne.
  3. Pokud backend ještě nestihl založit `Document` (abort před dokončením requestu) → žádný side effect. Pokud `Document` už vznikl a saga běží, samotný HTTP abort to nezruší — UI nabídne navazující „Smazat dokument" (UC-26-06) nebo „Zrušit zpracování" pokud backend podporuje cancel sagy (oblast 04).
  4. Klient odebere řádek z aktivní fronty / ponechá jako Canceled dle UX.
- **Postcondition / záruky:** přerušený přenos nezanechá UI v nejednoznačném „forever uploading" stavu; pokud blob/Document částečně vznikl, UI to zviditelní a nabídne čistku. · **Tenancy / permissions:** rušit lze jen vlastní rozdělaný upload. · **Reuse / canonical pattern:** `AbortController` (web standard), oblast 04 (operation lifecycle). · **Data dotčena:** žádná (čistý abort) nebo `hybridrag_documents` (follow-up delete). · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-26-02
- **EC-26-02-01 — Cancel po dokončení přenosu, před response** · Trigger: abort přesně v okamžiku, kdy backend už commitnul `Document`. · Očekávané chování: UI nesmí tvrdit „zrušeno" definitivně — po refetchi listu se může dokument objevit; UI to po `invalidateQueries` reflektuje a nabídne smazat. · Mechanismus: refetch jako zdroj pravdy + idempotentní backend. · Severity: P2 · Test: abort v race okně → list refetch ukáže dokument nebo ne, UI konzistentní s backendem.
- **EC-26-02-02 — Cancel všech při paralelním běhu** · Trigger: „Zrušit vše" během 4 paralelních uploadů. · Očekávané chování: všechny aktivní AbortControllery se abortnou, čekající ve frontě se nezahájí, řádky → Canceled. · Mechanismus: hromadný abort + vyprázdnění fronty. · Severity: P2 · Test: 4 běžící + 6 queued → vše Canceled, 0 dalších POST.

---

## UC-26-03 — Retry selhaného uploadu (přenos)
- **Actor / role:** uživatel s `rag.documents.upload`. · **Precondition:** řádek souboru ve stavu `UploadFailed` (síťová chyba, 5xx, timeout, disconnect). · **Trigger:** klik „Zkusit znovu" na řádku nebo „Zkusit znovu všechny selhané". · **Main flow:**
  1. Klient drží referenci na původní `File` objekt v paměti (dokud uživatel neopustí stránku) → není nutné znovu vybírat soubor.
  2. Vytvoří nový multipart POST přes BFF (čerstvý `AbortController`), progress se resetuje, řádek → `Uploading`.
  3. Při úspěchu standardní flow jako UC-26-01 (krok 4–6).
- **Postcondition / záruky:** retry nevytvoří duplicitní dokument pokud původní pokus neprošel; pokud původní backend přesto Document založil (response se ztratila), zobrazí se po refetchi a uživatel řeší duplicitu manuálně/dedup (oblast 01 idempotence dle UNIQUE klíče, pokud definovaný). · **Tenancy / permissions:** vlastní soubory. · **Reuse / canonical pattern:** TanStack Query mutation retry, oblast 01. · **Data dotčena:** `hybridrag_documents`. · **Eventy:** žádné klientské. · **Priorita:** P2

### Edge cases UC-26-03
- **EC-26-03-01 — Ztracený File objekt po reloadu stránky** · Trigger: uživatel refreshne stránku, pak chce retry. · Očekávané chování: File už není v paměti — UI vyzve znovu vybrat soubor, neslibuje retry bez dat. · Mechanismus: browser nepersistuje File mezi reloady. · Severity: P3 · Test: failed upload → reload → řádek nabízí re-select, ne tiché selhání.
- **EC-26-03-02 — Retry vede k duplicitě** · Trigger: první POST uspěl na backendu, ale response se ztratil → retry pošle podruhé. · Očekávané chování: pokud backend má dedup (content hash / idempotency key, oblast 01), vrátí existující dokument; jinak vzniknou 2 řádky a UI to zobrazí pravdivě (žádné skrývání). · Mechanismus: backend idempotence nebo viditelná duplicita. · Severity: P2 · Test: simulace ztráty response → ověř chování dle backend kontraktu.

---

## UC-26-04 — Live sledování ingest statusu dokumentu (Queued→Indexed/Failed)
- **Actor / role:** uživatel sledující detail kolekce / řádek dokumentu. · **Precondition:** dokument má běžící nebo nedávno doběhlou ingest sagu (`operationId`). · **Trigger:** automaticky po uploadu (UC-26-01) nebo při otevření listu kolekce s nedokončenými dokumenty. · **Main flow:**
  1. Jeden sdílený SSE realtime provider se připojí na `/v1/hybridrag/.../stream` (oblast 21 streaming SSE) a odebírá události typu „document status changed" pro dokumenty viditelné na stránce.
  2. Při příchodu eventu provider zaktualizuje TanStack Query cache (setQueryData / targeted invalidate) → řádek dokumentu přejde stavem: `Queued` → `Pending` → `Parsing` → `Chunking` → `Embedding` → `Indexed` (success), nebo kterýkoli krok → `Failed` (s chybovým kódem/zprávou z oblasti 04/17 degradation `RetrievalStatus`).
  3. Stav se vizualizuje stepperem/badge + případně progress (např. „embedded 120/340 chunks" pokud backend posílá průběh).
  4. Fallback bez SSE: klient polluje `GET /v1/hybridrag/operations/{operationId}` (oblast 04) s backoffem, dokud stav není terminální.
  5. Při `Indexed` provider invaliduje detail dokumentu (chunk count, pages, ingested-at). Při `Failed` zobrazí důvod + nabídne retry (UC-26-07).
- **Postcondition / záruky:** UI status vždy konverguje k backend pravdě (saga je autorita); žádný „věčně Pending" bez fallback pollu; terminální stav je stabilní. · **Tenancy / permissions:** SSE stream owner-scoped tokenem (uživatel vidí jen vlastní/oprávněné dokumenty); oblast 16 isolation. · **Reuse / canonical pattern:** jeden SSE provider (oblast 21 + frontend pattern), oblast 04 (`IOperationStore` 202+status), oblast 17 (RetrievalStatus/degradation pro chybové třídy). · **Data dotčena:** read-only `hybridrag_documents` + operations. · **Eventy:** konzumuje document-status SSE eventy (oblast 04/21). · **Priorita:** P0

### Edge cases UC-26-04
- **EC-26-04-01 — SSE reconnect + replay (Last-Event-ID)** · Trigger: výpadek sítě/server restart během ingestu. · Očekávané chování: provider se reconnectne s `Last-Event-ID`, zmeškané status-transition eventy se přehrají (best-effort, oblast 21 replay), UI dorovná stav; pokud replay nepokryje, fallback poll na operations dorovná. · Mechanismus: SSE replay (Last-Event-ID, Redis stream) + poll fallback. · Severity: P1 · Test: kill SSE uprostřed → reconnect → stav konverguje k Indexed bez ručního reloadu.
- **EC-26-04-02 — Saga stuck / timeout (stale Pending)** · Trigger: ingest uvázne, durable work neterminalizuje. · Očekávané chování: backend reconcile job (oblast 04 `ReconcileStale…`) překlopí starý Pending/Running → Failed; UI to dostane jako status event a zobrazí Failed + retry. UI samo NErozhoduje o timeoutu. · Mechanismus: backend reconcile (oblast 04). · Severity: P1 · Test: zablokuj worker → po reconcile UI ukáže Failed.
- **EC-26-04-03 — Event pro dokument mimo viditelnou stránku** · Trigger: SSE pošle status dokumentu z jiné kolekce/stránky. · Očekávané chování: provider update cache, ale UI nerenderuje irelevantní toast spam; badge se aktualizuje až při návštěvě listu. · Mechanismus: cílený cache update dle queryKey. · Severity: P2 · Test: status event mimo aktuální view → žádný rušivý toast, cache konzistentní.
- **EC-26-04-04 — Out-of-order status eventy** · Trigger: `Embedding` event dorazí po `Indexed` (přeházené pořadí, oblast Wolverine no-ordering). · Očekávané chování: UI nepřepíše terminální `Indexed` zpět na `Embedding` — řadí dle monotónní verze/sekvence nebo ignoruje regresi z terminálu. · Mechanismus: status má pořadové/verzové pole; UI honoruje terminál. · Severity: P1 · Test: pošli eventy přeházeně → UI zůstane na Indexed.
- **EC-26-04-05 — Session expiry uprostřed sledování** · Trigger: token vyprší během dlouhého ingestu. · Očekávané chování: SSE/poll dostane 401 → BFF se pokusí refresh server-side; při neúspěchu UI navede na re-login bez ztráty kontextu (po loginu refetch). · Mechanismus: BFF refresh rotace + centrální 401 handling. · Severity: P1 · Test: expiruj token → graceful re-auth, stav se dorovná.

---

## UC-26-05 — Seznam/tabulka dokumentů v kolekci
- **Actor / role:** uživatel s read právem na kolekci. · **Precondition:** kolekce existuje, uživatel je oprávněn ji číst. · **Trigger:** otevření detailu kolekce / route `/hybridrag/collections/{id}`. · **Main flow:**
  1. TanStack Query načte přes BFF `GET /v1/hybridrag/collections/{id}/documents` (paged, oblast 00/01) — sloupce: filename, velikost, status (badge), chunk count, počet stran (pages), ingested-at, last-reindexed-at, tagy.
  2. UI vykreslí tabulku s řazením (default ingested-at desc), filtrováním (status, tag — UC-26-12), stránkováním/virtualizací pro velké kolekce.
  3. Řádky s neterminálním statusem se napojí na SSE provider (UC-26-04) pro live update.
  4. Akce per řádek (permission-gated): preview (UC-26-10), reindex (UC-26-11), delete (UC-26-06), retry (UC-26-07), edit tagů (UC-26-12).
- **Postcondition / záruky:** zobrazená data odpovídají serveru (po mutaci invalidace); řazení/filtr přes server `$orderby`/`$filter` ekvivalent, ne jen klientsky. · **Tenancy / permissions:** RLS + Scope na backendu (cizí dokument se v listu nezobrazí / 404 na detail); UI gat­uje akce dle perms. · **Reuse / canonical pattern:** oblast 00/01 (list slice, paging), TanStack Query single source, frontend tabulkový pattern (`Paged.totalCount`). · **Data dotčena:** read-only `hybridrag_documents`. · **Eventy:** konzumuje status SSE. · **Priorita:** P0

### Edge cases UC-26-05
- **EC-26-05-01 — Prázdná kolekce (empty state)** · Trigger: kolekce bez dokumentů. · Očekávané chování: empty state s výzvou „nahraj první dokument" + dropzone, ne prázdná tabulka bez kontextu. · Mechanismus: explicitní empty stav. · Severity: P2 · Test: nová kolekce → empty state render.
- **EC-26-05-02 — Velká kolekce (tisíce dokumentů)** · Trigger: 5000 dokumentů. · Očekávané chování: server-side paging + virtualizace řádků, žádný fetch-all; scroll plynulý. · Mechanismus: paging (oblast 00) + virtual list. · Severity: P1 · Test: seed 5000 → render první stránka, žádný N+1.
- **EC-26-05-03 — Loading / skeleton** · Trigger: první načtení. · Očekávané chování: skeleton řádky, ne layout shift; chyba → error boundary s retry. · Mechanismus: TanStack Query loading/error stavy. · Severity: P2 · Test: pomalá síť → skeleton; 500 → error UI s retry.

---

## UC-26-06 — Smazání dokumentu (purge chunků + vektorů)
- **Actor / role:** uživatel s `rag.documents.delete`. · **Precondition:** dokument existuje, uživatel je vlastník/oprávněný dle Scope. · **Trigger:** klik „Smazat" na řádku → potvrzovací dialog. · **Main flow:**
  1. Uživatel potvrdí v modal dialogu (destruktivní akce — vyžaduje explicitní potvrzení, ne jednoklik).
  2. Klient optimisticky odebere řádek z listu + `DELETE /v1/hybridrag/collections/{id}/documents/{docId}` přes BFF (oblast 01).
  3. Backend smaže `Document` + kaskádově purge `Chunk` rows + jejich embeddingy (`halfvec`) + případně graph nodes/edges odvozené jen z tohoto dokumentu (oblast 02/03/10 dle backend kontraktu) + blob přes `IFileStorage`.
  4. Při úspěchu success toast; klient `invalidateQueries` listu (potvrdí odebrání). Při chybě rollback (řádek se vrátí) + error toast.
- **Postcondition / záruky:** po smazání dokument nefiguruje v retrievalu (chunky/vektory pryč) — žádné „duch" výsledky v budoucích dotazech; append-only audit smazání zaznamenán (oblast 25). · **Tenancy / permissions:** cizí docId → 404 (RLS/Scope, oblast 16); akce gat­ovaná `rag.documents.delete`. · **Reuse / canonical pattern:** oblast 01 (delete slice), Surrounding Concerns (post-mutation invalidace + ordering + success feedback). · **Data dotčena:** `hybridrag_documents`, `hybridrag_chunks`, embedding sloupec, blob, případně graph tabulky, audit. · **Eventy:** backend může publikovat „document deleted" (re-index/graph cleanup); UI nečeká. · **Priorita:** P0

### Edge cases UC-26-06
- **EC-26-06-01 — Double-submit guard** · Trigger: rychlý dvojklik na Smazat. · Očekávané chování: tlačítko disabled + spinner po dobu requestu; druhý DELETE se neodešle / backend idempotentní (druhé smazání → 404 nebo no-op, UI nezobrazí matoucí error). · Mechanismus: loading state + idempotentní delete. · Severity: P1 · Test: 2× rychlý klik → 1 efektivní DELETE.
- **EC-26-06-02 — Smazání dokumentu během jeho ingestu** · Trigger: delete když saga ještě běží (Parsing/Embedding). · Očekávané chování: backend buď saga zruší/zneplatní a uklidí, nebo delete odmítne s „probíhá zpracování, zkuste po dokončení". UI zobrazí výsledek pravdivě, žádný orphan. · Mechanismus: backend koordinace saga↔delete (oblast 04). · Severity: P1 · Test: delete během ingestu → konzistentní stav, žádné osiřelé chunky.
- **EC-26-06-03 — Optimistic rollback při selhání** · Trigger: DELETE vrátí 500. · Očekávané chování: řádek se vrátí do listu (rollback), error toast; cache konzistentní. · Mechanismus: TanStack optimistic + rollback. · Severity: P1 · Test: mock 500 → řádek zpět přítomen.

---

## UC-26-07 — Retry selhané ingestace dokumentu (reprocess Failed)
- **Actor / role:** uživatel s `rag.documents.reindex`/`rag.documents.upload`. · **Precondition:** dokument ve stavu `Failed` (parsing/chunking/embedding selhalo). · **Trigger:** klik „Zkusit znovu zpracovat" na Failed řádku. · **Main flow:**
  1. Klient `POST /v1/hybridrag/collections/{id}/documents/{docId}/reindex` (resp. dedikovaný retry endpoint, oblast 04/09), bytes už jsou uloženy → reprocess nevyžaduje re-upload.
  2. Backend restartuje ingest sagu od bezpečného bodu (resumable, idempotentní — předchozí částečné chunky/embeddingy se přepíší/dedup­nou, oblast 02/03), vrátí 202 + `operationId`.
  3. Řádek přejde zpět do `Queued`/`Parsing`, UI sleduje status (UC-26-04).
- **Postcondition / záruky:** retry je idempotentní (žádné zdvojené chunky); po úspěchu `Indexed`, `last-reindexed-at` aktualizováno. · **Tenancy / permissions:** vlastní/oprávněný dokument; gat­ováno reindex perm. · **Reuse / canonical pattern:** oblast 04 (saga/202), oblast 09 (freshness/reindex), idempotence chunk/embed (oblast 02/03). · **Data dotčena:** `hybridrag_chunks`, embeddingy, `hybridrag_documents.last_reindexed`. · **Eventy:** status SSE. · **Priorita:** P1

### Edge cases UC-26-07
- **EC-26-07-01 — Trvale neparsovatelný soubor** · Trigger: poškozené PDF, retry opět selže. · Očekávané chování: po retry zase `Failed` s konkrétním důvodem; UI nenabízí nekonečný retry loop bez kontextu, ukáže chybový kód a doporučení (re-upload jiného souboru). · Mechanismus: backend chybová třída (oblast 17). · Severity: P2 · Test: corrupt soubor → 2× Failed se stejným kódem.
- **EC-26-07-02 — Retry během běžícího ingestu (dvojí spuštění)** · Trigger: uživatel klikne reindex na dokumentu, který už není Failed (race se SSE). · Očekávané chování: tlačítko gat­ované stavem (jen Failed/Indexed reindexovatelné); backend odmítne duplicitní běžící sagu. · Mechanismus: stavový guard UI + backend saga identity. · Severity: P2 · Test: reindex na Embedding stavu → tlačítko disabled / 409.

---

## UC-26-08 — CRUD kolekce (create / rename / delete) se Scope pickerem
- **Actor / role:** uživatel s `rag.collections.manage` (create/rename/delete). · **Precondition:** přihlášen; pro Tenant-scope kolekci navíc tenant-admin právo. · **Trigger:** „Nová kolekce" / „Přejmenovat" / „Smazat" v UI správy kolekcí. · **Main flow:**
  1. **Create:** dialog s polem name + **Scope picker** (`Tenant` | `User`) + volitelně default chunking config (UC-26-09). Scope picker je permission-gated — `Tenant` volba viditelná jen tomu, kdo smí zakládat tenant-wide kolekce; jinak forced `User`. `POST /v1/hybridrag/collections` (oblast 00). Optimisticky přidat do listu, invalidace.
  2. **Rename:** inline/dialog edit name → `PATCH .../collections/{id}`; optimistic + rollback.
  3. **Delete:** potvrzovací dialog s varováním (smaže všechny dokumenty/chunky/vektory kolekce) → `DELETE .../collections/{id}`; backend kaskáduje (oblast 00/01).
- **Postcondition / záruky:** Scope je neměnný po vzniku, pokud backend neumožní migraci (UI to respektuje — Scope picker disabled v rename); delete kolekce odstraní veškerý odvozený index. · **Tenancy / permissions:** Tenant kolekce viditelná všem v tenantu (read dle perms), User kolekce jen vlastníkovi (RLS, oblast 16); manage akce gat­ovány. · **Reuse / canonical pattern:** oblast 00 (collection CRUD), oblast 16 (Scope isolation), CLAUDE.md Scope=Tenant|User. · **Data dotčena:** `hybridrag_knowledge_collections` (+ kaskáda při delete). · **Eventy:** backend může publikovat collection lifecycle; UI nečeká. · **Priorita:** P0

### Edge cases UC-26-08
- **EC-26-08-01 — Duplicitní název kolekce** · Trigger: create se jménem, které už existuje v daném Scope. · Očekávané chování: backend vrátí `rag.collection.name_taken` (RFC9457) → inline validační chyba u pole, ne generický toast; optimistic rollback. · Mechanismus: backend UNIQUE + i18n errorCode. · Severity: P2 · Test: 2× stejné jméno → druhý 409 inline.
- **EC-26-08-02 — Delete neprázdné kolekce** · Trigger: smazání kolekce s tisíci dokumentů. · Očekávané chování: jasné varování s počtem dotčených dokumentů; smazání může být dlouhé → backend 202 + progress, nebo synchronní s loading; UI nezamrzne, po dokončení invalidace. · Mechanismus: oblast 00/04. · Severity: P1 · Test: delete kolekce s 500 dok → potvrzení s počtem, čisté dokončení.
- **EC-26-08-03 — Scope picker leak** · Trigger: uživatel bez tenant-práv otevře create. · Očekávané chování: `Tenant` volba skrytá nebo disabled s vysvětlením; i kdyby poslal `Scope=Tenant`, backend ho odmítne (403) — UI to nediktuje. · Mechanismus: permission-gating + backend autorita (oblast 16). · Severity: P0 · Test: bez tenant-perm → picker forced User; force Tenant POST → 403.
- **EC-26-08-04 — Concurrent rename (dva uživatelé)** · Trigger: dva tenant-admini přejmenují tutéž kolekci. · Očekávané chování: xmin concurrency (backend) → druhý dostane konflikt, UI nabídne refetch a re-apply, žádný tichý overwrite. · Mechanismus: oblast 00 + `ConcurrencyRetryBehavior`. · Severity: P2 · Test: paralelní PATCH → jeden uspěje, druhý 409 → refresh.

---

## UC-26-09 — Panel konfigurace chunkingu per kolekce (auto vs advanced)
- **Actor / role:** uživatel s `rag.collections.manage`. · **Precondition:** kolekce existuje. · **Trigger:** otevření „Nastavení zpracování" kolekce. · **Main flow:**
  1. Panel nabízí dva režimy (copy LlamaCloud auto-vs-advanced): **Auto** (doporučené defaulty, skryje detaily) a **Advanced** (odemkne pole: strategy `sentence` | `token` | `semantic` | `char`, chunk size, overlap).
  2. Hodnoty se čtou z config registru (oblast 24, `Rag:Chunking:*` defaulty) přepsatelné per-kolekce přes `RagSetting`/collection config (oblast 00/24). UI ukáže effective hodnoty + zda jsou default nebo override.
  3. Uložení → `PATCH .../collections/{id}/chunking-config` (oblast 00/02). Validace rozsahů: size/overlap bounded (overlap < size, size v povoleném rozsahu — backend autorita, UI zrcadlí).
  4. Upozornění: změna se projeví až na NOVĚ ingestovaných/reindexovaných dokumentech — UI nabídne navazující reindex (UC-26-11) pro aplikaci na stávající.
- **Postcondition / záruky:** config uložen, effective hodnoty konzistentní s backendem; stávající chunky se NEzmění samy (jen reindex). · **Tenancy / permissions:** gat­ováno manage perm; Tenant kolekce → tenant-admin. · **Reuse / canonical pattern:** oblast 02 (chunk), oblast 24 (config registry + RagSetting override), oblast 00. · **Data dotčena:** collection config / `hybridrag_settings`. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-26-09
- **EC-26-09-01 — Nevalidní rozsahy (overlap ≥ size, size mimo bounds)** · Trigger: advanced režim, uživatel zadá overlap > size. · Očekávané chování: inline validace (UI bound) + backend validator (`rag.chunking.invalid_config`) jako autorita; uložení blokováno. · Mechanismus: oblast 02 validace + FluentValidation. · Severity: P1 · Test: overlap 600 > size 500 → chyba, neuloží.
- **EC-26-09-02 — Změna configu bez reindexu** · Trigger: uživatel uloží novou strategii a očekává okamžitou změnu retrievalu. · Očekávané chování: UI explicitně sdělí „platí pro nové/reindexované dokumenty" + CTA reindex; žádné falešné očekávání. · Mechanismus: copy + UC-26-11 chaining. · Severity: P2 · Test: uložení → banner s reindex CTA.
- **EC-26-09-03 — Semantic strategy nedostupná (degradace)** · Trigger: backend nemá embed gateway pro semantic chunking (fake/degraded, oblast 17). · Očekávané chování: volba disabled s vysvětlením nebo fallback na default; UI nepředstírá funkčnost. · Mechanismus: capability flag z config (oblast 24/17). · Severity: P2 · Test: degraded backend → semantic disabled.

---

## UC-26-10 — Náhled parsování a chunkování dokumentu (parse/chunk preview)
- **Actor / role:** uživatel s read právem na dokument (+ příp. `rag.documents.preview`). · **Precondition:** dokument je alespoň ve stavu po parsování/chunkování (chunky existují) nebo backend umí dry-run preview. · **Trigger:** klik „Náhled" na řádku dokumentu. · **Main flow:**
  1. Klient `GET /v1/hybridrag/collections/{id}/documents/{docId}/preview` (oblast 02/01) — vrátí extrahovaný text, hranice chunků (offsety/indexy), mapování na stránky (page mapping), počet chunků a délky.
  2. UI vykreslí čtečku: levý panel = extrahovaný text se zvýrazněnými chunk hranicemi (barevné segmenty / čísla), pravý/hover = detail chunku (index, délka, page range, případně embedding status).
  3. Slouží k debugu špatného parsu PŘED indexací/reindexem — uživatel pozná OCR/extraction problém, špatné hranice, ztracené stránky.
  4. Z preview lze přejít na reindex (UC-26-11) po úpravě chunking configu (UC-26-09).
- **Postcondition / záruky:** read-only, nemění data; preview je bounded (paginace/lazy load chunků pro velké dokumenty). · **Tenancy / permissions:** cizí docId → 404 (RLS/Scope, oblast 16). · **Reuse / canonical pattern:** oblast 02 (chunk boundaries, page mapping), oblast 01. · **Data dotčena:** read-only `hybridrag_chunks` / `hybridrag_documents`. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-26-10
- **EC-26-10-01 — XSS / aktivní obsah v extrahovaném textu** · Trigger: dokument obsahuje `<script>`/HTML, který se dostal do extrahovaného textu. · Očekávané chování: text se renderuje výhradně jako plain text (escape), nikdy jako HTML; zvýraznění hranic přes bezpečné wrappery, ne injektovaný markup. · Mechanismus: React escaping / sanitizace (frontend XSS taxonomie). · Severity: P0 · Test: dokument s `<script>` → v DOM doslovný text, žádné spuštění.
- **EC-26-10-02 — Velmi dlouhý dokument (tisíce chunků)** · Trigger: 5000-chunkový dokument. · Očekávané chování: lazy/virtualizovaný render + paginace chunků, ne celý text najednou; bound na velikost odpovědi. · Mechanismus: paging + virtualizace. · Severity: P1 · Test: velký dokument → preview plynulé, žádný OOM.
- **EC-26-10-03 — Preview na dosud neparsovaný dokument** · Trigger: dokument je `Queued`/`Parsing`, chunky neexistují. · Očekávané chování: UI zobrazí „náhled zatím nedostupný, probíhá zpracování" + live status; ne prázdná/rozbitá čtečka. · Mechanismus: stavový guard + UC-26-04. · Severity: P2 · Test: preview na Parsing → informativní stav.
- **EC-26-10-04 — Selhaný parse (prázdný text)** · Trigger: OCR selhalo, extrahovaný text prázdný. · Očekávané chování: empty/error stav s vysvětlením a CTA reindex/jiný soubor, ne tichý prázdný panel. · Mechanismus: oblast 17 chybová třída. · Severity: P2 · Test: scan-only PDF bez OCR → empty-parse stav.

---

## UC-26-11 — Reindex / reprocess (úroveň dokumentu i kolekce) s progress
- **Actor / role:** uživatel s `rag.documents.reindex` (dok) / `rag.collections.manage` (kolekce). · **Precondition:** dokument `Indexed`/`Failed`, resp. kolekce s ≥1 dokumentem. · **Trigger:** klik „Přeindexovat" na dokumentu, nebo „Přeindexovat celou kolekci" v nastavení (typicky po změně chunking configu UC-26-09 nebo embedding modelu, oblast 03/09 freshness). · **Main flow:**
  1. **Doc-level:** `POST .../documents/{docId}/reindex` → 202 + operationId; saga přerozseká + přeembedne (idempotentně přepíše chunky/vektory, oblast 02/03/09), status sleduje UC-26-04.
  2. **Collection-level:** `POST .../collections/{id}/reindex` → spustí dávkovou, **resumable** úlohu (oblast 04/23 admin) přes 202; vrátí jeden operationId pro celou dávku.
  3. UI ukáže agregovaný progress (X/N dokumentů přeindexováno) + per-dokument badge přes SSE; možnost úlohu sledovat i po navigaci jinam (provider drží stav).
  4. Po dokončení `last-reindexed-at` aktualizováno; invalidace listu.
- **Postcondition / záruky:** reindex idempotentní (žádné duplicitní chunky), resumable (po výpadku pokračuje od nezpracovaných, oblast 04); stará data nahrazena, retrieval konzistentní s novým configem. · **Tenancy / permissions:** Tenant kolekce → tenant-admin; oblast 16. · **Reuse / canonical pattern:** oblast 04 (202+resumable saga), oblast 09 (freshness/reindex), oblast 23 (admin bulk job), oblast 03 (re-embed idempotence). · **Data dotčena:** `hybridrag_chunks`, embeddingy, `hybridrag_documents`, graph (pokud reextract). · **Eventy:** status SSE; backend interní reindex eventy. · **Priorita:** P1

### Edge cases UC-26-11
- **EC-26-11-01 — Přerušení uprostřed dávkového reindexu (resumable)** · Trigger: worker/server restart během reindexu kolekce. · Očekávané chování: úloha pokračuje od nezpracovaných dokumentů (resumable, oblast 04), žádné přeskočené ani dvojitě zpracované; UI progress se po reconnectu dorovná. · Mechanismus: durable saga + idempotence. · Severity: P1 · Test: kill worker uprostřed → restart dokončí zbytek.
- **EC-26-11-02 — Dvojí spuštění reindexu téže kolekce** · Trigger: uživatel klikne „Přeindexovat" dvakrát / dva admini současně. · Očekávané chování: backend deduplikuje běžící job (saga identity / „již probíhá" 409); UI tlačítko disabled během běhu. · Mechanismus: saga identity + UI guard. · Severity: P1 · Test: 2× POST → 1 aktivní job.
- **EC-26-11-03 — Reindex během dotazování (retrieval konzistence)** · Trigger: uživatel se ptá nad kolekcí, která se právě přeindexovává. · Očekávané chování: retrieval vrací konzistentní stav (staré nebo nové chunky, ne polovinu) — odpovědnost backendu (oblast 09/12); UI případně zobrazí „kolekce se přeindexovává, výsledky mohou být neúplné". · Mechanismus: backend atomicita/verze indexu (oblast 09). · Severity: P2 · Test: dotaz během reindexu → žádné rozbité/poloprázdné výsledky.

---

## UC-26-12 — Metadata a tagy dokumentu (edit + filtrování)
- **Actor / role:** uživatel s `rag.documents.manage` (edit metadat). · **Precondition:** dokument existuje, oprávněný. · **Trigger:** „Upravit tagy/metadata" na dokumentu, nebo filtr v listu. · **Main flow:**
  1. **Edit:** dialog s tag inputem (chips) + volnými metadata poli (key-value dle backend schématu, oblast 01). `PATCH .../documents/{docId}/metadata`; optimistic + rollback.
  2. **Filter:** v listu (UC-26-05) filtr dle tagů/metadat → server-side `$filter` ekvivalent (oblast 01); URL state (sdílitelný odkaz, zachovaný přes reload).
  3. Tagy/metadata lze využít i jako retrieval filtr (oblast 05/06/12) — UI je jen edituje, filtrování v dotazu řeší příslušná query oblast.
- **Postcondition / záruky:** metadata uložena, list/filtr konzistentní po invalidaci; tagy normalizované (trim, case dle backendu). · **Tenancy / permissions:** vlastní/oprávněný dokument; metadata viditelná dle Scope; gat­ováno manage perm. · **Reuse / canonical pattern:** oblast 01 (document metadata), oblast 05/06/12 (metadata jako retrieval filtr — konzument). · **Data dotčena:** `hybridrag_documents` (metadata/tags JSONB nebo dedikovaná tabulka). · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-26-12
- **EC-26-12-01 — XSS / injection v tagu** · Trigger: tag `<img onerror>` nebo speciální znaky. · Očekávané chování: render jako text (escape); backend normalizuje/validuje povolené znaky (`rag.document.invalid_tag`). · Mechanismus: React escape + backend validace. · Severity: P1 · Test: HTML tag → doslovný render, ne spuštění.
- **EC-26-12-02 — Concurrent edit metadat** · Trigger: dva uživatelé editují tytéž tagy. · Očekávané chování: xmin concurrency → druhý 409, UI nabídne refetch + merge, žádný tichý overwrite. · Mechanismus: oblast 01 + `ConcurrencyRetryBehavior`. · Severity: P2 · Test: paralelní PATCH → konflikt řešen.
- **EC-26-12-03 — Filtr bez výsledků** · Trigger: filtr na neexistující tag. · Očekávané chování: empty state „žádné dokumenty neodpovídají", možnost reset filtru; URL state zachová filtr. · Mechanismus: empty stav + URL state. · Severity: P3 · Test: filtr „xyz" → empty + reset CTA.

---

## UC-26-13 — Přehled/dashboard kolekcí
- **Actor / role:** přihlášený uživatel s read na RAG. · **Precondition:** přihlášen. · **Trigger:** vstup na `/hybridrag/collections`. · **Main flow:**
  1. `GET /v1/hybridrag/collections` (oblast 00, paged) → karty/tabulka kolekcí: name, Scope badge (Tenant|User), počet dokumentů, agregovaný stav (kolik Indexed/Failed/Processing), velikost, vytvořeno/aktualizováno.
  2. Tenant kolekce a vlastní User kolekce jsou vizuálně odlišené; akce manage (UC-26-08/09/11) gat­ované perms.
  3. Klik na kolekci → detail (UC-26-05).
- **Postcondition / záruky:** zobrazené kolekce odpovídají Scope/RLS uživatele (žádné cizí User kolekce); agregáty konzistentní po invalidaci. · **Tenancy / permissions:** RLS + Scope filtr backend (oblast 16); UI nediktuje viditelnost. · **Reuse / canonical pattern:** oblast 00 (collection list), nav entitlement (lowercase casing). · **Data dotčena:** read-only `hybridrag_knowledge_collections`. · **Eventy:** případně collection/document SSE pro live agregáty. · **Priorita:** P1

### Edge cases UC-26-13
- **EC-26-13-01 — Modul nezapnutý / bez entitlementu** · Trigger: tenant nemá HybridRag entitlement (`ModuleEntitlementGuard` → 404, oblast 16/multitenancy). · Očekávané chování: nav položka skrytá (entitlement casing lowercase); přímý přístup na route → graceful 404/„modul nedostupný", ne crash. · Mechanismus: entitlement guard + nav gating. · Severity: P1 · Test: tenant bez RAG → nav skryto, route 404.
- **EC-26-13-02 — Žádné kolekce** · Trigger: nový uživatel/tenant. · Očekávané chování: empty state s CTA „Vytvořit první kolekci" (gat­ováno manage perm — jinak read-only hláška). · Mechanismus: empty stav + permission gate. · Severity: P2 · Test: 0 kolekcí → empty + CTA dle perm.

---

## UC-26-14 — Hromadné akce nad dokumenty (multi-select → delete / reindex)
- **Actor / role:** uživatel s příslušnými perms (`rag.documents.delete` / `rag.documents.reindex`). · **Precondition:** list kolekce s ≥1 dokumentem. · **Trigger:** zaškrtnutí více řádků → bulk action bar (Smazat vybrané / Přeindexovat vybrané). · **Main flow:**
  1. Uživatel vybere N dokumentů (checkbox + „vybrat vše na stránce" / „vybrat vše odpovídající filtru").
  2. Potvrzovací dialog s počtem; klient pošle bulk operaci — buď jeden bulk endpoint, nebo bounded sérii (concurrency limit) volání UC-26-06/UC-26-11.
  3. Progress bar (X/N hotovo), per-řádek výsledek (success/fail); částečné selhání zviditelněno (které selhaly + retry jen těch).
  4. Po dokončení invalidace listu, success/partial toast.
- **Postcondition / záruky:** bulk je per-item nezávislý — selhání jednoho neshodí ostatní; konečný stav listu odpovídá backendu. · **Tenancy / permissions:** každý item ověřen backendem (cizí → 404, přeskočen); akce gat­ovány. · **Reuse / canonical pattern:** UC-26-06 / UC-26-11, oblast 23 (bulk admin pattern, pokud existuje bulk endpoint — jinak bounded fan-out), Surrounding Concerns (loading/partial feedback). · **Data dotčena:** dle akce (`hybridrag_documents`/`hybridrag_chunks`/embeddingy). · **Eventy:** status SSE per dokument. · **Priorita:** P2

### Edge cases UC-26-14
- **EC-26-14-01 — Částečné selhání dávky** · Trigger: 50 dokumentů, 3 selžou (concurrency/409/permission). · Očekávané chování: 47 úspěšných se projeví, 3 zvýrazněny s důvodem + „zkusit znovu jen selhané"; žádný all-or-nothing rollback úspěšných. · Mechanismus: per-item nezávislost + agregovaný report. · Severity: P1 · Test: mock 3 selhání → partial success UI.
- **EC-26-14-02 — Velký výběr (vybrat vše = tisíce)** · Trigger: „vybrat vše odpovídající filtru" nad 5000 dok. · Očekávané chování: bounded zpracování (server-side bulk job 202, oblast 04/23, nebo bounded fan-out s rate-limit oblast 22), UI progress, ne 5000 paralelních requestů. · Mechanismus: bulk job / bounded concurrency + rate-limit. · Severity: P1 · Test: 5000 výběr → bounded zpracování, žádný request storm.
- **EC-26-14-03 — Session expiry uprostřed dávky** · Trigger: token vyprší během dlouhé dávky. · Očekávané chování: BFF refresh server-side; při neúspěchu dávka se zastaví na bezpečném bodě, dokončené zůstanou, UI navede na re-login + nabídne dokončit zbytek. · Mechanismus: BFF refresh + per-item idempotence. · Severity: P2 · Test: expiruj token v půlce → dokončené persistují, zbytek obnovitelný.

---

## Cross-cutting UI edge cases (platí napříč UC-26-01..14)

### Edge cases — průřezové
- **EC-26-CC-01 — Dark mode + hydration** · Trigger: první render dropzone/tabulky/stepperu v dark mode. · Očekávané chování: žádný hydration mismatch (next-themes `scriptProps` fix, viz memory), badge/progress barvy mají dostatečný kontrast i v dark; žádný script-tag warning blokující kliky. · Mechanismus: next-themes + a11y kontrast. · Severity: P2 · Test: toggle dark → 0 hydration warning, kontrast AA.
- **EC-26-CC-02 — Chybějící i18n klíč (en/cs)** · Trigger: nový status/errorCode bez resx/next-intl klíče. · Očekávané chování: next-intl nested namespace `hybridrag.*` — chybějící klíč nesmí zobrazit raw tečkový klíč ani shodit dev overlay (past z memory: ploché klíče → 45s timeouty); fallback na čitelný text. · Mechanismus: i18n nested namespace + fallback. · Severity: P1 · Test: smaž klíč → graceful fallback, ne raw key.
- **EC-26-CC-03 — A11y: dropzone, modal focus trap, keyboard nav** · Trigger: uživatel ovládá vše klávesnicí / screen readerem. · Očekávané chování: dropzone má klávesovou alternativu (Enter otevře dialog), potvrzovací modaly mají focus-trap + Esc, tabulka navigovatelná, status badge má textový ekvivalent pro SR (ne jen barva). · Mechanismus: a11y (Base UI primitives + povinné focus mgmt). · Severity: P1 · Test: keyboard-only flow upload→delete; SR oznámí status změny.
- **EC-26-CC-04 — Responsive breakpointy** · Trigger: mobile/tablet. · Očekávané chování: tabulka → karty na mobilu, dropzone funkční na touch (file picker fallback místo drag-drop), bulk action bar dostupný; žádný horizontální scroll lock. · Mechanismus: responsive layout (mobile/tablet/desktop). · Severity: P2 · Test: 375px viewport → karty + funkční upload.
- **EC-26-CC-05 — Centrální RFC9457 error handling** · Trigger: jakýkoli endpoint vrátí ProblemDetails (rate-limit 429, 403, 404, 400). · Očekávané chování: jeden centrální handler mapuje na toast / inline / error-boundary dle typu; 429 ukáže `Retry-After` (oblast 22); permission chyby vedou na re-auth/skrytí, ne generický crash. · Mechanismus: centrální error handling + RFC9457. · Severity: P1 · Test: mock 429/403/404 → korektní UI reakce.
- **EC-26-CC-06 — Permission-gated prvky: skrýt vs disable** · Trigger: uživatel bez konkrétní perm. · Očekávané chování: destruktivní/manage akce skryté nebo disabled s tooltipem; UI gating je jen UX — backend zůstává autorita (403/404). Žádná akce se nespoléhá pouze na frontend gate. · Mechanismus: claim z tokenu (BFF server-side) + backend enforcement (oblast 16/23). · Severity: P0 · Test: per-perm matice → korektní viditelnost; force-call bez perm → 403/404.
