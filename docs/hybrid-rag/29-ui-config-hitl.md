# Oblast 29 — UI — Configuration/tuning panel & HITL review console

Tato oblast pokrývá **frontendovou vrstvu** pro (a) ladění konfigurace RAG pipeline a (b) human-in-the-loop (HITL) review konzoli. Vše je čistě **Next.js 16 App Router** UI nad existujícími backendovými endpointy — UI **nikdy nedefinuje business logiku, validaci ani izolaci**; ta je výhradně na backendu. UI pouze **konzumuje** read/write endpointy z oblastí 24 (config registry), 32 (HITL/review), 11 (entity-res review queue), 18 (eval/golden set) a 31 (routing policy / A-B experiment). Identita a tenant se řeší **server-side přes BFF Model A** (tokeny pouze v route handlers / server actions, nikdy v prohlížeči).

**Frozen předpoklady pro celou oblast:**
- BFF auth Model A — tokeny pouze server-side; identita z tokenu (`ITenantContext.UserId`), nikdy z body/LLM.
- TanStack Query = jediný data source; po mutaci `invalidateQueries`.
- Jeden SSE realtime provider (konzumuje `/v1/hybridrag/.../stream`), reconnect přes `Last-Event-ID`.
- Centrální error handling: RFC9457 → toast / error boundary.
- i18n en+cs přes next-intl, NESTED namespace klíče (žádné ploché tečkové klíče).
- a11y povinné (keyboard nav, screen reader, focus management, kontrast), responsive, dark mode, hydration-safe.
- Permission-gating: `RagManage` (config/tuning), `rag.review.approve` / `rag.review.label` (HITL). Permission-gated prvky se **skrývají** (ne disable) pokud uživatel nemá ani read právo; pokud má read ale ne write, prvek je **disabled** s tooltipem.
- Secrets (API klíče Cohere/Claude/embedding) se v UI **NIKDY** nezobrazují plaintextem — vždy maskované (`••••••••1234`), editace = write-only pole.
- Nav entitlement casing lowercase (`hybridrag`).

---

## UC-29-01 — Parameter registry editor: zobrazení všech laditelných knobů

- **Actor / role:** Tenant admin / RAG operátor s `RagManage` · **Precondition:** modul `hybridrag` je entitled pro tenant; uživatel přihlášen, token nese `RagManage` · **Trigger:** uživatel otevře `/hybridrag/settings` (nav položka "RAG → Configuration") · **Main flow:**
  1. Server component přes BFF zavolá read endpoint oblasti 24 (`GET /v1/hybridrag/settings/registry`) — vrací **schéma všech knobů** (key, group, scope-level kde lze override, default, datový typ, min/max/enum range, jednotka, `RuntimeApplicable` vs `RestartRequired`, popis, zda je secret).
  2. Paralelně `GET /v1/hybridrag/settings/effective?scope=tenant` → aktuální efektivní hodnoty + odkud override pochází.
  3. TanStack Query zhydratuje cache (`['rag','settings','registry']` + `['rag','settings','effective','tenant']`); klient vyrenderuje seskupený seznam (akordeon po skupinách: Retrieval, Embedding, Rerank, Chat, Freshness, Graph, Cost, …).
  4. Každý knob = řádek: název + tooltip popisu, badge scope (Tenant/Collection), badge `runtime` vs `restart`, aktuální hodnota, indikátor zda je hodnota default nebo override.
  5. Secret knoby vyrenderují maskovanou hodnotu, nikdy plaintext.
- **Postcondition / záruky:** read-only zobrazení; žádná mutace; UI odráží přesně backendový registr (žádný hardcoded seznam knobů na FE — registr je single source of truth). · **Tenancy / permissions:** server-side BFF filtruje na tenant z tokenu; bez `RagManage` se nav položka i route skryje (403 → redirect na dashboard). · **Reuse / canonical pattern:** frontend-feature-slice skill; oblast 24 (config registry endpointy); konzumace jako billing settings page. · **Data dotčena:** `RagSetting` (čtení), registr schématu (oblast 24). · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-29-01
- **EC-29-01-01 — Prázdný registr / modul disabled** · Trigger: tenant nemá `hybridrag` entitled nebo registr vrátí prázdno · Očekávané chování: nav položka se vůbec nezobrazí (entitlement guard), přímý vstup na URL → 404/redirect; pokud entitled ale registr prázdný → empty state „Žádné konfigurovatelné parametry". · Mechanismus: entitlement casing lowercase + oblast 16 izolace · Severity: P2 · Test: e2e bez entitlementu → nav nezobrazena; route 404.
- **EC-29-01-02 — Loading / skeleton** · Trigger: pomalý registry fetch · Očekávané chování: skeleton řádky per skupina, ne prázdná stránka; žádný layout shift po dohrání. · Mechanismus: TanStack `isPending` + skeleton komponenty · Severity: P3 · Test: throttled network → skeleton viditelný.
- **EC-29-01-03 — Registr přidá nový knob (FE neumí typ)** · Trigger: backend přidá knob s neznámým UI typem (např. nový `json` editor) · Očekávané chování: graceful fallback na read-only JSON/text zobrazení + disabled edit s tooltipem „Tento parametr lze upravit jen přes API", ne crash. · Mechanismus: type-driven render s default case · Severity: P2 · Test: mock registr s neznámým typem → fallback render.
- **EC-29-01-04 — i18n chybějící klíč popisu knobu** · Trigger: nový knob bez cs překladu · Očekávané chování: fallback na en popis (popisy knobů jdou z backendu, ne z next-intl bundle — tj. en/cs varianta z registru, jinak en), nikdy raw klíč ani dev overlay blokující kliky. · Mechanismus: backend dodává lokalizovaný popis; FE i18n jen pro chrome (labely UI) s NESTED klíči · Severity: P2 · Test: cs locale, knob bez cs popisu → en popis.
- **EC-29-01-05 — Secret knob maskování** · Trigger: knob označený `IsSecret` · Očekávané chování: hodnota maskovaná `••••••••`, žádný plaintext ani v DOM/network response (backend posílá masku, ne hodnotu); copy do schránky zakázané. · Mechanismus: backend nikdy nevrací plaintext secret; FE jen render masky · Severity: P0 · Test: network inspekce response neobsahuje plaintext; DOM neobsahuje secret.

---

## UC-29-02 — Editace per-tenant override jednoho knobu s validací

- **Actor / role:** Tenant admin s `RagManage` · **Precondition:** UC-29-01 načteno; knob je `ScopeLevel ⊇ Tenant` · **Trigger:** uživatel klikne na řádek knobu → inline editor (number input / slider / select / toggle dle typu) · **Main flow:**
  1. Editor předvyplní aktuální efektivní hodnotu; zobrazí default jako placeholder/hint a range jako min/max na slideru.
  2. Klientská **měkká validace** (zrcadlí backend range pro UX, NENÍ autoritou): out-of-range → inline chyba, Save disabled.
  3. Uložení → server action / route handler `PUT /v1/hybridrag/settings/{key}` s `{ scope: "tenant", value }` (oblast 24); identita/tenant doplní BFF server-side.
  4. **Optimistic update** efektivní hodnoty + okamžitý „override" badge; spinner na tlačítku, double-submit guard.
  5. Po 200: success toast „Parametr uložen", `invalidateQueries(['rag','settings','effective','tenant'])` → refetch potvrdí; pokud knob je `RestartRequired`, zobraz info banner (viz UC-29-05).
  6. Po chybě (422 z backendu — autoritativní validace): rollback optimistic, RFC9457 detail → inline pole + toast.
- **Postcondition / záruky:** override perzistován jen po backendovém 200; FE nikdy neobchází backend validaci; audit (oblast 25) zaznamenán backendem. · **Tenancy / permissions:** `RagManage`; bez write práva je editor disabled s tooltipem. · **Reuse / canonical pattern:** optimistic+rollback z modularplatform-frontend; oblast 24 write endpoint; RFC9457 error handling. · **Data dotčena:** `RagSetting` (insert/update override řádku). · **Eventy:** případně `RagSettingChanged` (oblast 24/25) — FE nekonzumuje přímo, jen invaliduje. · **Priorita:** P1

### Edge cases UC-29-02
- **EC-29-02-01 — Klientská vs serverová validace rozejde** · Trigger: FE měkká validace projde, backend 422 (přísnější/jiný range po změně registru) · Očekávané chování: rollback optimistic, zobraz backend chybu jako autoritu, nikdy netvrď „uloženo". · Mechanismus: backend = autorita validace (zákon: UI nedefinuje validaci) · Severity: P1 · Test: mock 422 → rollback + chyba zobrazena.
- **EC-29-02-02 — Concurrent edit (dva admini)** · Trigger: dva tenant admini upraví stejný knob současně · Očekávané chování: backend optimistic concurrency (xmin, oblast 24) → druhý dostane 409; FE zobrazí „Hodnota byla mezitím změněna, načítám aktuální" + refetch, nepřepíše naslepo. · Mechanismus: oblast 24 concurrency + ConcurrencyRetry; FE 409 handling · Severity: P2 · Test: simulovaný 409 → refetch + varování.
- **EC-29-02-03 — Reset na default** · Trigger: uživatel klikne „Reset na default" · Očekávané chování: `DELETE /v1/hybridrag/settings/{key}?scope=tenant` → override smazán, efektivní hodnota spadne na nižší scope/default; badge override zmizí, invalidate. · Mechanismus: oblast 24 delete override · Severity: P2 · Test: reset → hodnota = default, badge pryč.
- **EC-29-02-04 — Double-submit** · Trigger: rychlý dvojklik na Save · Očekávané chování: druhý submit ignorován (tlačítko disabled během requestu), jeden PUT. · Mechanismus: double-submit guard (loading state) · Severity: P2 · Test: dvojklik → 1 network call.
- **EC-29-02-05 — Session expiry uprostřed editace** · Trigger: token vyprší před Save · Očekávané chování: BFF vrátí 401 → centrální handler → redirect na login s návratovou URL, rozdělaná hodnota neztracena (draft v lokálním stavu / varování). · Mechanismus: centrální 401 handling, BFF refresh · Severity: P2 · Test: expirovaný token → redirect, ne tichý fail.
- **EC-29-02-06 — Secret knob editace (write-only)** · Trigger: editace secret knobu (např. Cohere API key override) · Očekávané chování: pole je prázdné write-only, ne předvyplněné maskou; uložení pošle novou hodnotu jen pokud je vyplněno, jinak ponechá; po uložení zpět maska. · Mechanismus: write-only secret pattern; backend nikdy nevrací plaintext · Severity: P0 · Test: edit secret → response/DOM bez plaintextu; prázdné pole neuloží prázdno.

---

## UC-29-03 — Per-collection override knobu

- **Actor / role:** Tenant admin / kolekce owner s `RagManage` · **Precondition:** existuje `KnowledgeCollection`; knob má `ScopeLevel ⊇ Collection` · **Trigger:** uživatel v detailu kolekce (`/hybridrag/collections/{id}/settings`) nebo v registry editoru přepne scope selektor na konkrétní kolekci · **Main flow:**
  1. Scope selector (Tenant ↔ Collection X) přepne kontext; fetch `GET /v1/hybridrag/settings/effective?scope=collection&collectionId={id}`.
  2. Editor ukazuje pro každý knob: efektivní hodnotu + **z jakého scope vyhrál** (Collection > Tenant > Default) — viz UC-29-04.
  3. Override na úrovni kolekce přes `PUT …/settings/{key}` s `{ scope:"collection", collectionId, value }`.
  4. Optimistic + invalidate `['rag','settings','effective','collection',id]`.
- **Postcondition / záruky:** collection override má přednost nad tenant; backend resolost; FE jen zobrazuje výsledek. · **Tenancy / permissions:** `RagManage` + collection musí patřit do tenantu (backend RLS/izolace oblast 16). · **Reuse / canonical pattern:** oblast 24 scope resolution; UC-29-02 editor reuse. · **Data dotčena:** `RagSetting` (collection-scoped řádek). · **Eventy:** žádné FE-konzumované. · **Priorita:** P2

### Edge cases UC-29-03
- **EC-29-03-01 — Knob nepodporuje collection scope** · Trigger: knob je jen Tenant-scoped, ale UI je v collection kontextu · Očekávané chování: knob zobrazen jako read-only s poznámkou „Nastavuje se na úrovni tenantu", odkaz na tenant editor; žádný collection editor. · Mechanismus: `ScopeLevel` z registru řídí editovatelnost · Severity: P2 · Test: tenant-only knob v collection view → read-only.
- **EC-29-03-02 — Smazaná kolekce uprostřed** · Trigger: kolekce smazána jiným uživatelem během editace settings · Očekávané chování: PUT vrátí 404 → toast „Kolekce již neexistuje" + redirect na seznam kolekcí, invalidate. · Mechanismus: oblast 00 delete + 404 handling · Severity: P3 · Test: 404 na collection settings → redirect.
- **EC-29-03-03 — Cross-tenant collection id (IDOR pokus)** · Trigger: ručně podstrčené cizí `collectionId` do URL/requestu · Očekávané chování: backend RLS → 404 (ne 403, neúnik existence); FE zobrazí not-found. · Mechanismus: oblast 16 izolace; identita z tokenu · Severity: P0 · Test: cizí collectionId → 404, žádná data.

---

## UC-29-04 — Effective-config viewer ("proč tahle odpověď")

- **Actor / role:** RAG operátor s `RagManage` (debug) · **Precondition:** existuje resolost config pro daný scope · **Trigger:** uživatel otevře „Effective config" panel (samostatně nebo z trace detailu odpovědi, oblast 19/13) · **Main flow:**
  1. Fetch `GET /v1/hybridrag/settings/effective?scope=…&explain=true` → pro každý knob: finální hodnota + **resolution chain** (které scope-vrstvy existují a která vyhrála: Collection=X → vyhrál, Tenant=Y, Default=Z).
  2. Render tabulka: knob | efektivní hodnota | zdroj (badge Collection/Tenant/Default) | rozbalitelný chain.
  3. Filtr „jen overridnuté" / „jen restart-required" / fulltext po knobu.
  4. Z trace detailu konkrétní odpovědi (oblast 13/19) lze otevřít viewer **se snapshotem configu, který byl použit pro tu odpověď** (read-only, point-in-time) — pro debug „proč tahle odpověď vypadá takhle".
- **Postcondition / záruky:** čistě read-only debug nástroj; pomáhá vysvětlit chování pipeline. · **Tenancy / permissions:** `RagManage`; point-in-time snapshot vázán na trace, jen pokud uživatel vidí daný trace (oblast 16/19). · **Reuse / canonical pattern:** oblast 24 resolution + oblast 19 trace; read-only viewer. · **Data dotčena:** `RagSetting` (čtení), `RagTrace` config snapshot (oblast 13/19, čtení). · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-29-04
- **EC-29-04-01 — Point-in-time config se liší od aktuálního** · Trigger: config se od běhu odpovědi změnil · Očekávané chování: viewer jasně označí „Snapshot z {timestamp} — od té doby změněno" + možnost porovnat s aktuálním (diff); nezamění historický a živý config. · Mechanismus: snapshot z trace (oblast 19) vs živý effective · Severity: P2 · Test: změň knob po odpovědi → viewer ukáže rozdíl.
- **EC-29-04-02 — Trace bez config snapshotu (legacy)** · Trigger: starý trace bez uloženého configu · Očekávané chování: empty state „Config snapshot není pro tuto odpověď k dispozici", ne crash; nabídni živý config jako přibližný. · Mechanismus: graceful degradace · Severity: P3 · Test: trace bez snapshotu → empty state.
- **EC-29-04-03 — Secret v resolution chain** · Trigger: chain obsahuje secret knob · Očekávané chování: maska na všech vrstvách, žádný plaintext. · Mechanismus: backend masking · Severity: P0 · Test: chain se secret → maska všude.

---

## UC-29-05 — Restart-required knob: warning, že se neprojeví do restartu

- **Actor / role:** Tenant admin s `RagManage` · **Precondition:** editovaný knob má `RestartRequired=true` (z registru) · **Trigger:** uživatel uloží override takového knobu (UC-29-02/03) · **Main flow:**
  1. Před uložením: u knobu trvalý badge „Restart required" + tooltip vysvětlující, že změna se projeví až po restartu hostů (Api/Worker/Jobs).
  2. Po uložení: kromě success toastu se zobrazí **persistentní info banner** „Uloženo — projeví se po příštím restartu služby" a u knobu indikátor „Pending restart" (hodnota uložena, ale `RuntimeApplicable=false`).
  3. Pokud backend poskytuje „last applied vs configured" (oblast 24), viewer ukáže obě hodnoty: configured=nová, active=stará-do-restartu.
- **Postcondition / záruky:** uživatel nikdy nedostane falešný dojem, že runtime knob se projeví okamžitě, když nejde; hodnota je perzistovaná. · **Tenancy / permissions:** `RagManage`. · **Reuse / canonical pattern:** registr metadata `RuntimeApplicable`/`RestartRequired` z oblasti 24. · **Data dotčena:** `RagSetting`. · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-29-05
- **EC-29-05-01 — Runtime knob mylně označen jako restart** · Trigger: registr metadata nekonzistentní · Očekávané chování: FE věří registru (single source of truth), nezpřesňuje sám; pokud chybí flag → konzervativně zobraz „projeví se dle konfigurace serveru" bez tvrzení o okamžitosti. · Mechanismus: registr autorita · Severity: P3 · Test: knob bez flagu → neutrální hláška.
- **EC-29-05-02 — Banner persistence napříč navigací** · Trigger: uživatel odejde a vrátí se, restart neproběhl · Očekávané chování: „Pending restart" indikátor stále viditelný (stav z backendu configured≠active), ne jen ephemeral toast. · Mechanismus: backend configured-vs-active stav · Severity: P3 · Test: reload → pending indikátor přetrvá.

---

## UC-29-06 — Presety (fast / balanced / accurate): výběr a aplikace s diffem

- **Actor / role:** Tenant admin s `RagManage` · **Precondition:** backend (oblast 24) definuje pojmenované presety jako sady hodnot knobů · **Trigger:** uživatel otevře „Presets" v config editoru a vybere preset (fast/balanced/accurate) · **Main flow:**
  1. Fetch `GET /v1/hybridrag/settings/presets` → seznam presetů + jejich hodnoty per knob.
  2. Výběr presetu → **diff view**: které knoby se změní z aktuální efektivní hodnoty na presetovou (zelená = nová, šedá = beze změny), kolik knobů dotčeno, zvýraznění restart-required mezi nimi.
  3. „Apply preset" → potvrzovací modal s rekapitulací diffu (počet změn, případné restart-required) → server action `POST /v1/hybridrag/settings/apply-preset { presetKey, scope }`.
  4. Optimistic-ish (raději po 200 invalidate, protože mění mnoho knobů); success toast „Preset {name} aplikován ({n} parametrů)"; invalidate effective.
  5. Pokud preset obsahuje restart-required knoby → souhrnný restart banner.
- **Postcondition / záruky:** aplikace presetu = batch override na backendu (atomicky, oblast 24); FE jen spustí a zobrazí výsledek; žádné per-knob FE smyčky. · **Tenancy / permissions:** `RagManage`; scope (tenant/collection) dle kontextu. · **Reuse / canonical pattern:** oblast 24 preset endpoint; diff komponenta. · **Data dotčena:** `RagSetting` (batch). · **Eventy:** případně `RagPresetApplied` (oblast 25 audit). · **Priorita:** P2

### Edge cases UC-29-06
- **EC-29-06-01 — Preset částečně selže (atomicita)** · Trigger: jeden knob z presetu neprojde backend validací · Očekávané chování: backend aplikuje atomicky (vše nebo nic) → FE dostane 422 s detailem, nic neaplikováno, žádný částečný stav v UI. · Mechanismus: backend transakce (oblast 24); FE neaplikuje per-knob sám · Severity: P1 · Test: mock 422 → 0 změn, chyba zobrazena.
- **EC-29-06-02 — Diff prázdný (preset == aktuální)** · Trigger: aktuální config už odpovídá presetu · Očekávané chování: diff prázdný, „Apply" disabled s hláškou „Tento preset je již aktivní". · Mechanismus: FE porovnání effective vs preset (jen pro UX) · Severity: P3 · Test: identický stav → Apply disabled.
- **EC-29-06-03 — Preset přepíše ruční overridy** · Trigger: uživatel měl ruční overridy, preset je přepíše · Očekávané chování: diff je explicitně zobrazí (které ruční hodnoty zmizí), potvrzovací modal varuje „Přepíše {n} ručních nastavení". · Mechanismus: diff transparentnost · Severity: P2 · Test: ruční override + preset → modal varování.
- **EC-29-06-04 — Concurrent preset apply + ruční edit** · Trigger: během apply presetu jiný admin edituje knob · Očekávané chování: backend serializace (poslední vyhrává / 409); FE po 200 vždy refetch → ukáže skutečný stav. · Mechanismus: oblast 24 concurrency · Severity: P3 · Test: souběh → refetch konzistentní.

---

## UC-29-07 — System-prompt template editor per kolekce (versioned, rollback)

- **Actor / role:** Tenant admin / prompt engineer s `RagManage` · **Precondition:** kolekce existuje; oblast 13/14 udržuje versionovaný system-prompt template per kolekce · **Trigger:** uživatel otevře „Prompt template" v detailu kolekce (`/hybridrag/collections/{id}/prompt`) · **Main flow:**
  1. Fetch `GET /v1/hybridrag/collections/{id}/prompt-template` → aktuální verze (text + version id + autor + timestamp) a `GET …/prompt-template/versions` → historie verzí.
  2. Editor: textarea/markdown editor s podporou placeholderů (`{context}`, `{question}`, `{citations}` …) + náhled, které placeholdery jsou povinné (validace z backendu). Zobrazí varování, pokud chybí povinný placeholder.
  3. „Save as new version" → `PUT /v1/hybridrag/collections/{id}/prompt-template { text }` → backend vytvoří novou verzi (append-only); optimistic na „current version", invalidate.
  4. Historie verzí: diff mezi verzemi (před/po), „Rollback to this version" → `POST …/prompt-template/rollback { versionId }` (vytvoří novou verzi = kopii staré, ne mutaci historie).
  5. Success toast, invalidate `['rag','collection',id,'prompt']` + versions.
- **Postcondition / záruky:** template versionovaný, append-only; rollback = nová verze; změna se projeví na příští odpovědi (prompt cache oblast 14 se invaliduje backendem, ne FE). · **Tenancy / permissions:** `RagManage`; kolekce v tenantu (izolace oblast 16); editace promptu je citlivá → vázáno na `RagManage`. · **Reuse / canonical pattern:** oblast 13 (answer/prompt) + 14 (cache); versioning pattern jako Identity refresh history; diff komponenta. · **Data dotčena:** prompt template verze (oblast 13/14 úložiště). · **Eventy:** případně `RagPromptTemplateChanged` (oblast 25, invaliduje cache oblast 14). · **Priorita:** P2

### Edge cases UC-29-07
- **EC-29-07-01 — Chybějící povinný placeholder** · Trigger: uživatel smaže `{context}` z templatu · Očekávané chování: backend validace 422 (povinné placeholdery); FE měkká validace předem varuje, Save disabled / s potvrzením; nikdy neuloží nevalidní prompt, který by rozbil retrieval. · Mechanismus: backend validace promptu (oblast 13) = autorita · Severity: P1 · Test: prompt bez `{context}` → 422 / FE block.
- **EC-29-07-02 — XSS / injection v template náhledu** · Trigger: prompt obsahuje HTML/script-like obsah · Očekávané chování: náhled renderuje jako **plain text / sanitizovaný markdown**, nikdy nevykoná HTML; placeholder substituce jen pro vizuální náhled, ne eval. · Mechanismus: sanitizace renderu (UI edge taxonomie XSS) · Severity: P1 · Test: `<script>` v promptu → escapováno, nespustí se.
- **EC-29-07-03 — Rollback na neexistující/cizí verzi** · Trigger: podstrčené versionId · Očekávané chování: backend 404 (verze mimo kolekci/tenant); FE not-found, žádná změna. · Mechanismus: izolace oblast 16 · Severity: P1 · Test: cizí versionId → 404.
- **EC-29-07-04 — Concurrent edit promptu** · Trigger: dva editoři ukládají · Očekávané chování: append-only → obě verze vzniknou, „current" = poslední; UI po refetch ukáže nejnovější + historii obou; žádná ztráta (ne přepsání). · Mechanismus: append-only verzování · Severity: P2 · Test: dva save → 2 verze v historii.
- **EC-29-07-05 — Velmi dlouhý prompt (limit)** · Trigger: prompt přesáhne max délku (token/char limit z configu) · Očekávané chování: backend 422 limit; FE ukáže počítadlo znaků/tokenů + varování blízko limitu, Save block nad limit. · Mechanismus: backend limit + FE counter · Severity: P2 · Test: nadlimitní prompt → block.

---

## UC-29-08 — A/B config experiment UI (oblast 24-13 / 31)

- **Actor / role:** RAG operátor / experimentátor s `RagManage` · **Precondition:** backend (oblast 24-13) podporuje A/B config experimenty (varianty config-setů + traffic split + metriky) · **Trigger:** uživatel otevře „Experiments" sekci config panelu · **Main flow:**
  1. Fetch `GET /v1/hybridrag/experiments` → seznam experimentů (název, status draft/running/stopped, varianty A/B/…, traffic split %, primární metrika, start/stop čas).
  2. „New experiment": formulář — vyber config knoby k variaci, definuj variantu A (baseline = aktuální) a B (override sada), traffic split (%), cílová metrika (z oblasti 18 eval — např. groundedness, citation precision, latency), sampling.
  3. Submit → `POST /v1/hybridrag/experiments` (draft); „Start" → `POST …/{id}/start` (validace splitu=100 % na backendu).
  4. Běžící experiment: dashboard s **live metrikami per varianta** (z oblasti 18/19), statistická signifikance (badge „nedostatek dat" dokud n malé), tlačítko „Promote winner" → aplikuje vítěznou variantu jako override (UC-29-02 batch) + „Stop".
  5. SSE/poll aktualizuje metriky; invalidate po start/stop/promote.
- **Postcondition / záruky:** experiment řízen backendem (traffic routing, sampling, metriky); FE jen konfiguruje a vizualizuje; promote = standardní override apply. · **Tenancy / permissions:** `RagManage`; experimenty tenant-scoped. · **Reuse / canonical pattern:** oblast 24-13 experiment engine + oblast 18 metriky + oblast 31 routing; UC-29-06 apply pattern pro promote. · **Data dotčena:** experiment definice (oblast 24), `RagSetting` (při promote). · **Eventy:** `RagExperimentStarted/Stopped/Promoted` (oblast 25). · **Priorita:** P3

### Edge cases UC-29-08
- **EC-29-08-01 — Traffic split ≠ 100 %** · Trigger: součet variant ≠ 100 · Očekávané chování: backend 422; FE měkká validace předem (live součet, Start disabled dokud ≠100). · Mechanismus: backend autorita; FE UX guard · Severity: P2 · Test: split 60/30 → Start disabled.
- **EC-29-08-02 — Promote bez signifikance** · Trigger: uživatel promote při malém vzorku · Očekávané chování: varovný modal „Nedostatek dat pro spolehlivý závěr — opravdu promote?"; nebrání (rozhodnutí uživatele) ale varuje; backend povolí. · Mechanismus: FE varování nad backend metrikami (oblast 18) · Severity: P3 · Test: malý n → varovný modal.
- **EC-29-08-03 — Dva běžící experimenty na překrývajících knobech** · Trigger: nový experiment varíruje knob už v běžícím experimentu · Očekávané chování: backend odmítne konflikt (409/422); FE zobrazí „Knob {x} je v aktivním experimentu {y}". · Mechanismus: backend konflikt detekce (oblast 24-13) · Severity: P2 · Test: konflikt → chyba s odkazem na experiment.
- **EC-29-08-04 — Live metriky SSE výpadek** · Trigger: SSE stream metrik spadne · Očekávané chování: fallback na polling, „live" badge → „aktualizace opožděna", reconnect přes Last-Event-ID; data neztracena. · Mechanismus: jeden SSE provider + reconnect/replay · Severity: P3 · Test: drop SSE → polling fallback.

---

## UC-29-09 — HITL review console: přehled front (entity-merge, answer-approval, feedback labeling)

- **Actor / role:** Reviewer s `rag.review.label` (a/nebo `rag.review.approve`) · **Precondition:** oblast 32 (HITL) generuje review položky; oblast 11 plní entity-merge frontu (UC-11-05); oblast 18 plní feedback labeling · **Trigger:** uživatel otevře `/hybridrag/review` (nav „RAG → Review") · **Main flow:**
  1. Fetch `GET /v1/hybridrag/review/queues` → souhrn front: typ (entity_merge / answer_approval / feedback_label), počet čekajících, počet přiřazených mně, SLA/stáří nejstarší položky.
  2. Karty/taby per fronta; každá ukazuje backlog count badge a filtr (přiřazené mně / nepřiřazené / vše, dle priority/stáří).
  3. Výběr fronty → seznam položek (paginated/virtualized) s preview (UC-29-10/11/12 dle typu).
  4. Realtime: SSE aktualizuje counts a nové položky bez reloadu (jeden provider, oblast 21/32 stream).
  5. Permission gating: kdo má jen `label` nevidí answer-approval approve akce (disabled/skryté).
- **Postcondition / záruky:** přehled je read-projekce backendových front; akce v detailních UC. · **Tenancy / permissions:** review fronty tenant-scoped; položky jen z tenantu reviewera (izolace oblast 16); `rag.review.*`. · **Reuse / canonical pattern:** oblast 32 fronty + oblast 11 (UC-11-05) + oblast 18; notifications feed-like UI. · **Data dotčena:** review queue (oblast 32), entity review (oblast 11), feedback (oblast 18) — čtení. · **Eventy:** SSE `ReviewItemAdded/Updated` (oblast 32). · **Priorita:** P1

### Edge cases UC-29-09
- **EC-29-09-01 — Prázdné fronty** · Trigger: žádné čekající review položky · Očekávané chování: empty state per fronta „Vše vyřízeno 🎉" (bez emoji v EN/CS textu dle policy — neutrální „Žádné položky k revizi"), ne prázdná stránka. · Mechanismus: empty state · Severity: P3 · Test: prázdná fronta → empty state.
- **EC-29-09-02 — Reviewer bez žádného review práva** · Trigger: uživatel s `RagManage` ale bez `rag.review.*` · Očekávané chování: nav „Review" skrytá; přímý vstup → 403/redirect. · Mechanismus: permission gating (skrýt) · Severity: P2 · Test: bez review práva → nav skrytá.
- **EC-29-09-03 — Realtime nový item během prohlížení** · Trigger: přijde nová položka přes SSE · Očekávané chování: count badge se zvýší + nenásilná „nové (1)" pilulka, ne auto-skok/scroll který by ztratil rozdělanou práci. · Mechanismus: SSE provider + ne-rušivý update · Severity: P3 · Test: push item → badge++ bez reloadu.
- **EC-29-09-04 — SSE reconnect / replay** · Trigger: výpadek spojení · Očekávané chování: reconnect přes Last-Event-ID, dohrání zmeškaných count změn; pokud replay nedostupný → plný refetch front. · Mechanismus: jeden SSE provider + Last-Event-ID (best-effort) · Severity: P3 · Test: drop → reconnect, counts konzistentní.

---

## UC-29-10 — Entity-merge review: rozhodnutí merge/split s rychlým UI

- **Actor / role:** Reviewer s `rag.review.label` / `rag.review.approve` · **Precondition:** oblast 11 entity resolution označila kandidáty na merge do review fronty (UC-11-05) · **Trigger:** uživatel otevře položku z entity-merge fronty · **Main flow:**
  1. Fetch `GET /v1/hybridrag/review/entity/{id}` → dva (či více) kandidátní `GraphNode` + jejich `EntityAlias`, similarity skóre, ukázky kontextu (chunky/dokumenty, kde se vyskytují), navrhovaná akce (merge/keep-separate).
  2. Side-by-side panel: entita A vs B, společné aliasy, rozdílné atributy, evidence.
  3. **Rychlé akce s klávesovými zkratkami:** `M` = merge (zvol survivor), `K` = keep separate, `S` = split (rozděl chybně sloučené), `N` = note, `→/←` = další/předchozí položka, `U` = undo poslední.
  4. Merge → modal vyber survivor node + potvrď → `POST /v1/hybridrag/review/entity/{id}/merge { survivorId, mergedIds, note }`; backend (oblast 11) provede merge grafu + přepíše aliasy + audituje.
  5. Optimistic odebrání položky z fronty + „Undo" toast (5 s); po 200 invalidate; auto-advance na další položku.
- **Postcondition / záruky:** rozhodnutí perzistováno backendem (graf merge je idempotentní/auditovaný oblast 11/25); fronta se aktualizuje; reviewer identita z tokenu. · **Tenancy / permissions:** `rag.review.label` pro návrh, `rag.review.approve` pro finální merge (dle routing policy UC-29-13); entity v tenantu (izolace 16). · **Reuse / canonical pattern:** oblast 11 entity-res + review queue (UC-11-05); keyboard-driven labeling UI. · **Data dotčena:** `GraphNode`, `GraphEdge`, `EntityAlias` (merge), review item stav (oblast 32). · **Eventy:** `EntityMerged` (oblast 11/25). · **Priorita:** P1

### Edge cases UC-29-10
- **EC-29-10-01 — Položka už vyřešena jiným reviewerem** · Trigger: souběžně někdo merge provedl · Očekávané chování: backend 409/„already resolved" → položka zmizí s hláškou „Již zpracováno uživatelem X", auto-advance; žádný dvojitý merge. · Mechanismus: oblast 32 claim/stav + concurrency · Severity: P1 · Test: souběh → druhý dostane already-resolved.
- **EC-29-10-02 — Undo po merge** · Trigger: reviewer klikne Undo do 5 s · Očekávané chování: `POST …/entity/{id}/undo` nebo split → merge se vrátí (oblast 11 podporuje split); pokud okno vypršelo, Undo zmizí a je nutný explicitní split. · Mechanismus: oblast 11 split jako inverze · Severity: P2 · Test: undo v okně → entity rozděleny zpět.
- **EC-29-10-03 — Keyboard shortcut konflikt / fokus v textovém poli** · Trigger: reviewer píše note a stiskne `M` · Očekávané chování: shortcuts neaktivní, když je fokus v inputu/textarea; jen mimo editaci. · Mechanismus: focus-aware keybinding · Severity: P2 · Test: psaní v note → `M` se vloží jako text, nemerguje.
- **EC-29-10-04 — Merge survivor nevybrán** · Trigger: potvrzení bez výběru survivora · Očekávané chování: validace blokuje, zvýrazní výběr; backend by stejně 422. · Mechanismus: FE guard + backend autorita · Severity: P3 · Test: bez survivora → block.
- **EC-29-10-05 — XSS v evidence/alias textu** · Trigger: alias/chunk obsahuje HTML · Očekávané chování: sanitizovaný render, žádné spuštění. · Mechanismus: sanitizace · Severity: P1 · Test: `<img onerror>` v aliasu → escapováno.

---

## UC-29-11 — Answer-approval review: schválit/zamítnout/editovat odpověď s feedbackem

- **Actor / role:** Reviewer s `rag.review.approve` · **Precondition:** routing policy (UC-29-13) označila odpověď k human approval (blocking gate nebo async); oblast 13 vygenerovala draft odpovědi + citace · **Trigger:** uživatel otevře položku z answer-approval fronty · **Main flow:**
  1. Fetch `GET /v1/hybridrag/review/answer/{id}` → otázka, draft odpověď, citace (`AnswerCitation` → odkazy na chunky/dokumenty), retrieval trace (oblast 13/19), groundedness skóre (oblast 18), důvod flagnutí (low confidence / policy / sampling).
  2. Reviewer vidí odpověď + **inline citace** (klik na citaci → zdrojový chunk/dokument v side panelu pro ověření faktů).
  3. Akce: **Approve** (`A`), **Deny with feedback** (`D` → povinný důvod/štítek), **Edit-to-correct** (`E` → upraví text odpovědi a/nebo citace, uloží opravenou verzi).
  4. Edit-to-correct → `POST /v1/hybridrag/review/answer/{id}/correct { correctedText, citations, note }`; Approve → `…/approve`; Deny → `…/deny { reason, labels }`.
  5. Pokud je to **blocking gate** (UC-29-14), approve/deny/correct uvolní čekající odpověď uživateli (oblast 32 → realtime push žadateli, oblast 21); jinak async (jen pro tréning/golden).
  6. Optimistic odebrání z fronty, undo toast, auto-advance, invalidate.
- **Postcondition / záruky:** rozhodnutí + opravená odpověď perzistovány; blocking gate uvolněn; vše auditováno (oblast 25); identita reviewera z tokenu. · **Tenancy / permissions:** `rag.review.approve` (approve/deny/correct); `rag.review.label` jen pokud policy dovolí navrhovat; odpověď v tenantu (16). · **Reuse / canonical pattern:** oblast 13 answer+citace + oblast 32 gate + oblast 21 realtime; XSS-safe render citací. · **Data dotčena:** `RagConversation/Turn/AnswerCitation` (oprava/anotace), review item (oblast 32). · **Eventy:** `AnswerApproved/Denied/Corrected` (oblast 25), realtime release žadateli. · **Priorita:** P1

### Edge cases UC-29-11
- **EC-29-11-01 — Blocking gate timeout (žadatel čeká)** · Trigger: reviewer nereaguje do SLA · Očekávané chování: backend (oblast 32) po timeoutu auto-rozhodne dle policy (abstain/serve-with-disclaimer); FE položku označí „Vypršelo — auto {akce}", nelze už approve. · Mechanismus: oblast 32 timeout policy + oblast 17 degradace · Severity: P1 · Test: timeout → položka uzavřena auto.
- **EC-29-11-02 — Deny bez důvodu** · Trigger: deny bez vyplněného feedbacku · Očekávané chování: validace vyžaduje důvod/štítek (povinný pro tréning); backend 422 jinak. · Mechanismus: FE guard + backend autorita · Severity: P2 · Test: prázdný deny → block.
- **EC-29-11-03 — XSS v odpovědi/citaci** · Trigger: LLM/zdroj obsahuje HTML/markdown injection · Očekávané chování: sanitizovaný markdown render, citace jako bezpečné odkazy; žádné spuštění skriptu. · Mechanismus: sanitizace (XSS taxonomie) · Severity: P0 · Test: `<script>` v odpovědi → escapováno.
- **EC-29-11-04 — Edit-to-correct concurrent s jiným reviewerem** · Trigger: dva korigují stejnou odpověď · Očekávané chování: backend concurrency (item claim/xmin) → druhý dostane „již upraveno", refetch ukáže aktuální. · Mechanismus: oblast 32 claim + concurrency · Severity: P2 · Test: souběh → druhý refetch.
- **EC-29-11-05 — Citace odkazuje na smazaný/erased dokument** · Trigger: zdrojový dokument byl GDPR-erased (oblast 20) mezi generací a review · Očekávané chování: citace ukáže „Zdroj nedostupný (odstraněn)", ne crash; reviewer to zohlední (pravděpodobně deny). · Mechanismus: oblast 20 erasure + graceful citace · Severity: P2 · Test: erased zdroj → placeholder.
- **EC-29-11-06 — Reviewer = autor dotazu (konflikt zájmů)** · Trigger: reviewer reviewuje vlastní odpověď · Očekávané chování: dle policy buď povoleno (malý tým) nebo backend skryje vlastní položky; FE respektuje co backend vrátí (nezavádí vlastní pravidlo). · Mechanismus: backend routing policy (oblast 32) · Severity: P3 · Test: vlastní položka dle policy (ne)viditelná.

---

## UC-29-12 — Feedback labeling: rychlé štítkování s klávesovými zkratkami a poznámkami

- **Actor / role:** Reviewer / annotator s `rag.review.label` · **Precondition:** oblast 18 (eval) sbírá online feedback (thumbs up/down, flagnuté odpovědi) a plní labeling frontu · **Trigger:** uživatel otevře feedback-label frontu · **Main flow:**
  1. Fetch `GET /v1/hybridrag/review/feedback/{id}` (a batch další) → odpověď + uživatelský feedback (👍/👎, volný text) + retrieval kontext.
  2. **Rychlé labeling UI:** sada štítků (correct / hallucination / incomplete / wrong-citation / off-topic / policy-violation …) s číselnými/písmennými shortcuty (`1-9`), `N` = note, `→` = další (auto-save předchozího), bulk mód (vyber víc → hromadný štítek).
  3. Label → `POST /v1/hybridrag/review/feedback/{id}/label { labels, note }`; optimistic posun na další, auto-advance, undo.
  4. Progress indikátor „X / Y zbývá", rychlost (items/min) pro produktivitu.
- **Postcondition / záruky:** labely perzistovány → feedují oblast 18 (online eval, tréning, golden kandidáti); identita z tokenu; auditováno. · **Tenancy / permissions:** `rag.review.label`; položky tenant-scoped (16). · **Reuse / canonical pattern:** oblast 18 feedback + keyboard-driven labeling; bulk pattern. · **Data dotčena:** feedback labely (oblast 18), review item (oblast 32). · **Eventy:** `FeedbackLabeled` (oblast 18/25). · **Priorita:** P2

### Edge cases UC-29-12
- **EC-29-12-01 — Auto-advance ztratí neuložený label** · Trigger: rychlý `→` před dokončením save · Očekávané chování: save je sekvenčně garantován (await/queued) než advance; při chybě save se vrátí na položku s chybou, neztratí label. · Mechanismus: sekvenční save + optimistic s rollback · Severity: P2 · Test: rychlé prokliky → všechny labely uloženy.
- **EC-29-12-02 — Bulk label nekonzistentní položky** · Trigger: bulk štítek na různorodé položky · Očekávané chování: potvrzení „Označit {n} položek štítkem {x}?"; backend aplikuje, FE refetch; částečné selhání → report které selhaly. · Mechanismus: backend batch + per-item výsledek · Severity: P3 · Test: bulk → potvrzení + výsledek.
- **EC-29-12-03 — Shortcut ve fokusu poznámky** · Trigger: psaní note, stisk `3` · Očekávané chování: shortcuts vypnuté v inputu (jako EC-29-10-03). · Mechanismus: focus-aware keybinding · Severity: P2 · Test: `3` v note → text, ne label.
- **EC-29-12-04 — One-click „add to golden set"** · Trigger: reviewer označí odpověď jako vzorovou · Očekávané chování: viz UC-29-15 — tlačítko „Add to golden", které vezme otázku+(opravenou)odpověď+citace a pošle do oblasti 18 golden set. · Mechanismus: UC-29-15 · Severity: P2 · Test: klik → položka v golden setu.

---

## UC-29-13 — Routing-policy editor (deklarativní thresholdy auto-serve / flag / abstain, bez deploye)

- **Actor / role:** Tenant admin / RAG operátor s `RagManage` (a `rag.review.approve` pro citlivé policy) · **Precondition:** oblast 31/32 podporuje deklarativní routing policy per use-case (auto-serve / flag-for-review / abstain) řízenou daty · **Trigger:** uživatel otevře „Routing policy" v config panelu · **Main flow:**
  1. Fetch `GET /v1/hybridrag/routing-policy` → seznam pravidel per use-case/kolekci: podmínka (např. groundedness < 0.6 → flag; < 0.3 → abstain; jinak auto-serve), sampling % (kolik auto-serve jde i tak na async review), přiřazená reviewer role, blocking vs async.
  2. Deklarativní editor: pro každý use-case nastav thresholdy (slider groundedness/confidence), akce (auto-serve / flag / abstain), sampling %, reviewer role (z Identity rolí), gate typ (blocking/async).
  3. Náhled „simulace": na historických odpovědích (oblast 18/19) ukáže, kolik by spadlo do serve/flag/abstain při daných thresholdech (read-only co-by-kdyby).
  4. Save → `PUT /v1/hybridrag/routing-policy { rules }` (deklarativní, žádný deploy/kód); validace (thresholdy 0-1, pokrytí všech pásem) na backendu; optimistic + invalidate.
- **Postcondition / záruky:** policy aplikována za běhu (data-driven, oblast 31), bez deploye; ovlivní budoucí routing odpovědí; auditováno. · **Tenancy / permissions:** `RagManage` (+ `rag.review.approve` pro blocking gate policy); tenant-scoped. · **Reuse / canonical pattern:** oblast 31 routing + oblast 32 gate + oblast 18 simulace; deklarativní config jako oblast 24. · **Data dotčena:** routing policy (oblast 31), `RagSetting`-like úložiště. · **Eventy:** `RoutingPolicyChanged` (oblast 25/31). · **Priorita:** P2

### Edge cases UC-29-13
- **EC-29-13-01 — Mezera/překryv v thresholdech** · Trigger: pásma nepokryjí celý rozsah nebo se překrývají · Očekávané chování: backend 422 (musí pokrýt 0-1 bez děr); FE měkká validace vizualizuje pásma (barevný pruh), zvýrazní mezeru/překryv, Save block. · Mechanismus: backend autorita + FE vizuální guard · Severity: P1 · Test: mezera → Save block + zvýraznění.
- **EC-29-13-02 — Sampling 0 % u abstain s tréninkem** · Trigger: abstain bez sampling → žádná data pro zlepšení · Očekávané chování: nebrání, ale upozorní „0 % sampling = žádná tréning data z tohoto pásma". · Mechanismus: FE advisory · Severity: P3 · Test: sampling 0 → advisory.
- **EC-29-13-03 — Reviewer role neexistuje** · Trigger: přiřazená role smazána v Identity · Očekávané chování: backend validace 422 / FE selektor jen existující role (fetch z Identity); pokud role zmizí po uložení → policy fallback + varování v UI. · Mechanismus: Identity role list + backend validace · Severity: P2 · Test: neexistující role → není v selektoru / 422.
- **EC-29-13-04 — Simulace na velkém datasetu (výkon)** · Trigger: simulace nad statisíci odpovědí · Očekávané chování: backend počítá (async/sampled), FE ukáže „počítám…" + výsledek; ne blokuje UI; cap na velikost vzorku. · Mechanismus: backend sampled simulace (oblast 18) · Severity: P3 · Test: velký dataset → async výsledek.
- **EC-29-13-05 — Blocking gate bez kapacity reviewerů** · Trigger: policy nastaví blocking gate, ale málo reviewerů → fronta roste · Očekávané chování: FE varuje „Blocking gate může zdržet odpovědi; zvaž SLA/kapacitu"; backend má timeout fallback (EC-29-11-01). · Mechanismus: FE advisory + oblast 32 timeout · Severity: P2 · Test: blocking gate → advisory zobrazeno.

---

## UC-29-14 — Blocking approval gate UI (čekající akce: schválit / zamítnout / editovat)

- **Actor / role:** Reviewer s `rag.review.approve` · **Precondition:** routing policy (UC-29-13) nastavila blocking gate pro use-case; odpověď čeká na lidské rozhodnutí, žadatel je blokován (oblast 32) · **Trigger:** nová blocking položka přijde do fronty (SSE push) / reviewer otevře „Pending approvals" · **Main flow:**
  1. Dedikovaný „Pending approvals" pohled zvýrazněný (vyšší priorita než async): ukazuje **čekající akce s odpočtem SLA** (kolik času do auto-timeout), žadatele (anonymizovaně/dle policy), otázku + draft odpověď.
  2. Realtime: SSE okamžitě přidá novou blocking položku (oblast 21/32), zvuk/badge upozornění; odpočet SLA živě tiká.
  3. Reviewer: Approve / Deny-with-feedback / Edit-to-correct (jako UC-29-11), ale s vědomím, že **žadatel čeká** — po rozhodnutí backend okamžitě uvolní odpověď žadateli (realtime release, oblast 21).
  4. Optimistic odebrání + potvrzení „Odpověď uvolněna žadateli"; invalidate pending fronty.
- **Postcondition / záruky:** blocking gate uzavřen rozhodnutím nebo timeoutem; žadatel dostane výsledek (schválenou/opravenou odpověď nebo abstain); auditováno. · **Tenancy / permissions:** `rag.review.approve`; položky tenant-scoped; žadatel-identita maskovaná dle policy (GDPR oblast 20). · **Reuse / canonical pattern:** UC-29-11 akce + oblast 32 gate + oblast 21 realtime release; SLA countdown UI. · **Data dotčena:** review item / gate stav (oblast 32), `RagConversation/Turn` (uvolnění/oprava). · **Eventy:** `ApprovalGranted/Denied`, realtime `AnswerReleased` žadateli. · **Priorita:** P1

### Edge cases UC-29-14
- **EC-29-14-01 — SLA vyprší během prohlížení** · Trigger: odpočet dojde na 0, zatímco reviewer čte · Očekávané chování: položka se uzamkne („Vypršelo — auto-{akce}"), reviewer akce zablokovány, refetch; žádné rozhodnutí po timeoutu. · Mechanismus: oblast 32 timeout + FE live countdown sync se serverem · Severity: P1 · Test: nech vypršet → akce disabled.
- **EC-29-14-02 — Clock drift (FE odpočet ≠ server)** · Trigger: FE hodiny se rozejdou se serverem · Očekávané chování: odpočet je orientační; rozhodnutí validuje server (pozdní approve → 409 „již vypršelo"); FE nikdy nerozhoduje o timeoutu sám. · Mechanismus: server = autorita času (UTC), FE jen vizualizace · Severity: P2 · Test: pozdní approve → 409.
- **EC-29-14-03 — Žadatel mezitím zrušil request** · Trigger: žadatel zavřel session / cancel · Očekávané chování: backend označí položku „žadatel odešel", FE ji zšedne „Není třeba — žadatel zrušil", reviewer může přeskočit. · Mechanismus: oblast 32 cancel signál · Severity: P3 · Test: cancel → položka označena.
- **EC-29-14-04 — Realtime release nedoručen žadateli** · Trigger: žadatelův SSE spadl při uvolnění · Očekávané chování: oblast 21 replay (Last-Event-ID) doručí po reconnectu; reviewer UI to neřeší (release je perzistovaný fakt, ne jen push). · Mechanismus: oblast 21 replay + perzistence rozhodnutí · Severity: P2 · Test: drop žadatel SSE → po reconnectu odpověď dorazí.

---

## UC-29-15 — One-click "add corrected to golden set"

- **Actor / role:** Reviewer s `rag.review.label` / `rag.review.approve` · **Precondition:** existuje (opravená) odpověď z review (UC-29-11/12) nebo vybraná konverzace; oblast 18 spravuje golden set · **Trigger:** reviewer klikne „Add to golden set" na položce / odpovědi · **Main flow:**
  1. Tlačítko (i klávesová zkratka `G`) na answer-approval, feedback-label nebo přímo v konverzaci → otevře lehký modal: předvyplní otázku, (opravenou) odpověď, citace; reviewer doplní očekávané body / štítky / collection scope.
  2. Submit → `POST /v1/hybridrag/eval/golden { question, expectedAnswer, citations, collectionId, tags, sourceReviewItemId }` (oblast 18); identita autora z tokenu.
  3. Optimistic „Přidáno do golden setu" toast + odkaz na golden položku; invalidate golden set query (UC oblast 18 UI, pokud zobrazena).
  4. Dedup: pokud otázka už v golden setu → backend vrátí konflikt / nabídne update existující.
- **Postcondition / záruky:** golden položka vytvořena, použitelná pro offline eval (oblast 18); vazba na zdrojový review item pro audit; idempotence dle `sourceReviewItemId`. · **Tenancy / permissions:** `rag.review.*`; golden set tenant/collection-scoped (16). · **Reuse / canonical pattern:** oblast 18 golden set; UC-29-11/12 zdroj dat; idempotency UNIQUE klíč (zákon 5). · **Data dotčena:** golden set (oblast 18). · **Eventy:** `GoldenItemAdded` (oblast 18/25). · **Priorita:** P2

### Edge cases UC-29-15
- **EC-29-15-01 — Duplicitní otázka v golden setu** · Trigger: stejná/podobná otázka už existuje · Očekávané chování: backend detekuje (UNIQUE/similarity) → 409 nebo „už existuje, aktualizovat?"; FE nabídne update místo duplikace. · Mechanismus: idempotency klíč + backend dedup (oblast 18) · Severity: P2 · Test: duplikát → nabídka update.
- **EC-29-15-02 — Add to golden z neopravené halucinace** · Trigger: reviewer omylem přidá špatnou odpověď · Očekávané chování: modal vyžaduje potvrzení „Toto bude očekávaná správná odpověď"; možnost edit před uložením; golden položku lze později smazat (oblast 18). · Mechanismus: potvrzení + editovatelnost · Severity: P3 · Test: přidání → modal s editem.
- **EC-29-15-03 — Citace na erased/smazaný zdroj** · Trigger: golden odpověď cituje GDPR-erased dokument · Očekávané chování: varování „Citace odkazuje na nedostupný zdroj"; golden lze uložit bez té citace nebo s poznámkou. · Mechanismus: oblast 20 erasure detekce · Severity: P3 · Test: erased citace → varování.
- **EC-29-15-04 — Double-submit (dvojklik G)** · Trigger: rychlé dvojí přidání · Očekávané chování: idempotency `sourceReviewItemId` → jedna golden položka; druhý request no-op/konflikt. · Mechanismus: idempotency UNIQUE (zákon 5) · Severity: P2 · Test: dvojklik → 1 golden položka.

---

## UC-29-16 — Reviewer assignment a load balancing

- **Actor / role:** Review koordinátor / tenant admin s `rag.review.approve` (+ admin nad frontou) · **Precondition:** existuje víc reviewerů (Identity role) a fronta položek (oblast 32) · **Trigger:** koordinátor otevře „Assignment" v review konzoli · **Main flow:**
  1. Fetch `GET /v1/hybridrag/review/assignments` → položky a jejich přiřazení (nepřiřazené / přiřazené konkrétnímu reviewerovi), zatížení per reviewer (počet otevřených, průměrná doba).
  2. Akce: „Assign to me" (claim), „Assign to {reviewer}", „Unassign", „Auto-distribute" (round-robin / dle zátěže — backend).
  3. Claim → `POST /v1/hybridrag/review/{itemId}/claim` (atomický na backendu, oblast 32 — zabraňuje dvojímu zpracování); optimistic, invalidate.
  4. Routing policy (UC-29-13) může přiřazovat dle reviewer role automaticky; toto UI je manuální override/koordinace.
- **Postcondition / záruky:** přiřazení perzistováno; claim je atomický (žádné dva reviewery na jedné položce); identita z tokenu. · **Tenancy / permissions:** koordinační akce vyžadují vyšší právo (`rag.review.approve` / admin); reviewers tenant-scoped. · **Reuse / canonical pattern:** oblast 32 claim/assign; atomický claim (zákon concurrency). · **Data dotčena:** review item assignment (oblast 32). · **Eventy:** `ReviewItemAssigned/Claimed` (oblast 32). · **Priorita:** P3

### Edge cases UC-29-16
- **EC-29-16-01 — Dvojitý claim (race)** · Trigger: dva reviewery claimnou současně · Očekávané chování: backend atomický claim → druhý dostane 409 „již přiřazeno", refetch; žádné dvojí zpracování. · Mechanismus: oblast 32 atomický claim (ExecuteUpdate guard / concurrency) · Severity: P1 · Test: souběžný claim → jeden vyhraje.
- **EC-29-16-02 — Assign reviewerovi bez práva** · Trigger: přiřazení uživateli bez `rag.review.*` · Očekávané chování: backend 422 (reviewer musí mít právo); FE selektor jen oprávněné uživatele. · Mechanismus: Identity permission check · Severity: P2 · Test: neoprávněný → není v selektoru / 422.
- **EC-29-16-03 — Unassign rozdělané položky** · Trigger: unassign položky, kterou někdo rozpracoval · Očekávané chování: varování „Reviewer X na ní pracuje"; po unassign se vrátí do nepřiřazených, jeho rozdělaná akce se zahodí čistě (ne částečný stav). · Mechanismus: oblast 32 stav + concurrency · Severity: P3 · Test: unassign rozdělané → návrat do poolu.

---

## UC-29-17 — Permission gating a secret masking napříč panelem (cross-cutting)

- **Actor / role:** Libovolný uživatel s přístupem do `hybridrag` · **Precondition:** token nese podmnožinu RAG permissionů · **Trigger:** render kteréhokoli config/review prvku · **Main flow:**
  1. BFF server-side přečte permissiony z tokenu (nikdy z klienta); předá do RSC jako bezpečný kontext.
  2. Prvky bez read práva: **skryté** (nezobrazí se vůbec — žádná indikace existence). Prvky s read ale bez write: **disabled** s tooltipem „Vyžaduje oprávnění {X}".
  3. Secret hodnoty (API klíče Cohere/Claude/embedding v config registru) vždy **maskované**; backend nikdy nevrací plaintext; editace = write-only.
  4. Approve akce (`rag.review.approve`) skryté pro `label`-only reviewery; admin/koordinační akce skryté pro běžné reviewery.
- **Postcondition / záruky:** žádný únik existence funkcí/secretů nad rámec práv; izolace a autorizace na backendu, FE jen reflektuje. · **Tenancy / permissions:** celá matice RAG permissionů (`RagManage`, `rag.review.label`, `rag.review.approve`, admin). · **Reuse / canonical pattern:** permission-gating (skrýt vs disable) z modularplatform-frontend; BFF Model A; secret masking. · **Data dotčena:** žádná (gating vrstva). · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-29-17
- **EC-29-17-01 — Klientská manipulace permissionů** · Trigger: uživatel zmanipuluje klientský stav, aby odkryl skryté akce · Očekávané chování: backend stejně odmítne (403); FE gating je jen UX, autorizace je server-side. · Mechanismus: backend autorizace (zákon: identita/práva z tokenu) · Severity: P0 · Test: zfalšovaný FE stav → backend 403.
- **EC-29-17-02 — Secret prosákne do logu/telemetrie** · Trigger: omylem zalogovaná hodnota · Očekávané chování: secret se nikdy neposílá na FE plaintextem → nelze zalogovat na klientovi; FE telemetrie (oblast 19) secret pole nesbírá. · Mechanismus: backend masking + FE telemetrie allowlist · Severity: P0 · Test: client logy/telemetrie bez secretu.
- **EC-29-17-03 — Permission odebráno uprostřed session** · Trigger: adminovi odebráno `RagManage` během práce · Očekávané chování: další write → backend 403 → centrální handler „Oprávnění odvoláno", refresh tokenu/redirect; rozdělaná write akce neprojde. · Mechanismus: backend autorita + 403 handling · Severity: P2 · Test: odebrání práva → write 403.

---

## UC-29-18 — Cross-cutting UI kvalita: i18n, a11y, responsive, dark mode, hydration

- **Actor / role:** Libovolný uživatel config/review panelu · **Precondition:** panel renderován · **Trigger:** běžné použití napříč zařízeními/lokalizací/tématem · **Main flow:**
  1. **i18n en+cs** (next-intl, NESTED namespace klíče): všechny UI labely, tooltips, chybové hlášky (chybové texty z RFC9457 errorCode → SharedResource backend; UI chrome z FE bundle); žádné ploché tečkové klíče (jinak raw klíč + dev overlay blokuje kliky — past z MEMORY).
  2. **a11y:** keyboard nav (review shortcuts + tab order), focus management (modaly = focus trap, návrat fokusu po zavření), screen reader (ARIA labely na knobech, frontách, akcích), dostatečný kontrast (zvl. diff barvy, badge scope/restart).
  3. **Responsive:** config tabulky a review konzole použitelné na tabletu; na mobilu degradace na karty (side-by-side entity-merge → stacked); review shortcuts mají i tlačítkové ekvivalenty (mobil bez klávesnice).
  4. **Dark mode:** všechny prvky (diff zelená/šedá, badge, secret maska) čitelné v dark mode (next-themes, hydration-safe — `scriptProps` fix z MEMORY).
  5. **Hydration-safe:** žádné client-only hodnoty (čas, náhodné) v SSR výstupu bez suppression; SLA countdown se inicializuje až po mountu.
- **Postcondition / záruky:** panel přístupný, lokalizovaný, responzivní, bez hydration warningů; konzistentní s platform FE konvencemi. · **Tenancy / permissions:** N/A (kvalita vrstva). · **Reuse / canonical pattern:** modularplatform-frontend (i18n NESTED, dark mode, a11y); MEMORY pasti (i18n namespace, next-themes scriptProps). · **Data dotčena:** žádná. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-29-18
- **EC-29-18-01 — Chybějící i18n klíč (cs)** · Trigger: nový UI label bez cs překladu · Očekávané chování: fallback na en (next-intl fallback), nikdy raw klíč ani dev overlay blokující kliky; NESTED klíče povinné. · Mechanismus: next-intl fallback + NESTED namespace · Severity: P2 · Test: cs bez klíče → en fallback, ne raw.
- **EC-29-18-02 — Focus trap v review modalu** · Trigger: otevřený merge/approve modal, tab cyklus · Očekávané chování: fokus uvězněn v modalu, Esc zavře a vrátí fokus na trigger; screen reader oznámí modal. · Mechanismus: a11y focus trap · Severity: P2 · Test: keyboard-only projití modalu.
- **EC-29-18-03 — Kontrast diff barev v dark mode** · Trigger: diff zelená/červená na tmavém pozadí · Očekávané chování: WCAG AA kontrast i v dark mode; nelze rozlišovat jen barvou (i ikona/text). · Mechanismus: dark mode tokeny + ne-jen-barva · Severity: P2 · Test: kontrast audit dark mode.
- **EC-29-18-04 — Hydration mismatch u SLA countdownu** · Trigger: server vs klient čas v countdownu · Očekávané chování: countdown se renderuje až po mountu (žádná hodnota v SSR), 0 hydration warningů. · Mechanismus: client-only po mountu (MEMORY scriptProps/hydration past) · Severity: P2 · Test: 0 hydration warningů v konzoli.
- **EC-29-18-05 — Responsive entity-merge na mobilu** · Trigger: side-by-side na úzké obrazovce · Očekávané chování: přepne na stacked/tab view, akce dostupné tlačítky (ne jen shortcuty). · Mechanismus: responsive breakpointy · Severity: P3 · Test: mobile viewport → stacked.
