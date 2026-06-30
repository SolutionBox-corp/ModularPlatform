# Oblast 28 — UI — Eval, cost, audit & observability dashboards

> **Rozsah oblasti.** Frontendové dashboardy platformové Next.js aplikace (`frontend/`), které **pouze konzumují existující backend read-endpointy** modulu HybridRag a NEPOČÍTAJÍ žádné nové metriky/agregace — veškerá business logika, agregace, izolace a redakce PII je na backendu (oblasti 18 eval, 19 observ+cost, 24 config-registry, 25 audit, 30 model-routing/cost, 31 eval-experiment/golden). UI vrstva drží frozen patterny: BFF auth MODEL A (tokeny pouze server-side přes route handlers/server actions, NIKDY v browseru), TanStack Query jako JEDEN data source (fetch jednou, reuse, `invalidateQueries` po mutaci), JEDEN SSE realtime provider, centrální RFC9457 error handling, i18n en+cs (next-intl, NESTED namespace klíče), povinná a11y, responsive, dark mode, nav entitlement casing lowercase.
>
> **Identita a tenancy.** Identitu i tenant řeší BFF server-side z tokenu (`ITenantContext.UserId`, `tenant_id` claim) — UI NIKDY neposílá subjekt z browseru. Všechny dashboardy jsou RLS-scoped na tenant; cizí id → backend vrací 404 (IDOR-safe, oblast 16). Prvky jsou permission-gated dle claimů v session: `PlatformPermissions.RagEvalRead` (`rag.eval.read`), `RagCostRead` (`rag.cost.read`), `RagObservabilityRead` (`rag.observability.read`), `PlatformPermissions.AuditRead` (`audit.read`). Permission-gated prvky se **skrývají** (ne disablují) tam, kde by jejich pouhá existence prozradila citlivou strukturu; akční tlačítka uvnitř povolené stránky se disablují s tooltipem.
>
> **DoS / rozsah.** Každý časový rozsah a stránkování má UI-side cap odpovídající backend range-capu (oblast 19/22) — UI nikdy nepošle neohraničený `from/to` ani neomezený `pageSize`. Agregáty jsou PII-redigované na backendu; UI nesmí re-konstruovat PII z dílčích polí.

---

## UC-28-01 — Otevření EVAL dashboardu (Ragas-style metriky, aktuální snapshot)

- **Actor / role:** Přihlášený uživatel s `RagEvalRead` (typicky tenant admin / quality engineer). · **Precondition:** Tenant má modul `hybridrag` entitled (nav casing lowercase); existuje aspoň jeden dokončený eval běh (oblast 18/31). · **Trigger:** Uživatel klikne na „Eval" v navigaci nebo přejde na `/[locale]/hybridrag/eval`.
- **Main flow:**
  1. Server component / route handler (BFF) ověří session, přiloží Bearer token server-side, zavolá read-endpoint `GET /v1/hybridrag/eval/summary?window=30d` (oblast 18) přes platformový API klient.
  2. Backend vrátí RLS-scoped agregát (context precision/recall, faithfulness, answer relevancy, citation coverage, hallucination rate) jako poslední snapshot + delta vůči předchozímu oknu; RFC9457 při chybě.
  3. UI hydratuje TanStack Query cache klíčem `['rag','eval','summary',{window}]`; metriky vykreslí jako karty (KPI tiles) s hodnotou, jednotkou (0–1 / %), barevným stavem vůči prahu a šipkou trendu.
  4. Skeleton stavy během načítání; empty state pokud žádný běh ("Zatím žádné eval běhy — spusťte golden-set runner", odkaz na UC-28-05).
- **Postcondition / záruky:** Žádná mutace. Data jen čtena, cache sdílená s ostatními kartami stránky. · **Tenancy / permissions:** RLS na tenant; stránka gated `RagEvalRead`, jinak nav položka skrytá + přímý přístup → 403 boundary (redirect na „nemáte oprávnění"). · **Reuse / canonical pattern:** `frontend-feature-slice` skill (single data source); read-endpoint oblast 18; BFF MODEL A. · **Data dotčena:** read-only nad `hybridrag_*` eval projekcemi (přes endpoint). · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-28-01
- **EC-28-01-01 — Žádný eval běh (empty)** · Trigger: tenant nikdy nespustil eval. · Očekávané chování: empty state s CTA, žádné prázdné/0,00 karty působící jako reálná data. · Mechanismus: UI empty-state taxonomie. · Severity: P2 · Test: e2e — nový tenant → eval page → vidí empty CTA, ne nuly.
- **EC-28-01-02 — Backend 403 (chybí permission, přímý URL)** · Trigger: uživatel bez `RagEvalRead` zadá `/hybridrag/eval` ručně. · Očekávané chování: server-side guard → redirect/ NotAuthorized boundary, nikdy se nevyrenderují data. · Mechanismus: BFF permission check + Law 10 (identita z tokenu) + oblast 16. · Severity: P0 · Test: e2e bez claimu → 403 boundary, network neukáže payload.
- **EC-28-01-03 — Stale cache po novém běhu** · Trigger: mezitím doběhne nový eval běh (SSE event). · Očekávané chování: realtime provider invaliduje `['rag','eval',...]`, karty se přepočítají bez reloadu. · Mechanismus: jeden SSE provider + `invalidateQueries` (oblast 21). · Severity: P2 · Test: e2e — emit eval-completed event → karty se aktualizují.
- **EC-28-01-04 — Hydration mismatch u trend šipek** · Trigger: barevný/locale-formátovaný delta render na serveru vs klientu. · Očekávané chování: 0 hydration warningů; formátování čísel přes deterministický `Intl` s pevným locale ze session. · Mechanismus: hydration-safe formatting (frozen past z MEMORY). · Severity: P2 · Test: konzole 0 warningů na eval page (en i cs).

---

## UC-28-02 — Trend metrik v čase (časová řada per metrika)

- **Actor / role:** `RagEvalRead`. · **Precondition:** Existuje ≥2 eval snapshoty. · **Trigger:** Uživatel přepne na záložku „Trend" nebo zvolí metriku z karty (drill).
- **Main flow:**
  1. UI volá `GET /v1/hybridrag/eval/trend?metric=faithfulness&window=90d&bucket=day` (oblast 18) — backend vrací bucketovanou časovou řadu (RLS-scoped, range-capped na max okno).
  2. TanStack Query klíč `['rag','eval','trend',{metric,window,bucket}]`; vykreslení line chartu (owned shadcn/Recharts-style komponenta) s a11y popisem (aria-label, tabulková fallback reprezentace pro screen reader).
  3. Přepínač okna (7d/30d/90d) a bucketu (hour/day/week) re-fetchuje; UI cappuje volby tak, aby (window/bucket) nepřekročilo backend max počet bucketů.
- **Postcondition / záruky:** Read-only. · **Tenancy / permissions:** RLS tenant; gated `RagEvalRead`. · **Reuse / canonical pattern:** oblast 18 trend endpoint; chart komponenta z `/design` gallery. · **Data dotčena:** read eval time-series. · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-28-02
- **EC-28-02-01 — Range-cap překročen (DoS guard)** · Trigger: uživatel zvolí 90d × hour bucket. · Očekávané chování: UI volbu buď zablokuje (disable + tooltip "příliš jemný bucket pro toto okno") nebo backend vrátí 400 → toast; nikdy se nepošle neohraničený dotaz. · Mechanismus: UI cap zrcadlí oblast 19/22 range-cap. · Severity: P1 · Test: výběr nepovolené kombinace → guard, žádný request nebo zachycený 400.
- **EC-28-02-02 — Jediný bod / mezery v řadě** · Trigger: jen 1 snapshot nebo díry mezi běhy. · Očekávané chování: graf zobrazí markery/gap, ne interpolovanou lež; tooltip "bez dat". · Mechanismus: empty-segment rendering. · Severity: P2 · Test: řada s gapem → vizuálně mezera.
- **EC-28-02-03 — Reconnect SSE během sledování trendu** · Trigger: ztráta SSE spojení. · Očekávané chování: provider se reconnectne s `Last-Event-ID` (replay), poslední bod se doplní bez duplicit. · Mechanismus: oblast 21 SSE replay. · Severity: P2 · Test: drop SSE → reconnect → žádný duplicitní bod.

---

## UC-28-03 — Per-experiment srovnání (experiment A vs B, eval matice)

- **Actor / role:** `RagEvalRead`. · **Precondition:** Existují ≥2 eval experimenty (oblast 31 golden/experiment). · **Trigger:** Uživatel otevře „Experimenty" a vybere dva běhy k porovnání.
- **Main flow:**
  1. UI načte seznam experimentů `GET /v1/hybridrag/eval/experiments?window=...` (paged, RLS-scoped), vykreslí selectory A/B.
  2. Po výběru volá `GET /v1/hybridrag/eval/experiments/compare?a={idA}&b={idB}` (oblast 31) — backend vrátí side-by-side matici metrik + per-metriku delta + statistickou významnost (pokud backend poskytuje).
  3. UI renderuje srovnávací tabulku (zvýraznění lepší/horší per řádek, barevně + ikonou ne jen barvou kvůli a11y) a per-question drill (UC-28-11 retrieval drilldown odkaz).
- **Postcondition / záruky:** Read-only; výběr A/B uložen v URL query (sdílitelný odkaz, deep-link). · **Tenancy / permissions:** RLS tenant; oba experimenty musí patřit tenantovi — cizí id → 404. · **Reuse / canonical pattern:** oblast 31 compare endpoint; IDOR→404 oblast 16. · **Data dotčena:** read experiment metriky. · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-28-03
- **EC-28-03-01 — Cizí experiment id v URL (IDOR pokus)** · Trigger: uživatel ručně přepíše `b=` na id jiného tenanta. · Očekávané chování: backend 404, UI ukáže "experiment nenalezen", žádná data neuniknou. · Mechanismus: RLS + IDOR→404 (oblast 16, Law 10). · Severity: P0 · Test: deep-link s cizím id → 404 prázdný stav.
- **EC-28-03-02 — A == B (stejný experiment)** · Trigger: uživatel vybere tentýž běh v obou selectorech. · Očekávané chování: UI zablokuje compare / zobrazí hint "vyberte dva různé". · Mechanismus: client-side validace výběru. · Severity: P3 · Test: stejný výběr → disabled compare.
- **EC-28-03-03 — Experiment ještě běží (nedokončený)** · Trigger: jeden z vybraných běhů je Running (oblast 04/31). · Očekávané chování: UI ukáže status badge "běží", částečné/žádné metriky označené jako předběžné, žádné finální delta. · Mechanismus: RetrievalStatus/eval status (oblast 17/31). · Severity: P2 · Test: compare s běžícím experimentem → "předběžné".

---

## UC-28-04 — Hallucination-rate / citation-coverage detail (kvalitativní drill)

- **Actor / role:** `RagEvalRead`. · **Precondition:** Eval běh s per-question výsledky. · **Trigger:** Klik na kartu „Hallucination rate" nebo „Citation coverage".
- **Main flow:**
  1. UI volá `GET /v1/hybridrag/eval/questions?metric=hallucination&window=...&page=...` (oblast 18, paged, range-capped).
  2. Vykreslí seznam selhávajících otázek: otázka, vygenerovaná odpověď (sanitizovaná), očekávaná odpověď z golden-setu, citace (link na chunk/dokument přes oblast 13), skóre.
  3. Render obsahu odpovědí/citací prochází XSS sanitizací (žádný `dangerouslySetInnerHTML` bez sanitizace) — obsah pochází z LLM/dokumentů, je untrusted.
- **Postcondition / záruky:** Read-only. · **Tenancy / permissions:** RLS tenant; gated `RagEvalRead`. · **Reuse / canonical pattern:** oblast 13 citace, oblast 18 per-question eval. · **Data dotčena:** read eval question results. · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-28-04
- **EC-28-04-01 — XSS v odpovědi/citaci** · Trigger: dokument/LLM výstup obsahuje `<script>`/HTML. · Očekávané chování: vykresleno jako text, žádné spuštění; markdown sanitizován allowlistem. · Mechanismus: UI XSS sanitizace (frozen taxonomie). · Severity: P0 · Test: golden answer s `<img onerror>` → render jako text, žádný side-effect.
- **EC-28-04-02 — Citace odkazuje na smazaný/erased dokument** · Trigger: dokument byl GDPR-erased (oblast 20) po eval běhu. · Očekávané chování: odkaz vede na "dokument nedostupný / odstraněn", ne 500. · Mechanismus: oblast 20 erasure + graceful link. · Severity: P2 · Test: eval citace na erased doc → graceful placeholder.
- **EC-28-04-03 — Velký seznam selhání (paging)** · Trigger: stovky failujících otázek. · Očekávané chování: paged + virtualizace, žádný neomezený render. · Mechanismus: range-cap + pagination. · Severity: P2 · Test: 500 otázek → paged, plynulý scroll.

---

## UC-28-05 — Spuštění golden-set runneru z UI (trigger eval běhu)

- **Actor / role:** `RagEvalRead` + write permission (`rag.eval.run` / `RagEvalRun` — pokud backend vyžaduje vyšší práva pro spuštění; jinak gated stejnou permission). · **Precondition:** Existuje golden-set (oblast 31). · **Trigger:** Uživatel klikne „Spustit eval" a zvolí golden-set + model/konfiguraci.
- **Main flow:**
  1. UI server action volá `POST /v1/hybridrag/eval/runs` (oblast 18/31) s vybraným golden-set id + (volitelně) experiment label.
  2. Backend je long-running → vrací **202 + `Location: /v1/hybridrag/eval/runs/{id}`** (oblast 04 operations pattern). UI uloží operation id.
  3. UI zobrazí progress (status polling přes `GET .../runs/{id}` nebo SSE oblast 21), tlačítko se disabluje + spinner (double-submit guard), po dokončení invaliduje `['rag','eval',...]`.
  4. Success toast po přijetí 202 (před navigací), error toast z RFC9457.
- **Postcondition / záruky:** Vytvořen eval běh (idempotentně dle backendu); UI nezná tokeny, vše přes BFF. · **Tenancy / permissions:** RLS tenant; akce gated write permission, jinak tlačítko skryté. · **Reuse / canonical pattern:** oblast 04 (202+status), oblast 18/31; double-submit guard + loading state (Surrounding Concerns §1). · **Data dotčena:** vytváří eval run (přes endpoint, ne UI). · **Eventy:** backend publikuje eval-started/completed (oblast 18) → SSE. · **Priorita:** P1

### Edge cases UC-28-05
- **EC-28-05-01 — Double-submit (rychlý dvojklik)** · Trigger: uživatel klikne 2× než přijde 202. · Očekávané chování: druhý klik blokován (disabled + in-flight guard), vznikne max 1 běh. · Mechanismus: UI double-submit guard + backend idempotency. · Severity: P1 · Test: 2 rychlé kliky → 1 POST.
- **EC-28-05-02 — Disconnect během běhu** · Trigger: uživatel zavře tab / ztratí síť po 202. · Očekávané chování: běh pokračuje na backendu (durable, oblast 04); po návratu UI dotáhne status z `/runs/{id}`. · Mechanismus: 202 durable operation. · Severity: P2 · Test: zavřít tab → vrátit se → status viditelný.
- **EC-28-05-03 — Tlačítko bez write permission** · Trigger: read-only uživatel. · Očekávané chování: tlačítko skryté (ne disabled), aby neprozradilo existenci akce. · Mechanismus: permission-gated (skrýt vs disable). · Severity: P1 · Test: read-only → bez tlačítka.
- **EC-28-05-04 — Golden-set prázdný / smazaný mezitím** · Trigger: vybraný golden-set byl odstraněn. · Očekávané chování: backend 404/422 → toast "golden-set nedostupný", výběr resetován. · Mechanismus: RFC9457 + selection reset. · Severity: P2 · Test: stale golden-set id → graceful error.

---

## UC-28-06 — COST & usage dashboard (tokeny, latence, $/query, $/collection)

- **Actor / role:** `RagCostRead`. · **Precondition:** Tenant má cost telemetrii (oblast 19/30). · **Trigger:** Otevření `/[locale]/hybridrag/cost`.
- **Main flow:**
  1. BFF volá `GET /v1/hybridrag/cost/summary?window=30d` (oblast 19) — backend vrací agregát: tokens in/out per query, embed tokens per ingest, retrieval latency (p50/p95/p99), $/query, $/collection, total spend, počet dotazů/ingestů.
  2. UI vykreslí KPI karty + breakdown tabulky (per-collection, per-model, oblast 30) + latency histogram.
  3. Všechny částky formátované přes `Intl.NumberFormat` s měnou z config (oblast 24); tokeny jako celá čísla; latence v ms.
- **Postcondition / záruky:** Read-only; agregáty PII-redigované backendem (žádné per-uživatel-query texty). · **Tenancy / permissions:** RLS tenant; gated `RagCostRead`. · **Reuse / canonical pattern:** oblast 19 (cost telemetrie) + oblast 30 (per-model breakdown) + oblast 22 (Redis cost bucket). · **Data dotčena:** read cost projekce. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-28-06
- **EC-28-06-01 — Agregát by odhalil PII jednotlivce** · Trigger: malý vzorek (1 dotaz v okně). · Očekávané chování: backend redaguje / suppressuje pod prahem k-anonymity; UI zobrazí "nedostatek dat pro breakdown", NErekonstruuje. · Mechanismus: PII-redakce v agregátu (backend, oblast 19/20). · Severity: P0 · Test: tenant s 1 dotazem → breakdown suppressed.
- **EC-28-06-02 — Neohraničený rozsah (DoS)** · Trigger: uživatel zvolí "all time". · Očekávané chování: UI cappuje na max okno (oblast 22) + tlačítko "max" odpovídá backend limitu. · Mechanismus: range-cap. · Severity: P1 · Test: pokus o all-time → capped request.
- **EC-28-06-03 — Měnové/locale formátování (cs vs en)** · Trigger: přepnutí jazyka. · Očekávané chování: konzistentní `Intl` formát, žádný hydration mismatch, desetinná čárka v cs. · Mechanismus: i18n + hydration-safe. · Severity: P2 · Test: cs locale → "1 234,56 $" formát konzistentní SSR/CSR.
- **EC-28-06-04 — Chybějící cost data pro období (telemetrie down)** · Trigger: outage telemetrie. · Očekávané chování: graf ukáže gap + banner "data za období neúplná", ne nuly jako fakt. · Mechanismus: degradace (oblast 17). · Severity: P2 · Test: díra v cost řadě → banner.

---

## UC-28-07 — Budget vs spend a alert thresholdy (read view)

- **Actor / role:** `RagCostRead`. · **Precondition:** Nastaven budget/threshold (config oblast 24, override `RagSetting`). · **Trigger:** Sekce „Rozpočet" na cost dashboardu.
- **Main flow:**
  1. UI volá `GET /v1/hybridrag/cost/budget` (oblast 19/24) — backend vrací aktuální spend, budget limit, % využití, prahy alertů, stav (OK/Warn/Over).
  2. UI vykreslí progress bar budget vs spend, barevně + textový stav, seznam definovaných prahů a kdy byly naposledy překročeny (audit link oblast 25).
  3. Pokud budget není nastaven → empty state s odkazem na admin config (oblast 23/27 admin UI), bez možnosti měnit zde (read view).
- **Postcondition / záruky:** Read-only (změna budgetu je admin UI oblast 27, ne zde). · **Tenancy / permissions:** RLS tenant; gated `RagCostRead`. · **Reuse / canonical pattern:** oblast 19 budget endpoint, oblast 24 config registr. · **Data dotčena:** read budget/spend. · **Eventy:** backend může publikovat budget-exceeded → notifikace/SSE. · **Priorita:** P2

### Edge cases UC-28-07
- **EC-28-07-01 — Spend překročil budget (over)** · Trigger: spend > limit. · Očekávané chování: výrazný "Over budget" stav (barva+ikona+text), ale dashboard zůstává čitelný; žádné blokování čtení. · Mechanismus: stavová logika UI. · Severity: P2 · Test: over-budget data → červený stav + ikona.
- **EC-28-07-02 — Budget není nastaven** · Trigger: žádný `RagSetting` budget. · Očekávané chování: empty state, odkaz na config (jen pokud má admin permission, jinak jen text). · Mechanismus: empty-state + permission-gated odkaz. · Severity: P3 · Test: bez budgetu → CTA jen pro admina.
- **EC-28-07-03 — Realtime překročení během prohlížení** · Trigger: budget-exceeded event přijde za běhu. · Očekávané chování: SSE invaliduje budget query, progress bar se aktualizuje + nenásilný toast. · Mechanismus: SSE + invalidate. · Severity: P2 · Test: emit event → bar se přepne na Warn.

---

## UC-28-08 — MODEL COMPARISON view (kvalita × cena matice, champion/challenger)

- **Actor / role:** `RagEvalRead` ∧ `RagCostRead` (zobrazuje kvalitu i cenu). · **Precondition:** Existují data ≥2 modelů (oblast 30 routing + 31 shadow eval). · **Trigger:** Otevření „Modely / srovnání".
- **Main flow:**
  1. UI volá `GET /v1/hybridrag/models/comparison?window=...` (oblast 30/31) — backend vrací matici model A/B: kvalitativní metriky (faithfulness, citation coverage…) × cena ($/query, latence) + označení champion vs challenger + výsledky shadow evalu.
  2. UI vykreslí kvalita/cena scatter nebo tabulkovou matici se zvýrazněním champion (badge), challenger (badge) a "lepší při nižší ceně" kvadrantu.
  3. Drill na konkrétní model → odkaz na eval trend (UC-28-02) filtrovaný modelem.
- **Postcondition / záruky:** Read-only (promotion championa je admin akce oblast 30/27, ne zde). · **Tenancy / permissions:** RLS tenant; vyžaduje OBĚ permission (eval+cost), jinak degradovaný view (jen ta část, na kterou má práva). · **Reuse / canonical pattern:** oblast 30 model routing/cost + oblast 31 shadow eval. · **Data dotčena:** read model metriky. · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-28-08
- **EC-28-08-01 — Má jen jednu z dvou permission** · Trigger: uživatel má `RagCostRead` ale ne `RagEvalRead`. · Očekávané chování: kvalitativní sloupce skryté, cenové zobrazené; banner "pro kvalitu metriky chybí oprávnění", žádný leak eval dat. · Mechanismus: per-sloupec permission-gating. · Severity: P1 · Test: jen cost permission → bez kvalita sloupců.
- **EC-28-08-02 — Challenger bez dokončeného shadow evalu** · Trigger: challenger model nasazen, shadow eval ještě neběžel. · Očekávané chování: kvalita "n/a — probíhá", cena zobrazena; žádné srovnání kvality vynucené. · Mechanismus: oblast 31 status. · Severity: P2 · Test: nový challenger → kvalita n/a.
- **EC-28-08-03 — Jen jeden model (žádné srovnání)** · Trigger: tenant používá 1 model. · Očekávané chování: matice degraduje na single-model přehled + hint "přidejte challenger". · Mechanismus: empty/degraded view. · Severity: P3 · Test: 1 model → single view.

---

## UC-28-09 — AUDIT timeline viewer (config změny + operace, forenzní)

- **Actor / role:** `PlatformPermissions.AuditRead` (forenzní role). · **Precondition:** Existují audit záznamy v `hybridrag_audit_entries` + config audit (oblast 24/25). · **Trigger:** Otevření `/[locale]/hybridrag/audit`.
- **Main flow:**
  1. BFF volá `GET /v1/hybridrag/audit?window=...&entity=...&page=...` (oblast 25) — backend vrací paged timeline: kdo (actor user id/email), co (entita, akce, změněná pole JSONB), kdy (UTC), z jaké IP (minimalizovaná dle `Audit:IpStorage`).
  2. UI vykreslí timeline/tabulku s filtry (entita, actor, akce, časové okno) a expand na diff změněných polí.
  3. **PII v audit hodnotách je redigované** (`[encrypted]`/`[erased]`) DOKUD nemá uživatel `AuditRead` — s `AuditRead` backend dešifruje a UI zobrazí plaintext; po GDPR erasure → `[erased]` (oblast 25/20, crypto-shred).
- **Postcondition / záruky:** Read-only; forenzní čtení samo je auditovatelné backendem. · **Tenancy / permissions:** RLS tenant; gated `AuditRead`; bez něj nav položka skrytá a přímý přístup 403. · **Reuse / canonical pattern:** oblast 25 audit + platform `GetUserAuditTrail` pattern (CLAUDE.md §4 audit-PII crypto-shred). · **Data dotčena:** read audit entries. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-28-09
- **EC-28-09-01 — PII redakce bez AuditRead** · Trigger: uživatel s nižším oprávněním vidí audit (pokud má jen základní view). · Očekávané chování: PII pole jako `[encrypted]`, nikdy plaintext na klientu. · Mechanismus: backend rozhoduje dle `AuditRead` (oblast 25), UI jen renderuje co dostane. · Severity: P0 · Test: bez AuditRead → `[encrypted]` v payloadu i UI.
- **EC-28-09-02 — Erased subjekt (post-GDPR)** · Trigger: subjekt audit záznamu byl erased. · Očekávané chování: hodnoty `[erased]` i pro AuditRead (DEK shredded, nevratné). · Mechanismus: oblast 20 crypto-shred. · Severity: P0 · Test: erased subjekt → `[erased]` i s AuditRead.
- **EC-28-09-03 — Velký rozsah / range-cap** · Trigger: filtr "vše za rok". · Očekávané chování: UI cappuje okno + paginace; žádný neohraničený dotaz. · Mechanismus: range-cap (oblast 22). · Severity: P1 · Test: roční filtr → capped + paged.
- **EC-28-09-04 — XSS v audit diff hodnotě** · Trigger: změněná hodnota obsahuje HTML/skript. · Očekávané chování: render jako text. · Mechanismus: XSS sanitizace. · Severity: P0 · Test: audit hodnota s `<script>` → text.
- **EC-28-09-05 — Cizí tenant id ve filtru (IDOR)** · Trigger: pokus filtrovat dle cizího actor/tenant. · Očekávané chování: RLS odfiltruje, prázdný výsledek/404; žádný cross-tenant leak. · Mechanismus: RLS + oblast 16. · Severity: P0 · Test: cizí tenant filtr → prázdno.

---

## UC-28-10 — OBSERVABILITY / trace inspektor (per-span pipeline, effective-config stamp)

- **Actor / role:** `RagObservabilityRead`. · **Precondition:** Existují trace záznamy (`RagTrace`, oblast 19). · **Trigger:** Otevření „Trace" nebo drill z konkrétní konverzace/dotazu.
- **Main flow:**
  1. UI volá `GET /v1/hybridrag/traces?window=...&conversationId=...&page=...` (oblast 19) — backend vrací paged seznam trace (per dotaz: total latence, total cost, status).
  2. Po výběru `GET /v1/hybridrag/traces/{id}` → waterfall spanů: retrieve → embed → dense → BM25 → RRF → rerank → graph → generate, každý span latence + tokeny + cost + status (oblast 17 RetrievalStatus).
  3. Zobrazí **effective-config stamp** (jaká konfigurace byla pro tento dotaz aktivní — model, váhy RRF k=60, rerank on/off, freshness, oblast 24) — read-only snapshot, klíčové pro forenzní reprodukci.
- **Postcondition / záruky:** Read-only. · **Tenancy / permissions:** RLS tenant; gated `RagObservabilityRead`; trace cizího tenanta → 404. · **Reuse / canonical pattern:** oblast 19 trace + oblast 24 effective-config + oblast 17 degradace statusů. · **Data dotčena:** read `RagTrace`. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-28-10
- **EC-28-10-01 — Span selhal / degradace (partial)** · Trigger: rerank span timeout, pipeline degradovala (oblast 17). · Očekávané chování: span označen "degraded/skipped" se statusem, ne skryt; celkový stav RetrievalStatus viditelný. · Mechanismus: oblast 17 RetrievalStatus. · Severity: P1 · Test: trace s degradovaným rerankem → status badge.
- **EC-28-10-02 — Trace obsahuje query text s PII** · Trigger: dotaz uživatele obsahuje PII. · Očekávané chování: backend redaguje dle permission; UI nezobrazí plaintext PII bez oprávnění. · Mechanismus: PII redakce (oblast 19/20). · Severity: P0 · Test: PII dotaz → redigováno bez práv.
- **EC-28-10-03 — Cizí trace id (IDOR)** · Trigger: ruční přepsání trace id. · Očekávané chování: 404. · Mechanismus: RLS + oblast 16. · Severity: P0 · Test: cizí id → 404.
- **EC-28-10-04 — Velmi dlouhý trace (mnoho spanů)** · Trigger: graph-heavy dotaz se stovkami spanů. · Očekávané chování: waterfall virtualizovaný/collapsible, žádný UI freeze. · Mechanismus: virtualizace. · Severity: P2 · Test: 200-span trace → plynulý render.

---

## UC-28-11 — Retrieval-quality drilldown (per-dotaz kvalita získání)

- **Actor / role:** `RagEvalRead` ∨ `RagObservabilityRead` (dle backendu). · **Precondition:** Trace/eval s retrieval detaily. · **Trigger:** Drill z eval otázky (UC-28-04) nebo trace (UC-28-10) na "kvalitu retrievalu".
- **Main flow:**
  1. UI volá `GET /v1/hybridrag/traces/{id}/retrieval` (oblast 19/07/08) — backend vrátí kandidátní chunky: dense skóre, BM25 skóre, RRF rank (k=60), rerank skóre (Cohere rerank-3.5), freshness boost (oblast 09), který chunk skončil v odpovědi jako citace (oblast 13).
  2. UI vykreslí tabulku kandidátů seřazenou dle finálního rank, se sloupci skóre per fáze a vizuální indikací "použito v odpovědi".
  3. Drill na chunk → preview (sanitizovaný) + odkaz na dokument (oblast 02/13).
- **Postcondition / záruky:** Read-only. · **Tenancy / permissions:** RLS tenant; gated; cizí trace → 404. · **Reuse / canonical pattern:** oblast 05/06/07/08/09/13. · **Data dotčena:** read retrieval kandidáti. · **Eventy:** žádné. · **Priorita:** P2

### Edge cases UC-28-11
- **EC-28-11-01 — Rerank přeskočen (degradace)** · Trigger: Cohere nedostupný, fallback na RRF (oblast 08/17). · Očekávané chování: rerank sloupec "n/a — degradováno", rank dle RRF; jasně označeno. · Mechanismus: oblast 17. · Severity: P2 · Test: trace bez reranku → n/a sloupec.
- **EC-28-11-02 — Chunk smazán/erased** · Trigger: dokument odstraněn po dotazu. · Očekávané chování: preview "nedostupný", skóre zůstávají z trace. · Mechanismus: oblast 20. · Severity: P2 · Test: erased chunk → graceful.
- **EC-28-11-03 — XSS v chunk preview** · Trigger: chunk obsahuje HTML. · Očekávané chování: text. · Mechanismus: sanitizace. · Severity: P0 · Test: chunk s `<script>` → text.

---

## UC-28-12 — Export dashboardu (CSV/JSON snapshot agregátu)

- **Actor / role:** `RagEvalRead` / `RagCostRead` (dle dashboardu). · **Precondition:** Zobrazená data. · **Trigger:** Klik „Export" na eval nebo cost dashboardu.
- **Main flow:**
  1. UI server action volá existující read-endpoint s `format=csv` (nebo UI sestaví CSV z již načteného TanStack cache snapshotu — bez nového výpočtu).
  2. BFF streamuje soubor klientovi (Content-Disposition); tokeny zůstávají server-side.
  3. Export respektuje aktuální filtry (okno, model breakdown) a PII-redakci backendu — exportovaná data jsou stejně redigovaná jako UI.
- **Postcondition / záruky:** Read-only; export neobchází redakci ani RLS. · **Tenancy / permissions:** RLS tenant; gated stejnou permission jako dashboard. · **Reuse / canonical pattern:** oblast 19/18 read-endpoint format param; BFF stream. · **Data dotčena:** read. · **Eventy:** žádné (volitelně audit "export" v oblast 25). · **Priorita:** P3

### Edge cases UC-28-12
- **EC-28-12-01 — Export by obešel PII redakci** · Trigger: pokus exportovat raw. · Očekávané chování: export jde přes stejný redigovaný endpoint; žádné plaintext PII. · Mechanismus: backend redakce (oblast 19/20). · Severity: P0 · Test: export → žádné PII v CSV.
- **EC-28-12-02 — Velký export (range-cap)** · Trigger: export obrovského okna. · Očekávané chování: cap nebo streamování v dávkách; žádný OOM v BFF. · Mechanismus: range-cap + streaming. · Severity: P1 · Test: max okno → streamovaný export.
- **EC-28-12-03 — CSV injection (formule v hodnotě)** · Trigger: audit/eval hodnota začíná `=`/`+`/`-`/`@`. · Očekávané chování: hodnoty escapované (prefix `'`) proti CSV/formula injection. · Mechanismus: CSV sanitizace. · Severity: P1 · Test: hodnota `=cmd()` → escapováno.

---

## UC-28-13 — Společný layout dashboardů: navigace, filtry, sdílený stav

- **Actor / role:** Libovolný s aspoň jednou Rag*Read permission. · **Precondition:** Modul entitled. · **Trigger:** Vstup do sekce „HybridRag → Dashboardy".
- **Main flow:**
  1. App shell vykreslí pod-navigaci (Eval / Cost / Modely / Audit / Trace) — položky filtrované dle permission claimů (uživatel vidí jen sekce, na které má právo; casing lowercase).
  2. Globální filtr (časové okno, volitelně collection/model) je sdílený přes URL query + TanStack Query klíče, takže přepínání sekcí neztratí kontext a nefetchuje zbytečně.
  3. Jeden SSE realtime provider na úrovni layoutu invaliduje relevantní query klíče (eval-completed, budget-exceeded) napříč sekcemi.
- **Postcondition / záruky:** Konzistentní filtr/stav; žádné duplicitní fetch; data fetchnuta jednou a sdílena. · **Tenancy / permissions:** RLS tenant; per-sekce permission-gating. · **Reuse / canonical pattern:** `modularplatform-frontend` skill (app shell, jeden SSE provider, jeden data source). · **Data dotčena:** žádná (orchestrace). · **Eventy:** konzumuje SSE (oblast 21). · **Priorita:** P1

### Edge cases UC-28-13
- **EC-28-13-01 — Uživatel bez žádné Rag*Read permission** · Trigger: má `hybridrag` entitled ale žádnou dashboard permission. · Očekávané chování: celá sekce "Dashboardy" skrytá v navigaci; přímý přístup → 403 boundary. · Mechanismus: permission-gated nav + BFF guard. · Severity: P1 · Test: bez Rag*Read → bez sekce.
- **EC-28-13-02 — Session expiry uprostřed prohlížení** · Trigger: token vyprší během práce. · Očekávané chování: BFF refresh (oblast Identity), nebo redirect na login se zachováním návratové URL; žádný leak ani prázdný 500. · Mechanismus: BFF auth MODEL A + refresh rotation. · Severity: P1 · Test: expirace → silent refresh nebo čistý redirect.
- **EC-28-13-03 — Dark mode konzistence napříč grafy** · Trigger: přepnutí dark mode. · Očekávané chování: grafy/karty/timeline respektují theme tokeny, žádný bílý flash, kontrast splňuje WCAG AA. · Mechanismus: next-themes + design tokeny. · Severity: P2 · Test: dark mode → kontrast + žádný flash.
- **EC-28-13-04 — i18n chybějící klíč** · Trigger: nový metrika label bez cs překladu. · Očekávané chování: fallback na klíč/en, žádný raw dotted klíč v UI (NESTED namespace), žádný dev overlay blokující kliky. · Mechanismus: next-intl NESTED klíče (frozen past). · Severity: P2 · Test: cs s chybějícím klíčem → fallback, ne raw klíč.

---

## UC-28-14 — A11y a responsive napříč dashboardy (grafy, tabulky, timeline)

- **Actor / role:** Libovolný (vč. uživatelů asistivních technologií, mobilních). · **Precondition:** Dashboard otevřen. · **Trigger:** Interakce klávesnicí / screen readerem / na malém viewportu.
- **Main flow:**
  1. Grafy mají textovou/tabulkovou alternativu (aria-label, `<table>` fallback), KPI karty mají sémantické heading hierarchie, timeline je list s aria.
  2. Filtry, selectory, paginace plně ovladatelné klávesnicí; modaly (compare, trace detail) mají focus-trap a Esc; focus management po zavření vrací fokus na trigger.
  3. Responsive: na mobilu se matice/waterfall přepínají na stackované karty/akordeon; tabulky horizontálně scrollovatelné s fixed hlavičkou.
- **Postcondition / záruky:** WCAG AA kontrast + keyboard + SR; žádná informace jen barvou (vždy + ikona/text). · **Tenancy / permissions:** N/A (prezentační). · **Reuse / canonical pattern:** `frontend-design` + owned shadcn komponenty, design gallery `/design`. · **Data dotčena:** žádná. · **Eventy:** žádné. · **Priorita:** P1

### Edge cases UC-28-14
- **EC-28-14-01 — Graf jen barvou rozlišuje serie** · Trigger: barvoslepý uživatel. · Očekávané chování: serie rozlišené i tvarem/labelem, legenda textová. · Mechanismus: a11y design. · Severity: P2 · Test: a11y audit grafu.
- **EC-28-14-02 — Focus-trap v trace/compare modalu** · Trigger: Tab uvnitř modalu. · Očekávané chování: fokus cyklí uvnitř, Esc zavře, fokus zpět na trigger. · Mechanismus: focus mgmt. · Severity: P2 · Test: keyboard přes modal.
- **EC-28-14-03 — Mobilní waterfall/matice** · Trigger: úzký viewport. · Očekávané chování: přepnutí na akordeon/stack, žádný horizontální přetok mimo obrazovku. · Mechanismus: responsive breakpointy. · Severity: P2 · Test: 375px → stackovaný layout.
