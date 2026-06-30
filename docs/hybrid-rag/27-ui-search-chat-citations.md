# Oblast 27 — UI — Grounded search, chat & citations

Frontendová vrstva (platform Next.js 16 app) pro grounded RAG search a chat nad modulem HybridRag. Tato oblast **pouze konzumuje** existující backend read/write endpointy a SSE streamy — neredefinuje business logiku, validaci ani izolaci. Identita a tenant se řeší výhradně server-side přes BFF (Model A: tokeny nikdy v browseru). Veškerá data jdou jedním data sourcem (TanStack Query), realtime jedním SSE providerem. Citace **jsou produkt** — jádrem UX je dohledatelnost každého tvrzení zpět ke zdrojovému chunku.

Cross-ref backend oblasti (NEDUPLIKOVAT, jen konzumovat): **13** answer+citace · **17** degradation (RetrievalStatus) · **21** streaming SSE · **24** config query-override allowlist · **16** tenant/user isolation + horní meze · **19** observ+cost · **32** feedback. FE patterny: skills `modularplatform-frontend` + `frontend-feature-slice`.

---

## UC-27-01 — Položení grounded dotazu a zahájení streamované odpovědi

- **Actor / role:** přihlášený uživatel s `PlatformPermissions.RagQuery` (dotted `rag.query`) · **Precondition:** uživatel má přístup alespoň k jedné `KnowledgeCollection` (Scope=User nebo Tenant), modul `hybridrag` je entitled (nav casing lowercase) · **Trigger:** uživatel napíše dotaz do chat inputu a odešle (Enter / tlačítko Send) · **Main flow:**
  1. UI provede client-side validaci neprázdného dotazu (trim) a délkového limitu (zrcadlí backend mez, ale autoritativní je backend).
  2. Server action / route handler (BFF) připojí bearer token server-side, zavolá backend write endpoint pro vytvoření / pokračování `RagConversation` + `RagTurn` a otevře SSE stream (oblast 21, `/v1/hybridrag/conversations/{id}/stream` nebo `/messages/stream`).
  3. UI okamžitě vykreslí optimisticky user message bublinu + prázdnou asistentskou bublinu se „thinking" skeletonem.
  4. SSE delta eventy se streamují token po tokenu do asistentské bubliny; `done` event nese finální text + citační mapu + `RetrievalStatus` + traceId.
  5. Po `done` se TanStack Query cache konverzace invaliduje/aktualizuje na perzistovaný stav (citace, trace).
- **Postcondition / záruky:** v UI je vidět kompletní streamovaná odpověď s inline citacemi; konverzace je perzistovaná na backendu; žádný token z prompt cache ani retrieval kontextu neunikne do browseru mimo to, co backend ve `done` payloadu povolí · **Tenancy / permissions:** identita z tokenu server-side (`ITenantContext.UserId`), nikdy z body; collection scope vynucuje backend (oblast 16) · **Reuse / canonical pattern:** oblast 21 (SSE delta/done), oblast 13 (answer+citace); FE `modularplatform-frontend` SSE provider, vibe streaming vzor z MEMORY (`IChatClient.GetStreamingResponseAsync` + `/messages/stream`) · **Data dotčena:** `RagConversation`, `RagTurn`, `AnswerCitation` (read po done) · **Eventy:** SSE `delta`/`done` (konzumace, ne produkce) · **Priorita:** P0

### Edge cases UC-27-01
- **EC-27-01-01 — Prázdný / whitespace dotaz** · Trigger: uživatel odešle prázdný input · Očekávané chování: Send disabled dokud není neprázdný trimmed obsah; žádný request · Mechanismus: client guard (UX only, backend validace autoritativní) · Severity: P3 · Test: e2e — Send tlačítko disabled při prázdném inputu
- **EC-27-01-02 — Double-submit během streamu** · Trigger: uživatel rychle 2× Enter / klik než dorazí `done` · Očekávané chování: input + Send se zamknou (loading state) po dobu aktivního streamu, druhý submit ignorován · Mechanismus: in-flight guard + disabled state (Surrounding Concerns #5 loading state) · Severity: P1 · Test: e2e — dva rychlé submity → jen jeden request, jedna odpověď
- **EC-27-01-03 — Dotaz nad limit délky** · Trigger: vložení velmi dlouhého textu · Očekávané chování: UI ukáže počítadlo znaků + inline error při překročení; backend stejně vrátí RFC9457 ValidationException, který se mapuje na toast · Mechanismus: client hint + centrální RFC9457 error handling · Severity: P2 · Test: e2e — překročení limitu → blokace + chybová hláška
- **EC-27-01-04 — Optimistic user message duplikace** · Trigger: optimisticky vykreslená user bublina + následně perzistovaná z backendu · Očekávané chování: po reconcile cache je v UI jen JEDNA user message (žádný transientní duplikát) · Mechanismus: dedup podle turnId / `.first()` selektor (past z MEMERY: optimistic+persisted user msg transientní duplikát) · Severity: P2 · Test: e2e — assert přesně jedna user bublina po done
- **EC-27-01-05 — Session expiry uprostřed psaní/streamu** · Trigger: token vyprší před nebo během SSE · Očekávané chování: BFF vrátí 401 → UI přesměruje na re-auth / refresh, rozepsaný dotaz se neztratí (draft v lokálním stavu) · Mechanismus: centrální 401 handling, draft preservation · Severity: P1 · Test: e2e — vypršení tokenu během streamu → re-auth flow, draft zachován

---

## UC-27-02 — Token streaming odpovědi (delta/done) do chat bubliny

- **Actor / role:** uživatel (pasivní příjemce streamu) · **Precondition:** SSE stream otevřen (UC-27-01) · **Trigger:** příchod SSE `delta` eventů · **Main flow:**
  1. Jeden centrální SSE realtime provider (oblast 21) drží spojení; chat komponenta se subscribne na delta eventy daného turnu.
  2. Každý `delta` event apenduje text fragment do asistentské bubliny s plynulým renderem (žádný layout shift / blikání).
  3. Stream zobrazuje „typing" indikátor dokud nedorazí `done`.
  4. `done` event nahradí streamovaný buffer finálním kanonickým textem z backendu (autoritativní) + aktivuje citace a feedback UI.
- **Postcondition / záruky:** vykreslený text == finální backend text (streamovaný buffer je jen progresivní náhled); spojení se po `done` čistě uzavře nebo udrží pro další turn · **Tenancy / permissions:** stream je owner-scoped tokenem server-side (oblast 21) · **Reuse / canonical pattern:** oblast 21, FE jeden SSE provider; streamovaná cesta NEMÁ tool trace (past z MEMORY → assert na TEXT, ne na trace) · **Data dotčena:** žádná (read-only stream) · **Eventy:** SSE `delta`, `done` · **Priorita:** P0

### Edge cases UC-27-02
- **EC-27-02-01 — SSE disconnect během streamu** · Trigger: síť spadne / proxy timeout uprostřed delt · Očekávané chování: provider se reconnectne s `Last-Event-ID`, replayne zameškané eventy (oblast 21 replay), bublina pokračuje bez duplikace · Mechanismus: SSE reconnect + Last-Event-ID replay (oblast 21) · Severity: P1 · Test: e2e — drop spojení mid-stream → reconnect → kompletní odpověď bez dvojitých fragmentů
- **EC-27-02-02 — Disconnect před `done`, ale backend dokončil** · Trigger: klient odpojen, ale handler odpověď uložil (disconnect-safe save, MEMORY past) · Očekávané chování: po reconnectu / re-openu konverzace je odpověď kompletní z perzistovaného stavu · Mechanismus: backend disconnect-safe save + cache refetch · Severity: P1 · Test: e2e — zavřít tab mid-stream, znovu otevřít → odpověď je uložená
- **EC-27-02-03 — Replay duplikuje fragmenty** · Trigger: reconnect bez správného Last-Event-ID · Očekávané chování: žádný zdvojený text; idempotentní apply podle event id · Mechanismus: dedup podle SSE event id (oblast 21) · Severity: P2 · Test: e2e — vynutit replay, assert text bez duplicit
- **EC-27-02-04 — XSS v streamovaném obsahu** · Trigger: model / retrieved chunk obsahuje `<script>` nebo markdown injection · Očekávané chování: obsah se renderuje sanitizovaně (escape / allowlist markdown), žádné spuštění skriptu · Mechanismus: sanitizace renderu (FE XSS taxonomie) · Severity: P0 · Test: e2e — odpověď s `<img onerror>` → neprovede se

---

## UC-27-03 — Inline číslované citace `[1][2]` mapované na zdrojové chunky

- **Actor / role:** uživatel · **Precondition:** odpověď dokončena s citační mapou (oblast 13 `AnswerCitation`) · **Trigger:** render finální asistentské zprávy · **Main flow:**
  1. UI parsuje citační markery (`[1]`, `[2]`, …) v textu odpovědi a páruje je s `AnswerCitation` záznamy (citationIndex → chunkId, docId, page/offset, score).
  2. Každý marker se vykreslí jako interaktivní pill / superscript (klikatelný, klávesnicí fokusovatelný, ARIA popis „source 1, document X").
  3. Pod odpovědí je seznam zdrojů (References) odpovídající číslům.
  4. Hover / focus na marker zvýrazní odpovídající zdroj v seznamu a naopak.
- **Postcondition / záruky:** každé tvrzení nesoucí marker je dohledatelné na konkrétní chunk; čísla v textu == čísla v seznamu zdrojů (žádná osiřelá citace) · **Tenancy / permissions:** citace odkazují jen na chunky z collections, ke kterým má uživatel přístup (vynuceno backendem v retrieval fázi) · **Reuse / canonical pattern:** oblast 13 (answer+citace jako produkt) · **Data dotčena:** `AnswerCitation`, `Chunk` (metadata), `Document` · **Eventy:** žádné · **Priorita:** P0

### Edge cases UC-27-03
- **EC-27-03-01 — Marker bez odpovídajícího zdroje** · Trigger: model halucinuje `[7]`, ale citační mapa má jen 4 zdroje · Očekávané chování: neplatný marker se nerenderuje jako klikatelná citace (degraduje na plain text nebo se skryje), nikdy nevede k prázdnému panelu · Mechanismus: párování podle mapy, fallback na neaktivní text (defense, oblast 13) · Severity: P1 · Test: e2e — odpověď s indexem mimo rozsah → není klikatelná
- **EC-27-03-02 — Zdroj bez markeru v textu** · Trigger: retrieved chunk se dostal do kontextu, ale model ho necitoval · Očekávané chování: zobrazí se v retrieval inspection panelu (UC-27-05) jako „retrieved, neuvedeno", ne v inline References · Mechanismus: oddělení „cited" vs „retrieved" (oblast 13 vs 17) · Severity: P2 · Test: e2e — chunk v kontextu bez markeru → jen v debug panelu
- **EC-27-03-03 — Velký počet citací** · Trigger: odpověď s 20+ markery · Očekávané chování: seznam zdrojů je sbalitelný / scrollovatelný, žádný layout break, deduplikace stejného chunku · Mechanismus: virtualizace/collapse + dedup citationIndex · Severity: P3 · Test: e2e — odpověď s mnoha citacemi → UI stabilní
- **EC-27-03-04 — XSS v textu citace / názvu dokumentu** · Trigger: název dokumentu nebo snippet obsahuje HTML · Očekávané chování: sanitizováno při renderu pill i panelu · Mechanismus: sanitizace (FE taxonomie) · Severity: P0 · Test: e2e — doc název s `<script>` → escapováno

---

## UC-27-04 — Otevření citace side-by-side s přesnou pasáží a highlightem

- **Actor / role:** uživatel · **Precondition:** odpověď obsahuje aspoň jednu platnou citaci · **Trigger:** klik / Enter na inline marker nebo na položku v References · **Main flow:**
  1. UI otevře side-by-side panel (split view na desktopu, bottom sheet / overlay na mobilu) s odpovědí vlevo a zdrojovým chunkem vpravo.
  2. BFF dotáhne přesnou pasáž zdrojového chunku (oblast 13 — chunk text + offsety + page) přes read endpoint (TanStack Query, fetch jednou, reuse při dalším otevření).
  3. Citovaná pasáž se zvýrazní (highlight span) uvnitř širšího kontextu chunku (okolní text pro orientaci).
  4. Panel ukáže metadata: dokument, stránka/sekce, similarity + rerank score, časová značka (freshness, oblast 09 pokud dostupné).
  5. Zavření panelu vrátí fokus zpět na marker (focus management).
- **Postcondition / záruky:** uživatel vidí přesně tu pasáž, na které je tvrzení postaveno (auditovatelnost); žádná pasáž z cizí collection · **Tenancy / permissions:** chunk read je RLS/scope-omezený backendem; cizí chunkId → 404 · **Reuse / canonical pattern:** oblast 13 (citace = produkt), TanStack single data source; FE focus-trap v modalu · **Data dotčena:** `Chunk`, `Document` (read) · **Eventy:** žádné · **Priorita:** P0

### Edge cases UC-27-04
- **EC-27-04-01 — Zdrojový dokument mezitím smazán / GDPR erased** · Trigger: chunk byl odstraněn (oblast 20 GDPR) po vygenerování odpovědi · Očekávané chování: panel ukáže „zdroj již není dostupný" empty state místo crashe; odpověď zůstává čitelná · Mechanismus: 404 handling → empty state (oblast 20 + FE error taxonomie) · Severity: P1 · Test: e2e — citace na smazaný chunk → graceful empty
- **EC-27-04-02 — Highlight offset mimo rozsah** · Trigger: uložené offsety nesedí na aktuální text chunku · Očekávané chování: zobrazí se celý chunk bez highlightu (fallback), žádná chyba renderu · Mechanismus: defenzivní clamp offsetů · Severity: P2 · Test: unit — offset > délka → highlight přeskočen
- **EC-27-04-03 — Focus management v panelu** · Trigger: otevření panelu klávesnicí · Očekávané chování: fokus se přesune do panelu, Esc zavře a vrátí fokus na marker, focus-trap uvnitř · Mechanismus: a11y focus-trap (FE povinné a11y) · Severity: P1 · Test: e2e keyboard — Tab/Esc cyklus korektní
- **EC-27-04-04 — Mobile responsive** · Trigger: otevření citace na úzké obrazovce · Očekávané chování: side-by-side se přepne na full-screen bottom sheet, čitelné, scrollovatelné · Mechanismus: responsive breakpointy (FE) · Severity: P2 · Test: e2e mobile viewport — bottom sheet render

---

## UC-27-05 — Retrieval inspection panel („show retrieved chunks")

- **Actor / role:** uživatel (power user / debugging), volitelně gated `rag.query.inspect` permission · **Precondition:** odpověď dokončena, backend vrátil retrieval trace (oblast 19 observ + oblast 13/17) · **Trigger:** uživatel rozbalí „Show retrieved chunks" panel u odpovědi · **Main flow:**
  1. BFF dotáhne `RagTrace` / retrieval inspection payload pro daný turn (read endpoint, oblast 19).
  2. UI vykreslí seznam kandidátních pasáží: text snippet, similarity score (dense, oblast 05), BM25 / rerank score (oblast 06/08), source dokument + stránka, a **flag zda se dostala do finálního kontextu** vs byla odfiltrována.
  3. Seznam lze řadit podle skóre a filtrovat „jen v kontextu".
  4. Zobrazí použitou pipeline konfiguraci (topK, RRF k=60, rerank on/off, graph on/off) z trace.
- **Postcondition / záruky:** uživatel vidí přesně co retrieval našel a co prošlo do promptu (transparentnost, ladění kvality) · **Tenancy / permissions:** trace je owner-scoped; ukazuje jen chunky, ke kterým má uživatel přístup; inspect může být gated permission · **Reuse / canonical pattern:** oblast 19 (observ trace), oblast 13/17; permission-gated UI (skrýt vs disable) · **Data dotčena:** `RagTrace`, `Chunk` (read) · **Eventy:** žádné · **Priorita:** P1

### Edge cases UC-27-05
- **EC-27-05-01 — Trace nedostupný / vypnutý** · Trigger: backend trace capture vypnutý (config oblast 24) nebo retencí pryč · Očekávané chování: panel ukáže „retrieval trace nedostupný" empty state, ne chybu · Mechanismus: empty state (FE error taxonomie) · Severity: P2 · Test: e2e — bez trace → empty panel
- **EC-27-05-02 — Permission-gated inspect** · Trigger: uživatel bez `rag.query.inspect` · Očekávané chování: panel se vůbec nezobrazí (skrýt, ne disabled bez kontextu) · Mechanismus: permission-gated rendering (FE casing lowercase) · Severity: P2 · Test: e2e — bez permission → panel chybí
- **EC-27-05-03 — Velmi velký kandidátní seznam** · Trigger: topK vysoké, mnoho kandidátů · Očekávané chování: virtualizovaný seznam, žádný freeze · Mechanismus: list virtualization (FE) · Severity: P3 · Test: e2e — 50 chunků → svižný render
- **EC-27-05-04 — XSS ve snippetu chunku** · Trigger: retrieved text obsahuje HTML/markup · Očekávané chování: sanitizováno · Mechanismus: sanitizace · Severity: P0 · Test: e2e — chunk s markupem → escapováno

---

## UC-27-06 — ABSTAIN / „no relevant context" stav (viditelné odmítnutí místo halucinace)

- **Actor / role:** uživatel · **Precondition:** retrieval nenašel dostatečně relevantní kontext (RetrievalStatus indikuje abstain, oblast 17) · **Trigger:** `done` event s abstain stavem / prázdným groundingem · **Main flow:**
  1. Backend (oblast 17 degradation + oblast 13) signalizuje, že nebyl nalezen relevantní kontext → model viditelně odmítne odpovědět.
  2. UI vykreslí výrazný, odlišený abstain blok („Nenašel jsem v dostupných zdrojích relevantní informace k zodpovězení") místo běžné bubliny.
  3. Nabídne akce: upravit dotaz, rozšířit collection scope, přidat dokument (link na ingest oblast 01 UI), nebo zmírnit similarity threshold (per-query knob UC-27-08, pokud povoleno).
  4. NEgeneruje fabrikovanou odpověď ani citace.
- **Postcondition / záruky:** uživatel jednoznačně pozná, že systém odmítl, ne že odpověděl nepodloženě; žádné falešné citace · **Tenancy / permissions:** standardní · **Reuse / canonical pattern:** oblast 17 (RetrievalStatus), oblast 13 (grounding) · **Data dotčena:** `RagTurn` (uložen s abstain stavem) · **Eventy:** SSE `done` s abstain flagem · **Priorita:** P0

### Edge cases UC-27-06
- **EC-27-06-01 — Abstain doprovázený částečnými výsledky** · Trigger: něco se našlo, ale pod prahem relevance · Očekávané chování: jasně odlišit „nedostatečně relevantní" od „nic nenalezeno"; nenabízet částečná tvrzení jako jistá · Mechanismus: RetrievalStatus rozlišení (oblast 17) · Severity: P1 · Test: e2e — borderline relevance → abstain varianta
- **EC-27-06-02 — Abstain vs degradace prázdné collection** · Trigger: uživatel nemá žádný dokument v collection · Očekávané chování: specifický empty state „přidej dokumenty" s CTA na ingest, ne generický abstain · Mechanismus: rozlišení empty corpus vs no-match (oblast 00/17) · Severity: P2 · Test: e2e — prázdná collection → ingest CTA
- **EC-27-06-03 — i18n abstain hláška** · Trigger: cs locale · Očekávané chování: lokalizovaná hláška (nested namespace klíč), žádný raw klíč · Mechanismus: next-intl nested (MEMORY past: ploché klíče blokují) · Severity: P2 · Test: e2e cs — abstain text přeložen

---

## UC-27-07 — Multi-turn konverzace s perzistovanou historií a citacemi

- **Actor / role:** uživatel · **Precondition:** existující `RagConversation` s ≥1 turnem · **Trigger:** uživatel pokračuje v konverzaci nebo ji znovu otevře z historie · **Main flow:**
  1. UI načte seznam konverzací (read endpoint, paged, owner-scoped) a vykreslí historii v postranním panelu.
  2. Otevření konverzace dotáhne všechny `RagTurn` + perzistované `AnswerCitation` + odkaz na retrieved kontext (oblast 13/19) přes TanStack Query.
  3. Re-open zobrazí PŘESNĚ ten retrieved kontext, který byl použit při původní odpovědi (audit/trace) — citace v minulé odpovědi jsou klikatelné a otevírají původní pasáže (UC-27-04).
  4. Nový turn naváže na konverzační kontext (backend skládá historii do promptu, oblast 13/14 prompt cache).
- **Postcondition / záruky:** historie je věrná originálu; staré citace stále dohledatelné; nový turn respektuje konverzační kontext · **Tenancy / permissions:** konverzace owner-scoped (`UserId` z tokenu), cizí conversationId → 404 (RLS, oblast 16) · **Reuse / canonical pattern:** oblast 13 (perzistované citace), oblast 14 (prompt cache), oblast 19 (trace); TanStack invalidace po novém turnu · **Data dotčena:** `RagConversation`, `RagTurn`, `AnswerCitation`, `RagTrace` · **Eventy:** žádné (krom streamu nového turnu) · **Priorita:** P1

### Edge cases UC-27-07
- **EC-27-07-01 — Cizí conversationId v URL** · Trigger: uživatel zkusí přímý odkaz na konverzaci jiného uživatele · Očekávané chování: 404 / not found empty state, žádný leak · Mechanismus: backend RLS owner-scope (oblast 16), FE 404 handling · Severity: P0 · Test: e2e — cizí id → 404
- **EC-27-07-02 — Citace minulého turnu na mezitím smazaný zdroj** · Trigger: GDPR erasure dokumentu po odpovědi · Očekávané chování: stejné jako EC-27-04-01 — graceful „zdroj nedostupný", odpověď čitelná · Mechanismus: 404 → empty (oblast 20) · Severity: P1 · Test: e2e — re-open s erased zdrojem
- **EC-27-07-03 — Velmi dlouhá konverzace** · Trigger: desítky turnů · Očekávané chování: virtualizovaný / lazy-load scroll, stabilní výkon, zachované scroll position · Mechanismus: virtualization + scroll restore (FE) · Severity: P2 · Test: e2e — dlouhá historie svižná
- **EC-27-07-04 — Stale historie po novém turnu** · Trigger: nový turn dokončen · Očekávané chování: postranní seznam + konverzace se invaliduje a ukáže aktuální stav (poslední zpráva, timestamp) · Mechanismus: invalidateQueries po done (Surrounding Concerns #1) · Severity: P2 · Test: e2e — po odpovědi se historie aktualizuje
- **EC-27-07-05 — Concurrent edit konverzace ze dvou tabů** · Trigger: dva taby téhož uživatele píší do stejné konverzace · Očekávané chování: oba turny se uloží konzistentně (backend append-only turny), UI refetch sjednotí pořadí podle timestamp · Mechanismus: server append + refetch ordering (UTC) · Severity: P2 · Test: e2e — dva taby → konzistentní pořadí

---

## UC-27-08 — Per-query knoby z allowlistu (topK, threshold, rerank, graph)

- **Actor / role:** uživatel · **Precondition:** backend povoluje query-override allowlist (oblast 24) · **Trigger:** uživatel otevře „pokročilé nastavení dotazu" a změní knob před odesláním · **Main flow:**
  1. UI vykreslí jen knoby, které jsou na backend allowlistu (oblast 24 query override): topK (slider s horní mezí), similarity threshold, rerank on/off, graph on/off.
  2. Každý knob má v UI horní/dolní mez převzatou z config registru (oblast 24), ale **autoritativní clamp je backend** (oblast 16) — UI mez je jen UX hint.
  3. Hodnoty se přibalí k dotazu jako per-query override; backend je validuje proti allowlistu a clampuje na povolené meze.
  4. Použité (skutečně aplikované) hodnoty se zobrazí v retrieval inspection panelu (UC-27-05) — uživatel vidí, zda byly clampnuty.
- **Postcondition / záruky:** UI knoby NIKDY neobejdou backend mez; změny ovlivní jen daný dotaz, ne perzistentní config · **Tenancy / permissions:** override platí jen pro vlastní dotaz; nemůže zvýšit limit nad systémovou mez (oblast 16) · **Reuse / canonical pattern:** oblast 24 (query override allowlist), oblast 16 (horní meze) · **Data dotčena:** žádná perzistentní (jen request payload) · **Eventy:** žádné · **Priorita:** P1

### Edge cases UC-27-08
- **EC-27-08-01 — UI knob nad backend mez (pokus o obejití)** · Trigger: manipulace requestu / UI hodnota > systémová mez · Očekávané chování: backend clampuje na mez, inspection panel ukáže reálně aplikovanou (nižší) hodnotu; UI to neskrývá · Mechanismus: backend clamp autoritativní (oblast 16) · Severity: P0 · Test: integration — topK=9999 → clampnuto na max
- **EC-27-08-02 — Knob mimo allowlist** · Trigger: pokus poslat override neuvedený na allowlistu · Očekávané chování: backend ho ignoruje / odmítne (ValidationException), UI ho ani nenabízí · Mechanismus: allowlist enforcement (oblast 24) · Severity: P1 · Test: integration — neznámý knob → ignorován/odmítnut
- **EC-27-08-03 — Reset na default** · Trigger: uživatel chce zpět výchozí · Očekávané chování: tlačítko Reset vrátí knoby na config default (oblast 24) · Mechanismus: default z registru · Severity: P3 · Test: e2e — Reset → default hodnoty
- **EC-27-08-04 — Persistence preferencí mezi dotazy** · Trigger: uživatel chce knoby držet pro session · Očekávané chování: hodnoty se drží v UI stavu session-scoped (ne perzistentní config), jasně odlišeno od globálního nastavení · Mechanismus: client session state · Severity: P3 · Test: e2e — knob přežije další dotaz ve stejné session

---

## UC-27-09 — Metadata filtry v dotazu (dokument / tag / datum / source)

- **Actor / role:** uživatel · **Precondition:** collection má dokumenty s metadaty (tagy, datum, source) · **Trigger:** uživatel přidá filtr před odesláním dotazu · **Main flow:**
  1. UI nabídne filtry: výběr dokumentu/ů, tagů, datumový rozsah (UTC), source typ — naplněné z read endpointu metadat collection.
  2. Filtry se přibalí k dotazu; backend je aplikuje na retrieval scope (zúží kandidáty před dense/BM25/RRF).
  3. Aktivní filtry se zobrazí jako odebíratelné chipy nad chat inputem.
  4. Retrieval inspection (UC-27-05) ukáže, že kandidáti respektují filtr.
- **Postcondition / záruky:** odpověď je groundovaná jen na dokumenty splňující filtr; filtr nemůže rozšířit scope mimo přístupné collections · **Tenancy / permissions:** filtr operuje uvnitř owner/tenant scope (oblast 16); nemůže obejít isolation · **Reuse / canonical pattern:** oblast 13/05/06 (retrieval scope), oblast 00 (collection metadata) · **Data dotčena:** žádná perzistentní · **Eventy:** žádné · **Priorita:** P2

### Edge cases UC-27-09
- **EC-27-09-01 — Filtr vyloučí vše** · Trigger: kombinace filtrů bez shody · Očekávané chování: abstain / empty „žádné dokumenty neodpovídají filtru" (UC-27-06 varianta), ne halucinace · Mechanismus: empty retrieval → abstain (oblast 17) · Severity: P1 · Test: e2e — nemožný filtr → empty state
- **EC-27-09-02 — Datumový rozsah timezone** · Trigger: uživatel zadá lokální datum · Očekávané chování: převedeno na UTC konzistentně s backendem (vše UTC) · Mechanismus: UTC normalizace (battle-tested TZ knihovna, ne ruční) · Severity: P2 · Test: unit — lokální datum → správný UTC rozsah
- **EC-27-09-03 — Filtr na cizí dokument** · Trigger: pokus filtrovat na docId mimo scope · Očekávané chování: backend ho nezahrne (404/scope), UI ho ani nenabídne · Mechanismus: scope enforcement (oblast 16) · Severity: P1 · Test: integration — cizí docId filtr → ignorován
- **EC-27-09-04 — Stale metadata seznam** · Trigger: nový dokument přidán po načtení filtrů · Očekávané chování: refresh filtrů po ingest (invalidace) nebo manuální reload · Mechanismus: invalidateQueries po ingest (oblast 01 + Surrounding Concerns #1) · Severity: P3 · Test: e2e — po ingestu se tag objeví ve filtru

---

## UC-27-10 — RetrievalStatus banner (Partial / Degraded) + potlačení auto-akcí

- **Actor / role:** uživatel · **Precondition:** backend vrátí degradovaný `RetrievalStatus` (oblast 17 — např. rerank vypadl, graph nedostupný, jen lexikál) · **Trigger:** `done` event s Partial/Degraded statusem · **Main flow:**
  1. UI nad odpovědí vykreslí informační banner: „Částečné výsledky — rerank nedostupný" / „Degradovaný režim" s vysvětlením, co chybělo.
  2. V degradovaném stavu se POTLAČÍ automatické akce, které by braly odpověď jako plně spolehlivou (např. auto-follow-up, auto-export, auto-feedback positivní).
  3. Banner nabídne „zkusit znovu" (re-run dotazu, až bude komponenta zpět) a odkaz na status detail (oblast 17).
  4. Odpověď se i tak zobrazí s citacemi, ale vizuálně odlišená jako částečná.
- **Postcondition / záruky:** uživatel ví, že odpověď vznikla v degradovaném režimu; žádná tichá auto-akce nad nespolehlivým výstupem · **Tenancy / permissions:** standardní · **Reuse / canonical pattern:** oblast 17 (RetrievalStatus, suppress auto-actions) · **Data dotčena:** `RagTurn` (uložen se statusem) · **Eventy:** SSE `done` se statusem · **Priorita:** P1

### Edge cases UC-27-10
- **EC-27-10-01 — Status se změní mezi turny** · Trigger: rerank se obnoví v dalším dotazu · Očekávané chování: banner jen u dotčeného turnu, ne globálně; nový turn bez banneru · Mechanismus: per-turn status (oblast 17) · Severity: P2 · Test: e2e — degradovaný + následný zdravý turn
- **EC-27-10-02 — Plně Failed retrieval** · Trigger: retrieval kompletně selže (ne jen partial) · Očekávané chování: error/abstain stav místo banneru nad „odpovědí"; nenabízet částečnou odpověď jako validní · Mechanismus: rozlišení Degraded vs Failed (oblast 17) · Severity: P0 · Test: e2e — failed retrieval → error stav
- **EC-27-10-03 — i18n banner + a11y** · Trigger: cs locale, screen reader · Očekávané chování: lokalizovaný text, banner oznámen jako `role=status`/`alert`, kontrast splněn · Mechanismus: next-intl nested + ARIA (FE a11y) · Severity: P2 · Test: e2e — SR oznámí banner, kontrast OK

---

## UC-27-11 — Thumbs up/down + flag feedback na odpovědi

- **Actor / role:** uživatel · **Precondition:** dokončený `RagTurn` s odpovědí · **Trigger:** uživatel klikne thumbs up/down nebo „flag" na odpovědi · **Main flow:**
  1. UI ukáže feedback kontrolky pod odpovědí (po `done`).
  2. Klik na thumbs odešle feedback přes BFF na backend feedback endpoint (oblast 32) s turnId + sentiment; optimistic update zvýrazní vybranou volbu.
  3. „Flag" otevře krátký formulář (kategorie: nepřesné / chybí zdroj / nevhodné + volitelný komentář), odešle se jako strukturovaný feedback (oblast 32).
  4. Po úspěchu toast potvrzení; feedback je perzistovaný a párovaný s turnem + traceId (pro eval oblast 18).
- **Postcondition / záruky:** feedback uložen a navázán na konkrétní turn/trace (umožní online eval oblast 18); identita feedbacku z tokenu · **Tenancy / permissions:** feedback owner-scoped, identita z tokenu server-side (nikdy z body) · **Reuse / canonical pattern:** oblast 32 (feedback), oblast 18 (online eval); FE optimistic+rollback · **Data dotčena:** feedback záznam (oblast 32), odkaz na `RagTurn`/`RagTrace` · **Eventy:** dle oblasti 32 (možný feedback event) · **Priorita:** P2

### Edge cases UC-27-11
- **EC-27-11-01 — Feedback submit selže** · Trigger: síťová chyba při odeslání · Očekávané chování: optimistic stav se rollbackne, toast s chybou, možnost retry · Mechanismus: optimistic + rollback (FE, Surrounding Concerns #8) · Severity: P2 · Test: e2e — server 500 → rollback + retry
- **EC-27-11-02 — Změna / odebrání hlasu** · Trigger: uživatel přepne up→down nebo zruší · Očekávané chování: idempotentní update (jeden hlas na uživatele/turn), backend přepíše předchozí · Mechanismus: idempotentní feedback (oblast 32) · Severity: P3 · Test: e2e — toggle hlasu → jeden záznam
- **EC-27-11-03 — Double-submit flag** · Trigger: rychlé dvojí odeslání flag formuláře · Očekávané chování: submit disabled po odeslání, jeden záznam · Mechanismus: double-submit guard (FE) · Severity: P2 · Test: e2e — dvojí submit → jeden flag
- **EC-27-11-04 — XSS v komentáři flagu (zpětný render)** · Trigger: komentář s HTML zobrazený adminovi (oblast 23/32) · Očekávané chování: sanitizováno při renderu · Mechanismus: sanitizace · Severity: P1 · Test: e2e — komentář s markupem → escapováno

---

## UC-27-12 — Cancel / stop probíhajícího streamu

- **Actor / role:** uživatel · **Precondition:** aktivní SSE stream odpovědi (UC-27-02) · **Trigger:** uživatel klikne „Stop" během streamování · **Main flow:**
  1. UI zobrazí „Stop" tlačítko místo Send po dobu aktivního streamu.
  2. Klik abortuje SSE spojení client-side a signalizuje backendu zrušení (disconnect-safe — backend uloží to, co stihl, oblast 21).
  3. UI ukáže částečnou odpověď s indikací „zastaveno uživatelem" a uvolní input pro další dotaz.
  4. Částečná odpověď se buď perzistuje (s flagem), nebo zahodí — dle backend kontraktu (oblast 21).
- **Postcondition / záruky:** stream čistě ukončen, žádné visící spojení; UI konzistentní; další dotaz možný · **Tenancy / permissions:** standardní · **Reuse / canonical pattern:** oblast 21 (disconnect-safe save) · **Data dotčena:** `RagTurn` (případně částečný) · **Eventy:** SSE abort · **Priorita:** P2

### Edge cases UC-27-12
- **EC-27-12-01 — Stop těsně před `done`** · Trigger: race mezi Stop a příchodem `done` · Očekávané chování: pokud `done` dorazí, zobrazí se kompletní odpověď; jinak částečná — žádný nekonzistentní mix · Mechanismus: stav-machine guard streamu · Severity: P2 · Test: e2e — Stop v poslední chvíli → konzistentní výsledek
- **EC-27-12-02 — Stop a hned nový dotaz** · Trigger: uživatel po Stopu hned píše · Očekávané chování: nové spojení korektně otevřeno, staré plně uvolněno (žádný leak listenerů) · Mechanismus: cleanup SSE provider · Severity: P1 · Test: e2e — Stop → nový dotaz → jen jeden aktivní stream
- **EC-27-12-03 — Cost účtování při Stopu** · Trigger: zastavení po část. generování · Očekávané chování: cost se účtuje za skutečně spotřebované tokeny (oblast 19/22), ne za celý odhad · Mechanismus: backend cost tracking (oblast 19) · Severity: P2 · Test: integration — částečný stream → částečný cost

---

## UC-27-13 — Zobrazení per-query cost a rate-limit zpětné vazby

- **Actor / role:** uživatel · **Precondition:** backend vrací cost/usage info (oblast 19) a vynucuje rate-limit + Redis cost bucket (oblast 22) · **Trigger:** dokončení dotazu nebo zásah limitu · **Main flow:**
  1. Po `done` UI volitelně ukáže usage (tokeny, odhad ceny) z trace (oblast 19) — diskrétně, např. v inspection panelu.
  2. Při dosažení rate-limitu / vyčerpání cost bucketu (oblast 22) backend vrátí 429 + `Retry-After`; UI ukáže jasnou hlášku „dosažen limit dotazů, zkuste za X" a dočasně zamkne Send.
  3. Odpočet do obnovení se zobrazí (z `Retry-After`).
- **Postcondition / záruky:** uživatel ví, kolik dotaz stál a kdy může pokračovat; žádný tichý fail · **Tenancy / permissions:** limit per-user (oblast 22); cost owner-scoped · **Reuse / canonical pattern:** oblast 19 (cost), oblast 22 (rate-limit + Redis cost bucket); FE centrální 429 handling · **Data dotčena:** žádná (read usage) · **Eventy:** žádné · **Priorita:** P2

### Edge cases UC-27-13
- **EC-27-13-01 — 429 uprostřed psaní** · Trigger: limit vyčerpán předchozími dotazy · Očekávané chování: Send disabled s odpočtem `Retry-After`, draft zachován · Mechanismus: 429 + Retry-After handling (oblast 22, FE) · Severity: P1 · Test: e2e — vyčerpat limit → odpočet + draft
- **EC-27-13-02 — Cost bucket vyčerpán (rozdíl od rate-limit)** · Trigger: dosažen měsíční/denní cost strop · Očekávané chování: odlišná hláška „rozpočet vyčerpán" s odkazem na billing/admin, ne jen „zkuste později" · Mechanismus: rozlišení rate-limit vs cost bucket (oblast 22) · Severity: P2 · Test: e2e — cost strop → správná hláška
- **EC-27-13-03 — Usage info chybí** · Trigger: trace bez cost dat · Očekávané chování: usage sekce se skryje, žádná chyba · Mechanismus: graceful absence · Severity: P3 · Test: e2e — bez usage → sekce skrytá

---

## UC-27-14 — Volba aktivní collection / scope pro dotaz

- **Actor / role:** uživatel · **Precondition:** uživatel má přístup k ≥1 collection (Scope=User a/nebo Tenant) · **Trigger:** uživatel vybere/přepne collection scope před dotazem · **Main flow:**
  1. UI načte seznam dostupných collections (read endpoint, oblast 00) rozlišený podle Scope (User vs Tenant) a vykreslí selektor.
  2. Uživatel zvolí, nad kterou collection (nebo kombinací povolených) se dotaz spustí.
  3. Volba se přibalí k dotazu; backend ji ověří proti přístupu uživatele (oblast 16).
  4. Aktivní scope se zobrazí jako kontext nad chatem (chip / header).
- **Postcondition / záruky:** dotaz běží jen nad zvolenou přístupnou collection; default je rozumný (např. poslední použitá / všechny přístupné) · **Tenancy / permissions:** výběr nemůže zahrnout collection bez přístupu; Tenant-scope viditelný jen členům tenanta (oblast 16) · **Reuse / canonical pattern:** oblast 00 (collections), oblast 16 (isolation) · **Data dotčena:** žádná perzistentní (volba session/request) · **Eventy:** žádné · **Priorita:** P2

### Edge cases UC-27-14
- **EC-27-14-01 — Žádná collection** · Trigger: nový uživatel bez collections · Očekávané chování: empty state s CTA „vytvořit collection / nahrát dokument" (oblast 00/01), chat input disabled · Mechanismus: empty state (FE) · Severity: P1 · Test: e2e — bez collection → onboarding CTA
- **EC-27-14-02 — Collection smazána během session** · Trigger: aktivní collection odstraněna jinde · Očekávané chování: selektor se invaliduje, dotaz nad smazanou → 404/scope error → výzva zvolit jinou · Mechanismus: invalidace + 404 handling (oblast 00/16) · Severity: P2 · Test: e2e — smazat aktivní → graceful přepnutí
- **EC-27-14-03 — Pokus o dotaz nad cizí Tenant collection** · Trigger: manipulace requestu s cizím collectionId · Očekávané chování: backend 404 (RLS/scope), žádný leak; UI ji ani nenabízí · Mechanismus: scope enforcement (oblast 16) · Severity: P0 · Test: integration — cizí collectionId → 404
- **EC-27-14-04 — Stale collection seznam po ingestu** · Trigger: nová collection vytvořena · Očekávané chování: selektor se po vytvoření invaliduje a ukáže ji (Surrounding Concerns #1) · Mechanismus: invalidateQueries · Severity: P3 · Test: e2e — nová collection se objeví v selektoru

---

## UC-27-15 — Kopírování / sdílení odpovědi se zachovanými citacemi

- **Actor / role:** uživatel · **Precondition:** dokončená odpověď s citacemi · **Trigger:** uživatel klikne „kopírovat odpověď" / „sdílet" · **Main flow:**
  1. UI nabídne kopii odpovědi v čitelném formátu (text + očíslovaný seznam zdrojů s názvy dokumentů a stránkami), nikoli interní chunkId.
  2. Citační markery zůstanou jako `[1][2]` s legendou zdrojů na konci.
  3. Volitelně „sdílet konverzaci" vygeneruje owner-scoped odkaz (jen pokud backend share endpoint existuje; jinak feature skryta).
- **Postcondition / záruky:** zkopírovaný text je sebevysvětlující a auditovatelný (zdroje uvedeny); neexportuje interní identifikátory ani PII nad rámec oprávnění · **Tenancy / permissions:** sdílení respektuje scope; žádný cross-user leak · **Reuse / canonical pattern:** oblast 13 (citace), oblast 20 (GDPR — neexportovat erased PII) · **Data dotčena:** žádná (read) · **Eventy:** žádné · **Priorita:** P3

### Edge cases UC-27-15
- **EC-27-15-01 — Kopie obsahuje smazaný zdroj** · Trigger: citace na erased dokument · Očekávané chování: v kopii „[zdroj nedostupný]" místo PII · Mechanismus: GDPR-aware render (oblast 20) · Severity: P2 · Test: unit — erased zdroj → placeholder v kopii
- **EC-27-15-02 — Clipboard API nedostupné** · Trigger: prohlížeč bez clipboard permission · Očekávané chování: fallback (výběr textu / hláška), žádný tichý fail · Mechanismus: graceful fallback · Severity: P3 · Test: e2e — bez clipboard → fallback
- **EC-27-15-03 — Share feature gated** · Trigger: backend share endpoint neexistuje/nepovolen · Očekávané chování: tlačítko sdílet skryto (ne disabled bez kontextu) · Mechanismus: permission/feature-gated rendering · Severity: P3 · Test: e2e — bez share → tlačítko chybí

---

## Cross-cutting UI edge cases (platí napříč UC-27-01 až UC-27-15)

### Edge cases CC-27
- **EC-27-CC-01 — Dark mode konzistence** · Trigger: přepnutí dark mode během chatu · Očekávané chování: bubliny, citace, highlighty, banner i inspection panel mají korektní kontrast; žádný hydration warning (MEMORY past: next-themes scriptProps) · Mechanismus: next-themes + design tokeny (FE) · Severity: P2 · Test: e2e — dark mode toggle → kontrast OK, 0 hydration warning
- **EC-27-CC-02 — Hydration mismatch streamovaného obsahu** · Trigger: SSR vs client render chat historie · Očekávané chování: žádný hydration error; streamovaný obsah jen client-side · Mechanismus: client boundary pro stream (FE) · Severity: P1 · Test: e2e — 0 hydration warnings v konzoli
- **EC-27-CC-03 — i18n chybějící klíč** · Trigger: nepřeložený nested klíč v cs · Očekávané chování: fallback na en, NIKDY raw tečkový klíč v UI (MEMORY past: raw klíče → dev overlay blokuje kliky) · Mechanismus: next-intl nested namespace + fallback · Severity: P1 · Test: e2e cs — žádný raw klíč v DOM
- **EC-27-CC-04 — Keyboard-only navigace celého flow** · Trigger: uživatel bez myši · Očekávané chování: input, Send, citace markery, inspection toggle, feedback, panel — vše dosažitelné a ovladatelné klávesnicí s viditelným focusem · Mechanismus: a11y focus management (FE povinné) · Severity: P1 · Test: e2e keyboard — celý dotaz→odpověď→citace bez myši
- **EC-27-CC-05 — Centrální RFC9457 error → toast/boundary** · Trigger: jakákoli backend chyba (kromě 401/429 řešených zvlášť) · Očekávané chování: jednotný toast s lokalizovanou hláškou z errorCode (oblast 13/24 SharedResource), kritická chyba → error boundary místo crashe · Mechanismus: centrální RFC9457 handling (FE) · Severity: P1 · Test: e2e — vynutit 500 → toast, ne white screen
- **EC-27-CC-06 — Permission `rag.query` chybí** · Trigger: uživatel bez query permission otevře RAG sekci · Očekávané chování: celá chat plocha gated (skrýt vstup + ukázat „nemáte oprávnění"), nav položka skryta dle entitlement (casing lowercase) · Mechanismus: permission-gated rendering (oblast 16, FE casing) · Severity: P1 · Test: e2e — bez `rag.query` → sekce nedostupná


---

## Doplňky z completeness review
- **EC-27-02-05 — Stream stall / idle (žádná delta ani `done`)** · Trigger: backend přestane streamovat a `done` nedorazí (model hang / upstream timeout, ne čistý disconnect) · Očekávané chování: po idle timeoutu UI ukončí „thinking" stav, zobrazí timeout hlášku + Stop/Zkusit znovu; žádné nekonečné točení; částečná odpověď zachována/označena jako neúplná · Mechanismus: client-side SSE idle watchdog + oblast 21/17 (degradace) · Severity: P1 · Test: e2e — pozdrž stream bez `done` → timeout UI, ne věčné thinking.
- **EC-27-02-06 — Markdown obrázek/odkaz auto-načítá vzdálený zdroj (exfiltrace/tracking)** · Trigger: odpověď/chunk obsahuje `![](http://attacker/pixel)` nebo remote `<img src>` · Očekávané chování: sanitizovaný render NEnačítá libovolné vzdálené obrázky (úniku referreru / tracking pixel) — strip / allowlist / proxy; odkazy `rel=noopener noreferrer`, žádný auto-fetch cizí domény · Mechanismus: sanitizace markdownu (FE XSS/exfil taxonomie) + CSP · Severity: P1 · Test: e2e — odpověď s remote image → 0 outbound požadavků na cizí doménu.
- **EC-27-04-05 — Klik na citaci `[n]` otevře PŘESNĚ chunk dle citationIndex (ne poziční)** · Trigger: odpověď s přeházenými / duplicitními / chybějícími indexy (`[3][1][3]`) · Očekávané chování: klik na marker se resolvuje přes mapu `citationIndex → chunkId` z `AnswerCitation`, NIKDY přes pořadí v poli (žádný off-by-one); `[3]` vždy otevře chunk s indexem 3 · Mechanismus: oblast 13 citační mapa (klíč = index, ne pozice) · Severity: P1 · Test: e2e — neseřazené indexy → každý marker otevře správný chunkId, ne sousední.
