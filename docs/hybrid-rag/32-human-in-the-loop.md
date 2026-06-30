# Oblast 32 — Human-in-the-loop — configurable review, approval & feedback

> **Účel oblasti.** HybridRag potřebuje na několika místech zařadit člověka do smyčky: entity-resolution merge review (oblast 11, UC-11-05), golden-set harvest review (oblast 18, UC-18-07), schvalování citlivých odpovědí (legal/medical), schvalování ingest citlivých kolekcí a sběr uživatelského feedbacku. Bez konsolidace by každé místo mělo vlastní frontu, vlastní stav, vlastní locking a vlastní notifikace — porušení DRY (Zákon 4) a zdroj nekonzistence. **Oblast 32 je JEDNA sjednocená abstrakce HITL**: review queue store (`RagReviewItem`), approval-gate primitiv (blocking i async), deklarativní routing policy (`RagReviewPolicy`), three-band thresholdy, explicitní abstain path, eskalace na více signálů, reviewer assignment + RBAC, durable resumable handoff, feedback capture endpoint a feedback-loop do golden setu (oblast 31). Všechno mutace přes outbox, dlouhá práce přes 202/operations, durable resume přes sagu, notifikace přes `IRealtimePublisher`, audit přes `AuditInterceptor` → `hybridrag_audit_entries`.
>
> **Konsolidace (NEDUPLIKOVAT):** entity-res review (oblast 11 UC-11-05) i golden harvest review (oblast 18 UC-18-07) se PŘEPÍŠOU na produkci `RagReviewItem` s `type=entity_merge` resp. `type=feedback_label`/`answer_approval`. Tato oblast je vlastníkem fronty; oblasti 11/18 jsou jen producenti review položek a konzumenti rozhodnutí.
>
> **Reuse seamy (file:line):** vertical slice = `Features/Users/RegisterUser/*`; read = `GetProfileHandler.cs:12` (`IReadDbContextFactory`); outbox = `RegisterUserHandler.cs:22`; long work 202 = `StartDemoOperationHandler.cs:17` (`IOperationStore`); worker handler = `ProvisionCreditAccountHandler.cs:13`; saga = `CreditPurchaseSaga.cs:30`; provider port (fake-under-flag) = `IStripeGateway` + `MarketingModule.cs:51`; chat/agent = `ClaudeVibeAgentGateway.cs:85`; realtime = `IRealtimePublisher` `Ports.cs:98`; metriky = `PlatformMetrics.Meter` `PlatformMetrics.cs:19`; OTel GenAI semconv spany (oblast 19); audit = `AuditInterceptor` → `hybridrag_audit_entries`; PII = `[PersonalData]`+`IDataSubject` crypto-shred.

---

## UC-32-01 — Sjednocený review-queue store (RagReviewItem) jako jediná abstrakce

- **Actor / role:** Systém (producenti: oblasti 11/18/13/01) + reviewer (RBAC `RagReview`). · **Precondition:** Modul HybridRag enabled; tabulka `hybridrag_review_items` migrovaná; `HybridRagDbContext` registrovaný. · **Trigger:** Kterýkoli interní handler potřebuje lidské rozhodnutí a zavolá `IReviewQueue.EnqueueAsync(...)` (port v Core). · **Main flow:**
  1. Producent (např. entity-res merge handler oblast 11, nebo answer handler oblast 13 při three-band serve-with-flag) sestaví `EnqueueReviewCommand` s `Type` (`entity_merge|answer_approval|ingest_approval|feedback_label`), `Scope` (`Tenant|User`), `Payload` (JSONB — strukturovaný návrh k posouzení), `Priority`, `Signals` (faithfulness, coverage, cost, atd.) a `SuggestedDecision`.
  2. Handler vloží `RagReviewItem` (Id `Guid.CreateVersion7()`, `Status=Pending`, `CreatedUtc=IClock.UtcNow`, `TenantId` stamped interceptorem, `UserId` z `ITenantContext.UserId` u User-scope nebo owner item), `ReservedBy=null`, `ReservedUntilUtc=null`, `DecidedBy=null`, `Decision=null`.
  3. `SaveChangesAndFlushMessagesAsync` commitne + outboxem publikuje `ReviewItemEnqueuedIntegrationEvent` (Contracts).
  4. Worker handler routuje notifikaci (UC-32-12) + případně přiřadí reviewera (UC-32-10).
- **Postcondition / záruky:** Review položka existuje právě jednou na business důvod (dedup přes UNIQUE `(Type, DedupKey)` — viz EC-32-01-02); je viditelná jen v rámci tenanta/usera dle Scope. · **Tenancy / permissions:** `ITenantScoped` vždy; `IUserOwned` u Scope=User → per-user RLS (oblast 16). Producent NESMÍ přebrat `UserId` z payloadu/LLM — vždy `ITenantContext.UserId`. · **Reuse / canonical pattern:** entita jako `AuditableEntity` (oblast 25); enqueue command = vertical slice `Features/Users/RegisterUser/*`; outbox = `RegisterUserHandler.cs:22`; konsoliduje oblast 11 UC-11-05 + oblast 18 UC-18-07. · **Data dotčena:** `hybridrag_review_items`, `hybridrag_audit_entries`. · **Eventy:** `ReviewItemEnqueuedIntegrationEvent`. · **Priorita:** P1

### Edge cases UC-32-01
- **EC-32-01-01 — Item bez Scope/UserId u Scope=User** · Trigger: producent nastaví `Scope=User` ale neuvede ownera. · Očekávané chování: `EnqueueReviewValidator` (FluentValidation `.WithErrorCode("rag.review.user_scope_requires_owner")`) odmítne → 400. · Mechanismus: ValidationBehavior (oblast 17 degradation/validace) + Zákon 10. · Severity: P1 · Test: integration — enqueue User-scope bez UserId → ValidationException.
- **EC-32-01-02 — Duplicitní enqueue stejného business důvodu** · Trigger: entity-res vygeneruje stejný merge-candidate dvakrát (re-ingest). · Očekávané chování: UNIQUE `(Type, DedupKey)` → druhý insert padne na `DbUpdateException`, handler vrátí existující Pending item (idempotentní). · Mechanismus: idempotency UNIQUE key + catch `DbUpdateException` (Zákon 5, oblast 25). · Severity: P1 · Test: dva enqueue se stejným DedupKey → jeden řádek, žádná chyba.
- **EC-32-01-03 — Payload obsahuje PII (e-mail subjektu, jméno)** · Trigger: feedback_label item nese původní dotaz uživatele s PII; entity_merge nese alias se jménem. · Očekávané chování: PII pole v payloadu označená `[PersonalData]`+`[Encrypted]` → uložena jako `penc:v2` envelope, dešifrují se jen reviewerovi s oprávněním; po GDPR erasure → `[erased]`. · Mechanismus: PII at-rest (oblast 20) + `IDataSubject` crypto-shred. · Severity: P0 · Test: enqueue s PII → DB sloupec je ciphertext; reviewer čte plaintext; po shred → `[erased]`.

---

## UC-32-02 — Reservation / locking review položky s TTL (dva revieweři nedělají totéž)

- **Actor / role:** Reviewer (RBAC `RagReview`). · **Precondition:** Existuje `RagReviewItem` ve `Status=Pending`. · **Trigger:** `POST /v1/hybridrag/review/{id}/reserve`. · **Main flow:**
  1. Reviewer otevře frontu (UC-32-09) a klikne „vzít k posouzení".
  2. Handler provede **atomický `ExecuteUpdate` guard**: `db.ReviewItems.Where(r => r.Id == id && (r.Status == Pending || (r.Status == Reserved && r.ReservedUntilUtc < now))).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, Reserved).SetProperty(r => r.ReservedBy, userId).SetProperty(r => r.ReservedUntilUtc, now + TTL))`.
  3. `rows == 1` ⇒ rezervace získána; `rows == 0` ⇒ už drží někdo jiný a TTL neuplynul → `ConflictException("rag.review.already_reserved")`.
  4. TTL (config `Rag:Review:ReservationTtlMinutes`, default 15 min). Po uplynutí TTL je item znovu volný (krok 2 podmínka `ReservedUntilUtc < now`).
- **Postcondition / záruky:** Nejvýše jeden aktivní vlastník rezervace v daném okamžiku; expired rezervace se nestane „uzamčená navždy". · **Tenancy / permissions:** RLS scope + RBAC `RagReview`; reserve cizí tenant item → 404 (RLS). · **Reuse / canonical pattern:** atomický `ExecuteUpdate` guard = stejný princip jako Billing debit (CLAUDE.md §9 money correctness, `ExecuteUpdate` WHERE), bez raw SQL (Zákon 5). · **Data dotčena:** `hybridrag_review_items` (Status/ReservedBy/ReservedUntilUtc). **Pozn.:** `ExecuteUpdate` obchází audit/xmin — rezervace je provozní stav, ne auditovaná mutace; finální rozhodnutí (UC-32-03) jde přes tracked entitu + audit. · **Eventy:** žádný (interní stav). · **Priorita:** P0

### Edge cases UC-32-02
- **EC-32-02-01 — Dva revieweři reservují současně (race)** · Trigger: dva `reserve` na stejné Pending item v tomtéž okamžiku. · Očekávané chování: jeden `rows==1` (vyhraje), druhý `rows==0` → 409 `rag.review.already_reserved`. · Mechanismus: atomický conditional `ExecuteUpdate` (DB-level row lock) — pessimistic guard, bez double-assignmentu. · Severity: P0 · Test: 2-way concurrent reserve → právě jeden uspěje.
- **EC-32-02-02 — Rezervace vyprší před rozhodnutím** · Trigger: reviewer drží item, odejde, TTL uplyne, jiný reviewer reservuje. · Očekávané chování: druhý reviewer rezervaci získá; pokud první pak pošle decide → guard `ReservedBy == userId` selže → 409 `rag.review.reservation_expired`. · Mechanismus: decide guard kontroluje aktuální `ReservedBy` (UC-32-03). · Severity: P1 · Test: reserve → posun času za TTL → druhý reserve OK → první decide → 409.
- **EC-32-02-03 — Reviewer prodlouží práci přes TTL legitimně** · Trigger: dlouhé posuzování. · Očekávané chování: `POST /review/{id}/heartbeat` (nebo re-reserve vlastníkem) prodlouží `ReservedUntilUtc` — guard `ReservedBy == userId OR expired` dovolí re-reserve vlastníkovi. · Mechanismus: idempotentní re-reserve vlastníkem. · Severity: P2 · Test: vlastník re-reserve → TTL posunut, rows==1.

---

## UC-32-03 — Idempotentní rozhodnutí (approve / deny / edit) s auditem

- **Actor / role:** Reviewer s rezervací (RBAC dle kategorie — UC-32-11). · **Precondition:** Item `Status=Reserved`, `ReservedBy == caller`, neprošlý TTL. · **Trigger:** `POST /v1/hybridrag/review/{id}/decide` `{ decision: approve|deny|edit, correctedPayload?, feedback?, decisionToken }`. · **Main flow:**
  1. Handler načte tracked item, ověří `ReservedBy == ITenantContext.UserId` a `Status==Reserved`.
  2. Ověří RBAC pro `Type` (UC-32-11) — kdo smí approvovat CO.
  3. Nastaví `Status=Decided`, `Decision`, `DecidedBy`, `DecidedUtc`, `DecisionReason`, případně `CorrectedPayload` (u `edit`).
  4. `SaveChangesAndFlushMessagesAsync` → publikuje `ReviewItemDecidedIntegrationEvent` (nese `Type`, `Decision`, `ItemId`, `CorrelationId`).
  5. Audit zachytí změnu (xmin + `AuditInterceptor` → změněná pole do `hybridrag_audit_entries`).
  6. Konzument rozhodnutí (saga blocking-gate UC-32-04, nebo entity-res apply, nebo golden harvest oblast 31) reaguje na event.
- **Postcondition / záruky:** Rozhodnutí je idempotentní — opakovaný `decide` se stejným `decisionToken` na již `Decided` item vrátí uloženy výsledek (žádný dvojí side-effect). Identita rozhodovatele auditována (kdo/co/proč/kdy — Zákon, oblast 25). · **Tenancy / permissions:** RLS + RBAC; rozhodnutí cizí item → 404. Identita rozhodovatele z tokenu, nikdy z body. · **Reuse / canonical pattern:** tracked mutace + xmin + `ConcurrencyRetryBehavior`; idempotency UNIQUE `(ItemId, DecisionToken)` + catch `DbUpdateException`; outbox `RegisterUserHandler.cs:22`. · **Data dotčena:** `hybridrag_review_items`, `hybridrag_audit_entries`. · **Eventy:** `ReviewItemDecidedIntegrationEvent`. · **Priorita:** P0

### Edge cases UC-32-03
- **EC-32-03-01 — Double-decide (dvojklik / retry)** · Trigger: reviewer pošle decide dvakrát rychle za sebou. · Očekávané chování: UNIQUE `(ItemId, DecisionToken)` → druhý insert decision-log padne `DbUpdateException`, vráti se první rozhodnutí; side-effect se NEAPLIKUJE podruhé. · Mechanismus: idempotency UNIQUE key + catch `DbUpdateException` (Zákon 5). · Severity: P0 · Test: 2× decide stejný token → jeden event, jeden side-effect.
- **EC-32-03-02 — Decide bez platné rezervace** · Trigger: item `Pending` (nikdy nereservován) nebo rezervace vypršela / patří jinému. · Očekávané chování: guard `Status==Reserved && ReservedBy==caller` selže → 409 `rag.review.not_reserved_by_caller`. · Mechanismus: stav-machine guard. · Severity: P1 · Test: decide na Pending → 409.
- **EC-32-03-03 — Konkurenční decide a re-reserve (xmin)** · Trigger: dva paralelní zápisy na tracked item. · Očekávané chování: xmin concurrency token serializuje; `ConcurrencyRetryBehavior` (5× backoff, čistí tracker) přehraje; finálně jen jeden Decided. · Mechanismus: optimistic concurrency (CLAUDE.md §4). · Severity: P1 · Test: paralelní decide → žádná ztráta, deterministický finální stav.

---

## UC-32-04 — Blocking approval gate (interrupt → emit request → wait → durable resume)

- **Actor / role:** Systém (citlivá mutace — např. delete kolekce, schválení odpovědi v `<0.70` bandu, irreversible action) + schvalovatel. · **Precondition:** Routing policy (UC-32-06) označí action class jako gated. · **Trigger:** Handler citlivé akce při commitu zjistí, že akce vyžaduje approval. · **Main flow:**
  1. Handler **nezapíše gated effekt**; místo toho zahájí **`ApprovalGateSaga`** (Wolverine saga, EF-persisted v `HybridRagDbContext`) se stabilní `Id` (= GateId), uloží `PendingEffect` (serializovaný command, který se má provést po schválení) do saga rowu.
  2. Saga enqueue `RagReviewItem` (`Type=answer_approval|ingest_approval`, UC-32-01) + publikuje `ApprovalRequestedIntegrationEvent`.
  3. Akce vrátí volajícímu **202 + `Location: /v1/hybridrag/operations/{operationId}`** (operace ve stavu `Pending` — oblast 04/23); jen gated effekt je zadržen, ostatní (ne-gated) side-effekty proběhly normálně.
  4. Schvalovatel rozhodne (UC-32-03) → `ReviewItemDecidedIntegrationEvent`.
  5. Saga ten event přijme (korelace přes GateId): **approve** → dispatchne `PendingEffect` command (idempotentně) → operace `Succeeded`; **deny** → operace `Failed` s důvodem, effekt se nikdy neprovede; **edit** → dispatchne opravený command.
  6. Saga `MarkCompleted` až po terminálním stavu.
- **Postcondition / záruky:** Citlivý effekt se provede **právě tehdy a jen tehdy** když schválen; rozhodnutí za hodiny → deterministický durable resume (saga přežije restart). · **Tenancy / permissions:** Gate scope = scope akce; RBAC schvalovatele dle kategorie (UC-32-11). · **Reuse / canonical pattern:** saga `CreditPurchaseSaga.cs:30` (EF-persisted, terminal-state guard, late-confirmation honored); 202/status `StartDemoOperationHandler.cs:17` (`IOperationStore`); worker handler `ProvisionCreditAccountHandler.cs:13`. · **Data dotčena:** saga tabulka v module DbContext, `operations` (oblast 04), `hybridrag_review_items`. · **Eventy:** `ApprovalRequestedIntegrationEvent`, konzumuje `ReviewItemDecidedIntegrationEvent`, `ApprovalResolvedIntegrationEvent`. · **Priorita:** P0

### Edge cases UC-32-04
- **EC-32-04-01 — Blocking gate spadne (crash) před resume** · Trigger: Worker spadne mezi approve eventem a dispatchem PendingEffect. · Očekávané chování: saga je durable (EF-persisted) + envelope durable; po restartu Wolverine doručí `ReviewItemDecidedIntegrationEvent` znovu, saga (idempotentní guard na `Status`) dokončí dispatch effektu právě jednou. · Mechanismus: durable saga + outbox/inbox dedup (CLAUDE.md §4 messaging resilience). · Severity: P0 · Test: zabít worker po decide, restart → effekt proveden právě jednou.
- **EC-32-04-02 — Gated effekt by se provedl dvakrát (retry envelope)** · Trigger: PendingEffect command re-doručen po retry. · Očekávané chování: cílový command má vlastní idempotency UNIQUE key → druhé provedení vrátí existující stav. · Mechanismus: idempotency cílového commandu (Zákon 5). · Severity: P0 · Test: re-deliver PendingEffect → jeden side-effect.
- **EC-32-04-03 — Approval nikdy nepřijde (abandon)** · Trigger: schvalovatel nerozhodne v `Rag:Review:ApprovalTimeoutHours`. · Očekávané chování: saga `TimeoutMessage` → eskalace (UC-32-08) NEBO abandon → operace `Failed`/`Abstained` (dle policy), effekt se neprovede. · Mechanismus: saga timeout (jako `CreditPurchaseSaga` abandon). · Severity: P1 · Test: žádné rozhodnutí → po timeoutu operace terminalizuje, effekt nikdy.
- **EC-32-04-04 — Late approval po abandon** · Trigger: schvalovatel rozhodne po vypršení timeoutu. · Očekávané chování: saga `Status` už terminal → rozhodnutí honored jako no-op (statický `NotFound`/ignored), žádný side-effekt; reviewer dostane info „již uzavřeno". · Mechanismus: terminal-state guard + late-confirmation honored (vzor `CreditPurchaseSaga`). · Severity: P1 · Test: decide po abandon → no-op, žádný effekt.

---

## UC-32-05 — Async review queue gate (non-blocking — odpověď servírovaná while-review)

- **Actor / role:** Systém (answer handler oblast 13, serve-with-flag band) + reviewer. · **Precondition:** Three-band threshold (UC-32-07) zařadí odpověď do pásma `0.70–0.85` (serve-with-flag). · **Trigger:** Answer handler dokončí generování v serve-with-flag bandu. · **Main flow:**
  1. Handler **servíruje odpověď uživateli okamžitě** s příznakem `ReviewPending=true` (a UI flagem „čeká na ověření").
  2. Současně enqueue `RagReviewItem` (`Type=answer_approval`, async non-blocking) + publikuje event.
  3. Odpověď + citace + trace (oblast 13) se uloží do `RagConversation/Turn/AnswerCitation/Trace` se značkou pending-review.
  4. Reviewer offline rozhodne (UC-32-03): **approve** → flag se sundá; **deny** → turn se označí `Retracted`, uživateli se přes realtime (oblast 21/`IRealtimePublisher`) pošle korekce / stažení; **edit** → opravená odpověď nahradí + push do golden setu (oblast 31).
- **Postcondition / záruky:** UX není blokované; každá flagged odpověď je nakonec posouzena; deny vede k auditovatelné korekci. · **Tenancy / permissions:** Item Scope=User (owner = autor dotazu); reviewer RBAC `RagReview`. · **Reuse / canonical pattern:** realtime `IRealtimePublisher` `Ports.cs:98` (post-commit push); answer trace oblast 13; harvest do golden oblast 31. · **Data dotčena:** `hybridrag_review_items`, `hybridrag_rag_turns` (flag), `hybridrag_answer_citations`. · **Eventy:** `ReviewItemEnqueuedIntegrationEvent`, `ReviewItemDecidedIntegrationEvent`, realtime `answer.review_resolved`. · **Priorita:** P1

### Edge cases UC-32-05
- **EC-32-05-01 — Uživatel mezitím smaže konverzaci** · Trigger: deny dorazí po smazání turnu. · Očekávané chování: review konzument zjistí, že turn neexistuje → rozhodnutí se zaznamená do auditu, žádný realtime push (cílový stream zavřen), item `Decided`. · Mechanismus: graceful no-op, item terminalizuje. · Severity: P2 · Test: smazat turn → deny → žádný crash, audit zapsán.
- **EC-32-05-02 — Realtime push selže (uživatel offline)** · Trigger: deny/edit korekce při offline klientovi. · Očekávané chování: push je best-effort; trvalý fakt (retracted/edited stav turnu) je v DB, klient ho uvidí při příštím loadu nebo přes replay (oblast 21). · Mechanismus: realtime = UX smoothing, durable fakt v modulu (CLAUDE.md realtime replay). · Severity: P2 · Test: offline klient → DB stav správný, push se neztratí jako fakt.

---

## UC-32-06 — Deklarativní routing policy (RagReviewPolicy, versioned, bez deploye)

- **Actor / role:** Admin tenanta (RBAC `RagAdmin`). · **Precondition:** Modul enabled; admin endpoint dostupný. · **Trigger:** `PUT /v1/hybridrag/admin/review-policy` (per-tenant/scope). · **Main flow:**
  1. Admin definuje `RagReviewPolicy`: které `ActionClass` (answer/entity_merge/ingest/delete_collection/…) vyžadují approval, thresholdy three-band per use-case, `SamplingPercent` (kolik % auto-served odpovědí jde i tak na async review), `ReviewerRole`/`Team`, `NReviewersRequired`, `ApprovalTimeoutHours`, eskalační řetězec.
  2. Uložení vytvoří **novou verzi** policy (`Version` inkrement, `EffectiveFromUtc`), stará verze zůstává (append-only) pro audit a pro běžící gates (UC-32-13).
  3. Producenti review položek čtou **aktivní verzi** policy přes `IReviewPolicyProvider` (cache, invalidace na change event).
  4. Změna se projeví bez deploye (config v DB, ne v appsettings — registr přes oblast 24 `RagSetting`, ale policy je richer entita).
- **Postcondition / záruky:** Routing rozhodnutí jsou deklarativní, verzované, auditovatelné; změna nevyžaduje restart. · **Tenancy / permissions:** Policy je `ITenantScoped`; jen `RagAdmin`. Platform-admin může nastavit default napříč tenanty (oblast 23). · **Reuse / canonical pattern:** config-registry oblast 24 (`Rag:*` override přes `RagSetting`); admin slice oblast 23; versioned append-only jako subscription mirror princip. · **Data dotčena:** `hybridrag_review_policies` (append-only verze), `hybridrag_audit_entries`. · **Eventy:** `ReviewPolicyChangedIntegrationEvent` (invaliduje cache). · **Priorita:** P1

### Edge cases UC-32-06
- **EC-32-06-01 — Policy zmizí / žádná verze** · Trigger: tenant nemá nastavenou policy. · Očekávané chování: použije se bezpečný **default** (config `Rag:Review:DefaultPolicy` — konzervativní: citlivé kategorie gated, thresholdy 0.85/0.70). Fail-safe, ne fail-open. · Mechanismus: default policy provider; oblast 24. · Severity: P1 · Test: tenant bez policy → default aplikován, citlivé akce gated.
- **EC-32-06-02 — Nevalidní policy (threshold mimo 0–1, prázdný reviewer pro gated kategorii)** · Trigger: admin pošle threshold 1.5 nebo gated kategorii bez reviewer role. · Očekávané chování: `ReviewPolicyValidator` → 400 `rag.review.policy_invalid`. · Mechanismus: ValidationBehavior. · Severity: P1 · Test: threshold>1 → 400.
- **EC-32-06-03 — Změna policy během běžícího reviewu (mid-review)** · Trigger: admin změní thresholdy, zatímco existují Pending items vytvořené pod starou verzí. · Očekávané chování: běžící items si pamatují `PolicyVersion` z okamžiku enqueue; jejich routing/RBAC se vyhodnocuje proti TÉ verzi (deterministicky); nové items používají novou verzi. · Mechanismus: versioned policy snapshot na item (jako Stripe object-state mirror, oblast 13 consent PolicyVersion vzor). · Severity: P1 · Test: enqueue → změna policy → decide → použita stará verze, nový enqueue → nová verze.

---

## UC-32-07 — Three-band thresholdy + explicitní ABSTAIN path

- **Actor / role:** Systém (answer handler oblast 13). · **Precondition:** Odpověď vygenerována, spočten `faithfulness` skóre (oblast 18 eval signál) + `retrieval coverage` (oblast 17). · **Trigger:** Po generování, před servírováním. · **Main flow:**
  1. Handler načte thresholdy z aktivní policy (UC-32-06) per use-case (`Rag:Review:Bands:*`, override per kategorie — legal/medical ~1.0).
  2. Vyhodnotí pásmo:
     - `faithfulness ≥ HighBand (default 0.85)` → **auto-serve** (volitelně sampling % do async review, UC-32-06).
     - `MidBand (0.70) ≤ faithfulness < HighBand` → **serve-with-flag + async review** (UC-32-05).
     - `faithfulness < MidBand (0.70)` → **ABSTAIN + blocking review** (UC-32-04): NEvrátí halucinovanou odpověď, vrátí explicitní „nevím / nedostatečná opora" a enqueue blocking item; po lidském rozhodnutí buď přijde ručně doplněná odpověď, nebo trvalý abstain.
  3. ABSTAIN je zaznamenán jako `RetrievalStatus=Abstained` (oblast 17), s důvodem (low coverage / low confidence), a NEpočítá se jako úspěšná odpověď v metrikách (oblast 19).
- **Postcondition / záruky:** Žádná odpověď pod prahem se nevydá jako fakt; abstain je first-class stav, ne chyba. Legal/medical kategorie ~1.0 → prakticky vždy review. · **Tenancy / permissions:** Thresholdy per-tenant/scope dle policy. · **Reuse / canonical pattern:** degradation/`RetrievalStatus` oblast 17; eval faithfulness oblast 18; metriky `PlatformMetrics.Meter` `PlatformMetrics.cs:19` (`platform.hybridrag.abstain_total`). · **Data dotčena:** `hybridrag_rag_turns` (band, status), `hybridrag_review_items`. · **Eventy:** případně `ReviewItemEnqueuedIntegrationEvent`; metrika abstain. · **Priorita:** P0

### Edge cases UC-32-07
- **EC-32-07-01 — Faithfulness skóre nedostupné (eval gateway down)** · Trigger: eval signál nelze spočítat (oblast 18 degradace). · Očekávané chování: fail-safe → zachází se jako `< MidBand` → abstain + review (NE auto-serve). · Mechanismus: degradace fail-closed (oblast 17). · Severity: P0 · Test: eval down → odpověď NEauto-served, jde na review.
- **EC-32-07-02 — Override thresholdu per use-case (legal kolekce)** · Trigger: kolekce označená kategorií `legal` → policy override 0.98/0.95. · Očekávané chování: i odpověď s faithfulness 0.90 jde na review (band shift). · Mechanismus: per-kategorie override v policy (UC-32-06). · Severity: P1 · Test: legal kolekce + 0.90 → serve-with-flag/review, ne auto-serve.
- **EC-32-07-03 — Halucinace místo abstain (regrese)** · Trigger: handler omylem vrátí LLM výstup i pod prahem. · Očekávané chování: kontacjt test ověří, že `< MidBand` cesta NIKDY nevrací model-generated answer body, vždy abstain payload + held. · Mechanismus: explicitní abstain path (Zákon — žádná halucinace). · Severity: P0 · Test: nasimulovat faithfulness 0.5 → response je abstain, ne text odpovědi.

---

## UC-32-08 — Eskalace na více signálů (faithfulness, coverage, cost/retry/tool/latency cap, irreversible)

- **Actor / role:** Systém. · **Precondition:** Běží odpověď/agentní smyčka (oblast 13/15 MCP tool-calls). · **Trigger:** Některý ze sledovaných signálů překročí cap definovaný policy. · **Main flow:**
  1. Handler/agent sleduje signály: `faithfulness`, `retrieval coverage`, `cost` (oblast 19/22 cost bucket), `retryCount`, `toolCallCount` (oblast 15), `latency`, a `actionClass` (irreversible? oblast 04).
  2. Eskalační pravidlo (policy): pokud `faithfulness < band` **NEBO** `coverage < min` **NEBO** `cost > capUsd` **NEBO** `retries > max` **NEBO** `toolCalls > max` **NEBO** `latency > capMs` **NEBO** `actionClass ∈ irreversible` → eskaluj.
  3. Eskalace = povýšení na blocking review (UC-32-04) nebo na vyšší stupeň reviewer chainu (UC-32-10), s `EscalationReason` (který signál a hodnota).
  4. Irreversible action class (delete kolekce, hromadná erasure, export celého korpusu) → VŽDY blocking gate bez ohledu na ostatní signály.
- **Postcondition / záruky:** Rizikové operace nejsou tiše provedeny; každá eskalace nese strojově čitelný důvod. · **Tenancy / permissions:** Eskalační prahy per policy; irreversible kategorie RBAC-gated. · **Reuse / canonical pattern:** cost bucket oblast 22; tool-call trace oblast 15; OTel GenAI spany oblast 19; metriky `PlatformMetrics.Meter`. · **Data dotčena:** `hybridrag_review_items` (EscalationReason, Signals JSONB), `hybridrag_rag_traces`. · **Eventy:** `ReviewEscalatedIntegrationEvent`. · **Priorita:** P1

### Edge cases UC-32-08
- **EC-32-08-01 — Eskalační smyčka (escalation loop)** · Trigger: eskalace vytvoří akci, která sama opět eskaluje → nekonečno. · Očekávané chování: `EscalationDepth` counter na korelaci; při překročení `Rag:Review:MaxEscalationDepth` → hard-stop do nejvyššího stupně / abandon s alertem, NE rekurze. · Mechanismus: depth cap + WARN log + metrika `platform.hybridrag.escalation_loop_total`. · Severity: P1 · Test: nasimulovat self-eskalaci → zastaví se na max depth.
- **EC-32-08-02 — Více signálů současně** · Trigger: cost cap i low faithfulness zároveň. · Očekávané chování: jedna review položka s VŠEMI důvody v `Signals`, ne duplikát per signál. · Mechanismus: enqueue dedup (EC-32-01-02) + agregace důvodů. · Severity: P2 · Test: dva capy → jeden item, oba důvody.
- **EC-32-08-03 — Irreversible akce bypassne gate (regrese)** · Trigger: delete kolekce s vysokým faithfulness (irelevantní signál). · Očekávané chování: irreversible class → blocking gate VŽDY, nezávisle na skóre. · Mechanismus: action-class override (UC-32-08 krok 4). · Severity: P0 · Test: delete kolekce → blocking gate i při skvělých signálech.

---

## UC-32-09 — Reviewer si zobrazí frontu (paged, filtrovaná, RLS-scoped)

- **Actor / role:** Reviewer (RBAC `RagReview`). · **Precondition:** Existují review items. · **Trigger:** `GET /v1/hybridrag/review?status=&type=&assignedToMe=&page=`. · **Main flow:**
  1. Query handler (read přes `IReadDbContextFactory`) načte items filtrované RLS (tenant + případně user-scope) + RBAC (jen typy, na které má reviewer právo — UC-32-11).
  2. Filtry: `Status`, `Type`, `assignedToMe`, `priority`, řazení `Priority desc, CreatedUtc asc` (nejstarší/nejpalčivější první).
  3. Vrátí `Paged<ReviewItemSummaryDto>` s `totalCount` (concise summary: typ, otázka/kontext, návrh, confidence/signály — bez plného PII payloadu, ten až v detailu).
- **Postcondition / záruky:** Reviewer vidí jen co smí; concise summary šetří přenos PII. · **Tenancy / permissions:** RLS scope; RBAC filtruje typy; cizí tenant items neviditelné. · **Reuse / canonical pattern:** read query `GetProfileHandler.cs:12` (`IReadDbContextFactory`); paging `Paged<T>`/`totalCount` (BuildingBlocks). · **Data dotčena:** čte `hybridrag_review_items`. · **Eventy:** žádný (read). · **Priorita:** P1

### Edge cases UC-32-09
- **EC-32-09-01 — Reviewer bez práva na typ** · Trigger: reviewer s `RagReview` ale bez `RagReviewSensitive` filtruje legal items. · Očekávané chování: legal/sensitive items se ve frontě NEZOBRAZÍ (RBAC-filtered query). · Mechanismus: RBAC v query predicate (UC-32-11). · Severity: P1 · Test: ne-sensitive reviewer → fronta bez sensitive items.
- **EC-32-09-02 — Velká fronta (perf)** · Trigger: tisíce Pending items. · Očekávané chování: vždy paged + index na `(TenantId, Status, Priority, CreatedUtc)`; žádný unbounded fetch. · Mechanismus: paging + DB index. · Severity: P2 · Test: 5000 items → paged, rozumná latence.

---

## UC-32-10 — Reviewer assignment & routing (named / role-team, N-reviewer, multi-stage chain)

- **Actor / role:** Systém (router) + admin (konfigurace). · **Precondition:** Review item enqueued; policy definuje routing. · **Trigger:** `ReviewItemEnqueuedIntegrationEvent` ve Workeru. · **Main flow:**
  1. Worker router přečte z policy `ReviewerRole`/`Team` nebo named reviewer pro daný `Type`/`ActionClass`.
  2. **Assignment:** named → přiřadí konkrétnímu uživateli (`AssignedTo`); role/team → ponechá nepřiřazené, viditelné celé skupině (pull model, reserve UC-32-02).
  3. **N-reviewer threshold:** policy `NReviewersRequired > 1` → item vyžaduje N nezávislých approve, sleduje se `Approvals` count (UC-32-14).
  4. **Multi-stage chain:** policy definuje řetězec (stage 1 = team L1 → approve → stage 2 = L2 senior). Approve na stage posune item do další stage (nový reviewer set), ne hned terminál.
  5. Notifikace přiřazeným/skupině (UC-32-12).
- **Postcondition / záruky:** Item se dostane ke správnému reviewerovi/skupině; vícestupňové a více-schvalovatelské scénáře deterministicky postupují. · **Tenancy / permissions:** Routing per-tenant policy; RBAC per stage. · **Reuse / canonical pattern:** worker handler `ProvisionCreditAccountHandler.cs:13`; policy oblast 24/UC-32-06; saga pro chain stav (UC-32-04). · **Data dotčena:** `hybridrag_review_items` (AssignedTo, Stage, ApprovalsRequired), audit. · **Eventy:** `ReviewItemAssignedIntegrationEvent`. · **Priorita:** P2

### Edge cases UC-32-10
- **EC-32-10-01 — Named reviewer neexistuje / deaktivován** · Trigger: policy odkazuje usera, který byl smazán/erased. · Očekávané chování: fallback na role/team routing + WARN; item nezůstane sirotek. · Mechanismus: routing fallback. · Severity: P1 · Test: named neexistuje → spadne na team, item viditelný.
- **EC-32-10-02 — Multi-stage: L1 approve, L2 deny** · Trigger: dvoustupňový chain, druhý stupeň zamítne. · Očekávané chování: finální rozhodnutí = deny (poslední stage rozhoduje); effekt se neprovede; audit nese obě rozhodnutí. · Mechanismus: chain stav v saze (UC-32-04). · Severity: P1 · Test: L1 approve → L2 deny → effekt NE, audit obě.

---

## UC-32-11 — RBAC: kdo smí approvovat CO (gated sensitive kategorie)

- **Actor / role:** Reviewer / approver s konkrétními permissions. · **Precondition:** Permissions nasazeny (`PlatformPermissions.Rag*`). · **Trigger:** Reviewer volá reserve/decide na item daného `Type`/kategorie. · **Main flow:**
  1. Permissions: `RagReview` (běžné items), `RagReviewSensitive` (legal/medical/PII-heavy), `RagApproveIrreversible` (delete/erasure/export gate), `RagAdmin` (policy). Dotted: `rag.review`, `rag.review.sensitive`, `rag.approve.irreversible`, `rag.admin`.
  2. Endpoint gated `.RequirePermission(PlatformPermissions.RagReview)` (a vyšší pro citlivé) — preferovaně permission, ne role.
  3. Handler navíc ověří kategorii item vs permission reviewera (defence-in-depth) — reviewer bez `RagReviewSensitive` nemůže decide legal item ani když získá Id.
  4. Permissions auto-seedované z `PlatformPermissions` + grantnuté system `admin` roli (CLAUDE.md authorization).
- **Postcondition / záruky:** Citlivé kategorie smí schválit jen oprávněná role; pokus jiného = 403. · **Tenancy / permissions:** Permission claim z tokenu (snapshot, refresh on re-auth); nikdy DB hit per request. · **Reuse / canonical pattern:** `.RequirePermission(...)` + `PlatformPermissions` const (CLAUDE.md §4 authorization); Zákon 10 identity z tokenu. · **Data dotčena:** žádná nová; čte claims. · **Eventy:** žádný. · **Priorita:** P0

### Edge cases UC-32-11
- **EC-32-11-01 — Neoprávněný approver (403)** · Trigger: reviewer s `RagReview` ale bez `RagReviewSensitive` decide legal item. · Očekávané chování: 403 `forbidden` (ForbiddenException) — endpoint i handler guard. · Mechanismus: RBAC permission gate + handler defence-in-depth. · Severity: P0 · Test: ne-sensitive reviewer decide legal → 403.
- **EC-32-11-02 — Eskalace permission po re-auth** · Trigger: adminem nově grantnutý `RagReviewSensitive`, ale token nese starý snapshot. · Očekávané chování: nová permission platí až po re-auth (claims snapshot) — reviewer musí re-login; do té doby 403. · Mechanismus: claims snapshot refreshed on re-auth (CLAUDE.md). · Severity: P2 · Test: grant → starý token stále 403 → re-login → 200.
- **EC-32-11-03 — Self-approval citlivé akce** · Trigger: ten kdo akci inicioval ji sám schvaluje. · Očekávané chování: policy může vyžadovat separation-of-duty (`DisallowSelfApproval`) → initiator nesmí být approver → 403 `rag.review.self_approval_forbidden`. · Mechanismus: handler guard `DecidedBy != InitiatedBy`. · Severity: P1 · Test: initiator decide vlastní gated item → 403 (když policy vyžaduje SoD).

---

## UC-32-12 — Notifikace reviewerům (in-app / email / push) s concise summary

- **Actor / role:** Systém → reviewer. · **Precondition:** Item enqueued/assigned/eskalován. · **Trigger:** `ReviewItemEnqueuedIntegrationEvent` / `ReviewItemAssignedIntegrationEvent` / `ReviewEscalatedIntegrationEvent`. · **Main flow:**
  1. Worker handler přijme event a sestaví **concise summary**: otázka/kontext, navrhované rozhodnutí, confidence/signály, deadline — **bez plného PII** (jen reference, detail za RBAC v UI).
  2. Pošle notifikaci přes existující Notifications modul (`SendNotification`, in-app feed + email/push dle preferencí) a realtime push `IRealtimePublisher.PublishToUserAsync` (oblast 21) pro live odznak ve frontě.
  3. Eskalace má vyšší prioritu / dedikovaný kanál.
- **Postcondition / záruky:** Reviewer je upozorněn; notifikace nikdy neobsahuje surové PII. · **Tenancy / permissions:** Notifikace jen oprávněným reviewerům (RBAC). · **Reuse / canonical pattern:** Notifications modul (`SendNotification` přes outbox+Worker); realtime `IRealtimePublisher` `Ports.cs:98`; worker handler `ProvisionCreditAccountHandler.cs:13`. · **Data dotčena:** in-app notifikace (Notifications modul), žádný HybridRag write kromě audit. · **Eventy:** konzumuje review eventy; produkuje `SendNotification`. · **Priorita:** P2

### Edge cases UC-32-12
- **EC-32-12-01 — PII leak v notifikaci** · Trigger: summary by zahrnul plný dotaz s e-mailem/jménem. · Očekávané chování: notifikace nese jen ne-PII souhrn + odkaz; PII detail jen v UI za RBAC + dešifrování. · Mechanismus: PII minimalizace (oblast 20); concise summary kontrakt. · Severity: P0 · Test: notifikace neobsahuje plaintext PII, jen referenci.
- **EC-32-12-02 — Notifikace doručena, item už rozhodnut** · Trigger: race — notifikace dorazí po decide. · Očekávané chování: UI při otevření zjistí `Decided` → zobrazí „již vyřízeno", reserve vrátí 409. · Mechanismus: stav-machine (UC-32-02/03). · Severity: P2 · Test: notifikace po decide → reserve 409.

---

## UC-32-13 — Durable resumable handoff (lidské rozhodnutí za hodiny → deterministický resume)

- **Actor / role:** Systém (saga/operation) + schvalovatel. · **Precondition:** Blocking gate (UC-32-04) nebo multi-stage chain běží. · **Trigger:** Rozhodnutí přijde po libovolně dlouhé pauze (hodiny/dny). · **Main flow:**
  1. Stav handoffu žije v EF-persisted saze se stabilním `Id` (GateId / OperationId), ne v paměti procesu.
  2. PendingEffect, PolicyVersion snapshot, signály a stage jsou perzistované na saga rowu.
  3. Když rozhodnutí dorazí (i po restartech, deployích, dnech), Wolverine doručí event, saga deterministicky pokračuje od uloženého stavu (idempotentní apply effektu).
  4. Operace (oblast 04) poskytuje pollovatelný status (`Pending`→`Running`→`Succeeded`/`Failed`/`Abstained`); `ReconcileStaleOperationsCommand` (oblast 04) age-uje zaseklé Pending/Running, pokud durable práce neterminalizovala.
- **Postcondition / záruky:** Žádný handoff se neztratí restartem; rozhodnutí za hodiny vede ke stejnému výsledku jako okamžité. · **Tenancy / permissions:** Saga scope = tenant; operace `IUserOwned` (RLS). · **Reuse / canonical pattern:** saga `CreditPurchaseSaga.cs:30`; 202/status + reconcile `StartDemoOperationHandler.cs:17` + oblast 04. · **Data dotčena:** saga tabulka, `operations`. · **Eventy:** review/approval eventy, operace status. · **Priorita:** P0

### Edge cases UC-32-13
- **EC-32-13-01 — Deploy uprostřed čekání na rozhodnutí** · Trigger: nová verze appky nasazena, gate stále Pending. · Očekávané chování: saga přežije (EF-persisted), po nasazení pokračuje; PolicyVersion snapshot zajistí konzistentní vyhodnocení i když se mezitím změnila policy. · Mechanismus: durable saga + versioned snapshot (UC-32-06 EC-03). · Severity: P0 · Test: enqueue gate → restart hosta → decide → resume OK.
- **EC-32-13-02 — Operace zasekne (worker nikdy neterminalizoval)** · Trigger: bug/ztráta envelope. · Očekávané chování: `ReconcileStaleOperationsCommand` po stáří ageuje na Failed + alert; nezůstane věčně Pending. · Mechanismus: reconcile job (oblast 04). · Severity: P1 · Test: simulovat ztracený effekt → reconcile → Failed.

---

## UC-32-14 — N-reviewer threshold (vícenásobné schválení, partial-approval stav)

- **Actor / role:** N reviewerů. · **Precondition:** Policy `NReviewersRequired = N > 1` pro daný `Type`. · **Trigger:** Postupné `decide(approve)` od různých reviewerů. · **Main flow:**
  1. Každý approve inkrementuje `Approvals` (distinct `DecidedBy`), zaznamenává se kdo.
  2. Item zůstává v mezistavu `PartiallyApproved` dokud `Approvals < N`.
  3. Při `Approvals == N` → finální `Decided(approve)` → effekt/event.
  4. Jakýkoli `deny` v průběhu → okamžitě finální `Decided(deny)` (jeden deny ruší, pokud policy nestanoví jinak), effekt se neprovede.
- **Postcondition / záruky:** Effekt jen po N nezávislých approve; každý hlas auditován; deny má veto. · **Tenancy / permissions:** Každý hlas RBAC-ověřen; distinct reviewer (nelze dvakrát tentýž). · **Reuse / canonical pattern:** saga stav (UC-32-04); idempotency per `(ItemId, DecidedBy)` (UC-32-03). · **Data dotčena:** `hybridrag_review_items` (Approvals, hlasy v JSONB / pod-tabulka), audit. · **Eventy:** `ReviewItemDecidedIntegrationEvent` až při terminálu. · **Priorita:** P2

### Edge cases UC-32-14
- **EC-32-14-01 — Stejný reviewer hlasuje dvakrát** · Trigger: reviewer pošle approve 2×. · Očekávané chování: distinct guard UNIQUE `(ItemId, DecidedBy)` → druhý hlas no-op, `Approvals` se nezvýší. · Mechanismus: idempotency UNIQUE key. · Severity: P1 · Test: 2× approve týž reviewer → Approvals=1.
- **EC-32-14-02 — N approve a 1 deny souběžně (race)** · Trigger: poslední approve a deny současně. · Očekávané chování: deterministická serializace přes xmin; deny veto vyhrává nebo first-writer dle policy; finální stav jednoznačný, ne oba. · Mechanismus: xmin + `ConcurrencyRetryBehavior`. · Severity: P1 · Test: concurrent approve+deny → jeden terminál.
- **EC-32-14-03 — Partial approval a item expiruje** · Trigger: `Approvals = N-1`, timeout. · Očekávané chování: eskalace (UC-32-08) nebo abandon dle policy; partial schválení se NEpovažuje za plné. · Mechanismus: saga timeout. · Severity: P2 · Test: N-1 approve → timeout → eskalace, ne approve.

---

## UC-32-15 — Reviewer EDIT-to-correct → oprava vstupu/výstupu → kandidát do golden setu

- **Actor / role:** Reviewer (RBAC `RagReview`). · **Precondition:** Item s opravitelným payloadem (answer_approval, feedback_label, entity_merge). · **Trigger:** `decide` s `decision=edit` + `correctedPayload`. · **Main flow:**
  1. Reviewer opraví navrženou odpověď / správný entity-merge / správný label.
  2. Handler uloží `CorrectedPayload`, `Decision=edit`, publikuje `ReviewItemDecidedIntegrationEvent` s opraveným obsahem.
  3. Pokud `Type ∈ {answer_approval, feedback_label}` a policy `HarvestEditsToGolden=true` → handler **navrhne** opravený pár jako kandidát do golden datasetu (oblast 31) — `dispatcher.Send(AddGoldenCandidateCommand)` (NE přímý zápis do golden, jde to přes review/harvest oblasti 31, aby chybný edit neotrávil golden bez druhého schválení — viz EC).
  4. U `entity_merge` se opravený merge (správné cílové entity) aplikuje v oblasti 11.
- **Postcondition / záruky:** Oprava je auditovaná; golden harvest je gated, ne automatický. · **Tenancy / permissions:** Scope item; RBAC kategorie. · **Reuse / canonical pattern:** feature chaining `dispatcher.Send(...)` (CLAUDE.md §5); golden oblast 31; entity-res oblast 11. · **Data dotčena:** `hybridrag_review_items`, golden candidate (oblast 31), audit. · **Eventy:** `ReviewItemDecidedIntegrationEvent`, případně `GoldenCandidateProposedIntegrationEvent`. · **Priorita:** P1

### Edge cases UC-32-15
- **EC-32-15-01 — Reviewer edit zanese chybu do golden setu** · Trigger: reviewer opraví špatně → chybný „gold". · Očekávané chování: edit nejde přímo do golden — vytvoří jen **kandidáta** vyžadujícího druhé schválení (oblast 31 harvest review), případně N-reviewer; golden zůstane čistý dokud není kandidát potvrzen. · Mechanismus: gated harvest (oblast 31) + N-reviewer (UC-32-14). · Severity: P0 · Test: edit → golden NEobsahuje pár dokud druhý reviewer nepotvrdí.
- **EC-32-15-02 — Edit payload obsahuje nový PII** · Trigger: reviewer vepíše PII do opravené odpovědi. · Očekávané chování: `correctedPayload` PII pole `[Encrypted]`+`[PersonalData]`; uloženo šifrovaně; GDPR-shred dosažitelné. · Mechanismus: PII at-rest (oblast 20). · Severity: P1 · Test: edit s PII → ciphertext v DB.
- **EC-32-15-03 — Edit na již rozhodnutém item** · Trigger: edit po terminálu. · Očekávané chování: 409 `rag.review.already_decided` (idempotentní). · Mechanismus: stav-machine guard. · Severity: P2 · Test: edit po Decided → 409.

---

## UC-32-16 — Feedback capture endpoint (thumbs / rating / flag) → routing do review / golden harvest

- **Actor / role:** Koncový uživatel (autor dotazu). · **Precondition:** Existuje `RagConversation/Turn` ownerovaný uživatelem; query Id z odpovědi. · **Trigger:** `POST /v1/hybridrag/query/{id}/feedback` `{ verdict: thumbs_up|thumbs_down|rating, rating?, flagReason?, comment? }`. · **Main flow:**
  1. Handler ověří, že `query/{id}` patří `ITenantContext.UserId` (RLS + explicit guard) — cizí query → 404 (IDOR ochrana).
  2. Vloží `RagFeedback` (`IUserOwned`+`ITenantScoped`, UNIQUE `(QueryId, UserId)` — jeden feedback per query per user) — idempotentní upsert (změna verdiktu přepíše, ne duplikuje).
  3. Routing dle policy: `thumbs_down`/`flag` s nízkým skóre → enqueue `RagReviewItem` (`Type=feedback_label`, UC-32-01) k posouzení; `thumbs_up` na high-faithfulness odpověď → kandidát do golden harvest (oblast 31, gated).
  4. `SaveChangesAndFlushMessagesAsync` → `FeedbackSubmittedIntegrationEvent`.
- **Postcondition / záruky:** Jeden feedback per (query, user); špatné odpovědi se dostanou na review, dobré jako golden kandidáti; identita z tokenu. · **Tenancy / permissions:** `IUserOwned` → RLS; owner z tokenu (Zákon 10). · **Reuse / canonical pattern:** vertical slice + outbox `RegisterUserHandler.cs:22`; idempotency UNIQUE key; golden oblast 31; review enqueue UC-32-01. · **Data dotčena:** `hybridrag_feedback`, `hybridrag_review_items`, audit. · **Eventy:** `FeedbackSubmittedIntegrationEvent`. · **Priorita:** P1

### Edge cases UC-32-16
- **EC-32-16-01 — Feedback na cizí query (IDOR)** · Trigger: uživatel A pošle feedback na query uživatele B (uhodnuté Id). · Očekávané chování: RLS na `IUserOwned` query → 404; explicitní owner guard navíc. · Mechanismus: RLS (oblast 16) + Zákon 10. · Severity: P0 · Test: A → feedback na B query → 404, žádný zápis.
- **EC-32-16-02 — Double thumbs (idempotence)** · Trigger: uživatel klikne thumbs_down dvakrát / změní na thumbs_up. · Očekávané chování: UNIQUE `(QueryId, UserId)` → upsert: druhý klik aktualizuje verdikt, nevytvoří druhý řádek ani duplicitní review item. · Mechanismus: idempotency UNIQUE key + catch `DbUpdateException`. · Severity: P1 · Test: 2× feedback → jeden řádek, poslední verdikt.
- **EC-32-16-03 — Feedback comment obsahuje PII** · Trigger: uživatel napíše do komentáře osobní údaj. · Očekávané chování: comment pole `[PersonalData]`+`[Encrypted]`; review summary ho neukáže v plaintextu (UC-32-12); GDPR erasure shredne. · Mechanismus: PII at-rest + crypto-shred (oblast 20). · Severity: P1 · Test: feedback s PII → ciphertext, po erasure `[erased]`.
- **EC-32-16-04 — Feedback na neexistující / smazaný query** · Trigger: query Id neexistuje nebo byl GDPR-erased. · Očekávané chování: 404 `rag.query.not_found`; žádný orphan feedback. · Mechanismus: existence guard. · Severity: P2 · Test: feedback na neexistující Id → 404.

---

## UC-32-17 — One-click „add corrected answer → golden dataset" (feedback loop do oblasti 31)

- **Actor / role:** Reviewer / oprávněný operátor (RBAC `RagReview`). · **Precondition:** Existuje rozhodnutý/editovaný review item nebo dobrá feednutá odpověď. · **Trigger:** `POST /v1/hybridrag/review/{id}/promote-to-golden` (one-click). · **Main flow:**
  1. Handler vezme (otázka, opravená/schválená odpověď, citace, kontext) z item/turnu.
  2. `dispatcher.Send(AddGoldenCandidateCommand)` → oblast 31 vytvoří golden kandidáta (NE rovnou aktivní gold — jde přes harvest review oblasti 31, idempotentní per (query, normalized-answer)).
  3. Audit zaznamená kdo promote inicioval.
- **Postcondition / záruky:** Korigované páry plynule obohacují eval golden set (oblast 18/31), ale gated proti otravě. · **Tenancy / permissions:** RBAC; scope tenant. · **Reuse / canonical pattern:** feature chaining `dispatcher.Send(...)`; golden harvest oblast 31; idempotency UNIQUE key. · **Data dotčena:** golden candidate (oblast 31), audit. · **Eventy:** `GoldenCandidateProposedIntegrationEvent`. · **Priorita:** P2

### Edge cases UC-32-17
- **EC-32-17-01 — Promote stejného páru dvakrát** · Trigger: dvojklik / re-promote. · Očekávané chování: idempotentní — UNIQUE na golden kandidát (query+normalized answer) → druhý no-op. · Mechanismus: idempotency UNIQUE key. · Severity: P2 · Test: 2× promote → jeden kandidát.
- **EC-32-17-02 — Promote nízko-kvalitní odpovědi** · Trigger: operátor promotne deny-nutou/abstain odpověď. · Očekávané chování: guard `Decision ∈ {approve, edit}` — nelze promovat denied/abstained obsah do golden. · Mechanismus: handler guard. · Severity: P1 · Test: promote denied item → 409 `rag.review.not_promotable`.

---

## UC-32-18 — Audit & observabilita celé HITL smyčky (kdo/co/proč/kdy + metriky)

- **Actor / role:** Systém + auditor (admin forensic read). · **Precondition:** Probíhají review/approval/feedback akce. · **Trigger:** Každý enqueue / reserve / decide / edit / escalate / auto-route / feedback. · **Main flow:**
  1. Každá mutace `hybridrag_review_items`/`hybridrag_feedback` přes tracked entitu → `AuditInterceptor` zapíše změněná pole do `hybridrag_audit_entries` (kdo `DecidedBy`/`ReservedBy`, co, proč `DecisionReason`/`EscalationReason`, kdy `IClock.UtcNow`).
  2. Auto-route a auto-serve rozhodnutí (band high → auto-serve) se rovněž logují jako rozhodnutí systému (důvod = skóre + policy version).
  3. Metriky off `PlatformMetrics.Meter`: `platform.hybridrag.review_enqueued_total{type}`, `review_decided_total{decision}`, `abstain_total`, `escalation_total{signal}`, `approval_latency_seconds`, `review_queue_depth`.
  4. OTel GenAI spany (oblast 19) propojí review s odpovídajícím dotazem/agentní smyčkou (CorrelationId).
- **Postcondition / záruky:** Plná auditní stopa každého lidského i automatického rozhodnutí; metriky pro alerting (rostoucí fronta, abstain rate, eskalace). · **Tenancy / permissions:** Audit per-modul; PII v auditu šifrovaná (oblast 20), forensic read RBAC `AuditRead`. · **Reuse / canonical pattern:** `AuditInterceptor` → `hybridrag_audit_entries` (oblast 25); metriky `PlatformMetrics.Meter` `PlatformMetrics.cs:19`; OTel oblast 19. · **Data dotčena:** `hybridrag_audit_entries`, metriky. · **Eventy:** žádný nový. · **Priorita:** P1

### Edge cases UC-32-18
- **EC-32-18-01 — Auto-route obejde audit (ExecuteUpdate)** · Trigger: reservation/sampling přes `ExecuteUpdate` (bypassuje interceptor). · Očekávané chování: rezervace je provozní stav (neauditovaná), ale FINÁLNÍ rozhodnutí jde vždy přes tracked entitu → auditováno; auto-serve rozhodnutí se loguje explicitně samostatným audit-zápisem/metrikou. · Mechanismus: vědomý carve-out (CLAUDE.md `ExecuteUpdate` bypass caveat) + explicit audit pro auto rozhodnutí. · Severity: P1 · Test: auto-serve → audit/metrika existuje i bez tracked mutace.
- **EC-32-18-02 — Rostoucí fronta / stuck reviews (alert)** · Trigger: review items se hromadí (žádní revieweři). · Očekávané chování: `review_queue_depth` gauge + WARN nad prahem (`Rag:Review:QueueDepthAlert`) → infrastruktura alertuje. · Mechanismus: metrika + threshold (jako `MessagingHealthJob` vzor). · Severity: P2 · Test: napumpovat frontu → gauge roste, WARN nad prahem.
- **EC-32-18-03 — Audit PII forensic read po erasure** · Trigger: auditor čte staré rozhodnutí subjektu, který požádal o erasure. · Očekávané chování: PII pole `[erased]` (DEK shrednut), ne-PII audit (kdo/kdy/decision) zůstává. · Mechanismus: crypto-shred (oblast 20). · Severity: P1 · Test: erasure → forensic read → PII `[erased]`, rozhodnutí čitelné.
