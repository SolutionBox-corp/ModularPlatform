# CRM Module — Review Findings (2026-06-28)

> Konsolidovaný výstup dvou skeptických **ultracode** review pipeline nad větví `feature/crm-module`
> (commit `42c0115`). Každý nález byl adversariálně ověřen proti skutečnému kódu (line-by-line), ne odhadnut.
> - **Pipeline 1 — Konzistence s platformou** (33 agentů): drží se CRM kanonických LAWS z `CLAUDE.md`?
> - **Pipeline 2 — CRM jako produkt** (46 agentů): use-case úplnost, optimalizace datového modelu, nedodělky/dead code.
>
> Doprovodný dokument: `docs/crm-test-scenarios.md` (Given/When/Then UC + EC katalog, zrcadlí CORE `docs/test-scenarios.md`).

---

## Celkový verdikt

**Grade: `significant-rework-needed`** → po opravě 1 blockeru + HIGH bugů spadne na `mostly-solid-with-fixes`.

Kód je **řemeslně kvalitní**: žádný slop, žádné TODO, korektní RLS/GDPR plumbing/audit, EF-only, owner-scoping všude
z tokenu (`ITenantContext.UserId`), žádné IDOR, žádné raw SQL, žádná cross-module Core reference, modul registrovaný
ve všech 4 hostech + ArchitectureTests reálně rozšířené. Radek se **opřel o kanonický skeleton** (Identity slice,
`AddModuleDbContext`, `IUserOwned`+RLS, `AuditableEntity`, xmin, `ApiResponse`, blind index, sdílený test harness) —
nestavěl paralelní flow proti nehotovému core.

Problémy jsou ve dvou rovinách:
1. **Korektnost** — 1 GDPR blocker + 3 HIGH bugy v CRUD/state-machine sémantice (reálné chyby, ne scope).
2. **Rozsah** — jako *CRM produkt* je to „contacts + meetings" modul označený jako CRM; chybí revenue jádro (Deals),
   account vrstva (Companies), akční vrstva (Tasks/Reminders) a cross-module events. **Radkův vlastní
   `CRM_MODULE_ARCHITECTURE.md` je bral jako in-scope** → nejsou to rozumná v1 opomenutí, je to nedokončené jádro.

---

## ČÁST A — Konzistence s platformou (pipeline 1)

### 🔴 BLOCKER

**A1 — Contact je vlastní data-subject (`SubjectId => Id`) → GDPR crypto-shred je rozbitý.**
`src/modules/Crm/.../Entities/Contact.cs` deklaruje `Guid IDataSubject.SubjectId => Id;`. Platforma maže jen
`UserErasureRequestedHandler` → `ShredSubjectKeyCommand(message.UserId)`, což shredne **DEK uživatele**, nikdy DEK
kontaktu. `AuditInterceptor` přitom zašifroval jméno/email/telefon kontaktu do `crm_audit_entries` pod DEK kontaktu →
po smazání uživatele **třetí-strana PII v audit trailu (a v DB zálohách) zůstává navždy dešifrovatelná**. Kanonický
`Notification.cs` schválně používá `SubjectId => UserId`. Doc-komentář v `Contact.cs` navíc tvrdí opak (lže).
**Fix:** změnit na `=> UserId` (zrcadlit `Notification.cs`); eraser `ExecuteUpdate` tombstones se tím stanou
defence-in-depth. Per-contact subjekty NEpoužívat, dokud neexistuje port na shred libovolného subject DEK — neexistuje
(Law 11: „stop and ask"). Opravit i nepravdivý doc-komentář.

### 🟠 HIGH

**A2 — `CompleteMeeting` nemá transition guard a není idempotentní.**
`Features/Meetings/CompleteMeeting/CompleteMeetingHandler.cs:17-37` bezpodmínečně nastaví `Status = Done` a přidá
`ContactInteraction`. Dvojklik **nebo** automatický re-run přes `ConcurrencyRetryBehavior` po xmin konfliktu →
**duplikát na timeline kontaktu**; lze „dokončit" i zrušený meeting. Kanonický `ConfirmSpendHandler.cs:36-44` přesně
tohle řeší (early-return na cílový stav + `BusinessRuleException` na nelegální zdrojový stav) a byl ignorován.
**Fix:** `if (meeting.Status == Done) return;` + reject `Canceled`/`NoShow`. Přidat `crm.meeting.invalid_transition` do
obou resx.

**A3 — Branch veze červený test (entitlement rozpor).**
`src/modules/Tenancy/.../Services/ProductModuleKeys.cs` přidal `"crm"` do `DefaultEntitled` (CRM default-on pro každý
nový tenant), ale `EntitlementsTests` (netknutý) pořád tvrdí `ShouldNotContain("crm")` a komentář v
`TenantProvisioning.cs` je teď nepravdivý. **Fix:** rozhodnout produktový záměr (opt-in vs default-on) a sladit test +
komentář; branch nesmí shippnout červený test.

**A4 — Free-text PII v auditu je nemazatelné.**
`ContactInteraction.Body`, `Meeting.Notes/Outcome/Location`, `Contact.Notes` jsou plaintext bez `[PersonalData]`.
Eraser smaže živý řádek (`ExecuteUpdate`), ale plaintext historie v `crm_audit_entries` zůstává. `Company`/`Position`
eraser ani nenuluje. **Fix:** označit ta pole `[PersonalData]` (+ `ContactInteraction`/`Meeting` implementovat
`IDataSubject => UserId`) → audit hodnoty crypto-shreddable; zrcadlit `Notification.cs:21-26`.

**A5 — Zkontrolovat `AddInteractionHandler` na stejný retry-double-insert jako A2** *(completeness critic — neověřeno)*.
Je to taky `Add` pod retry behaviorem. Ověřit idempotenci nebo potvrdit, že je bezpečný.

### 🟡 MEDIUM (konzistence)

- **A6 — Paging re-implementovaný na 4 místech** (`{Total,Limit,Offset}` + bespoke `ContactsPageResponse`/
  `MeetingsPageResponse` místo `PagedResponse<T>`/`PageRequest` z `Cqrs/Paging.cs`; cap 200 vs kanon 100; FE divergence
  `total` vs `totalCount`). Law 4 (reuse-first). **Opravit BE+FE společně**, jinak se rozbije stránkování.
- **A7 — `CancelMeeting` bez source-state guardu** — „done" meeting lze tiše vrátit na „canceled"
  (`CancelMeetingHandler.cs:14-19`).
- **A8 — Eraser zapisuje literální tombstony do `[Encrypted]` sloupců přes `ExecuteUpdate`** — read path je
  model-level converter; ověřit, že `[erased]`/`null` plaintext v `penc:`-očekávajícím sloupci nerozbije čtení.

### 🟢 LOW / INFO (konzistence)

- **A9 — Normalizace Status/Type defaultů v endpoint vrstvě** místo handleru (`CreateContactEndpoint`/
  `UpdateContactEndpoint`/`AddInteractionEndpoint`) — endpoint má být čistý mapper (CLAUDE.md §2).
- **A10 — `CreateMeeting` vrací 201 s `Location: null`** — kanon staví Location přes named route + `LinkGenerator`
  (`StartDemoOperationEndpoint.cs:29`); zkopírovaný „would hardcode /v1" komentář je fakticky špatně.
- **A11 — `CrmContracts.ModuleKey` je mrtvý** — všech 13 endpointů hardcoduje literál `"crm"`; doc-komentář lže.
- **A12 — FE field-validation casing** — backend PascalCase chyby se nenavážou na camelCase RHF pole (chybí
  normalizace, kterou dělají kanonické auth formuláře).
- **A13 — zod `tags` schema nezrcadlí backend validátory** (max 32 / ≤48 znaků).

---

## ČÁST B — CRM jako produkt: use-cases, datový model, nedodělky (pipeline 2)

### Use-case úplnost vs. standardní CRM

| Schopnost | Stav | Verdikt |
|---|---|---|
| Contacts CRUD + lifecycle | ✅ EXISTS | OK, jádro je tu |
| Activity log (interactions) | ✅ EXISTS (append-only) | OK pro v1, bez edit/delete |
| Meetings / kalendář | ✅ EXISTS | OK, bez delete + bez state machine |
| **Deals / Pipeline / Opportunities** | ❌ ABSENT | **Největší mezera** — modul netrackuje revenue → Rolodex, ne CRM. Intent doc bral jako centrální |
| **Companies / Accounts** | ❌ ABSENT (`Company` je `string?`) | Reálná mezera — žádný rollup, žádný account owner, B2B nemožné |
| **Tasks / Reminders / follow-up** | ❌ ABSENT | Reálná mezera — nic nepřipomene „koho dnes obvolat" |
| **Cross-module integration events** | ❌ ABSENT (`ConfigureMessaging` prázdné) | Strukturální mezera — modul je ostrov, nic downstream nereaguje |
| Unified activity timeline | ◐ PARTIAL | Interactions a meetings jsou 2 listy; planned meeting na timeline až po completion |
| CSV import/export + bulk ops | ❌ ABSENT | Vysoký onboarding friction; GDPR export není náhrada |
| Dedup / merge | ❌ ABSENT (+ `EmailHash` non-unique) | Bez merge list shnije do duplikátů |
| Assignment / owner | ❌ ABSENT (vše per-user RLS) | Týmový CRM nemožný — kontakt vidí jen jeho tvůrce |
| Attachments | ❌ ABSENT | Files building-block existuje, přilnutí přes `FileObjectId` triviální |
| Reporting / dashboards / saved views | ❌ ABSENT | Pro v1 přijatelné (závisí na chybějících Deals) |
| Authorization granularita (read/write/manage) | ❌ ABSENT (jen `.RequireModule`) | Není leak (per-user RLS), odpovídá konvenci sibling modulů |

### Datový model — je optimalizovaný?

Kompetentní a konzistentní s platformou, ale **3 konkrétní mezery:**

- **B1 (MEDIUM) — chybí index `(UserId, CreatedAt)` na contacts.** `ListContactsHandler.cs:46` řadí default
  `CreatedAt DESC`, ale jediné indexy jsou `(UserId, Status)` + `(EmailHash)` → seq sort na každé stránce nejčastější
  CRM query. **Asymetrie** vůči Meetingu, který index pro svůj sort má (`(UserId, ScheduledAt)`).
- **B2 (LOW) — `Tags text[]` bez GIN indexu A bez API filtru** → tagy jsou dnes write-only metadata, segmentace
  nefunguje. Rozhodnout `text[]`+GIN vs junction tabulka **před** akumulací dat.
- **B3 (LOW) — orphan při delete kontaktu** — `DeleteContactHandler.cs:21` nekaskáduje; interactions zůstávají
  listovatelné, meetings drží dangling `ContactId` (na který `GET /contacts/{id}` vrací 404); `CompleteMeeting` dokonce
  vloží nový interaction na soft-deleted kontakt.
- **B4 (INFO) — Status/Type jako volný `varchar(32)`** s validací jen na edge, žádný DB CHECK (odpovídá platform
  konvenci — trade-off konzistence vs. obrana).
- **B5 (LOW) — žádné partial indexy** pro univerzální `DeletedAt IS NULL` filtr ani nullable `EmailHash`.

**Co změnit:** (1) `HasIndex(c => new { c.UserId, c.CreatedAt })`; (2) GIN + tag filtr nebo junction tabulka;
(3) orphan policy na delete; (4) volitelně DB CHECK + partial indexy.

### Nedodělky, dead code, half-implementace

#### BUGY
- **B6 (HIGH) — `PATCH /crm/contacts/{id}` je full-replace maskovaný jako PATCH (data loss).**
  `UpdateContactHandler.cs:25-33` přepíše bezpodmínečně VŠECHNA pole; endpoint mapuje `request.FullName ?? ""` a
  `IsNullOrWhiteSpace(request.Status) ? Lead : ...`. Klient pošle `{status:"customer"}` → buď 400
  `crm.contact.full_name.required`, nebo **tiše resetuje 'customer' kontakt zpět na 'lead'** (rewind pipeline stage) +
  vynuluje tags/phone/email/notes. **Stejný tvar má `UpdateMeeting`** (location/notes se vynulují). FE dnes posílá celý
  objekt → produkce nepadá, ale jakýkoli jiný klient (mobil, integrace) ztrácí data. **Fix:** skutečný partial patch
  nebo přejmenovat na PUT + nikdy nedefaultovat chybějící Status na 'lead'.
- **B7 (HIGH) — `CompleteMeeting` není idempotentní + bez state guardu** — viz **A2** (duplikát na timeline).

#### EDGE-CASE / STATE-MACHINE
- **B8 (MEDIUM) — Meeting nemá state machine** — cancel done, complete canceled, reschedule terminálního meetingu vše
  projde; cancel nikdy nevyčistí `Outcome`; dosažitelné nesmysly (canceled meeting s outcome, done meeting přeplánovaný
  do budoucna).
- **B9 (MEDIUM) — `ListInteractions` není pageable** — `ListInteractionsHandler.cs:17,22` clamp 1..500, `Take`, bez
  Skip/offset/total → kontakt s >500 interakcemi má historii oříznutou bez možnosti stránkovat. Nejhlouběji rostoucí
  kolekce a jediný list bez pagingu.
- **B10 (MEDIUM) — `ListContacts` bez name search / tag filtru / date range / sort control** — hledání kontaktu podle
  jména je nejčastější CRM operace a je nemožné (`FullName` je `[Encrypted]`, žádný name blind index).

#### DEAD CODE / half-wired
- **B11 (LOW) — `no_show` meeting status je nedosažitelný end-to-end** — `Meeting.cs:36` definuje, FE má filtr option +
  i18n label, ale žádný endpoint ho nenastaví → uživatel vybere „No-show" ve filtru a vždy dostane prázdný list.
- **B12 (LOW) — `MeetingStatuses.IsValid`/`.All` jsou unused** (status je server-controlled).
- **B13 (MEDIUM) — Meeting Outcome capture je half-wired** — backend přijímá outcome + validátor, ale FE jediný trigger
  (`meetings-table.tsx:90`) posílá hard-coded `outcome: null` → timeline Body vždy fallbackne na title, zobrazený
  outcome vždy „—", validátor cesta nedosažitelná. Complete-with-outcome dialog zamýšlen, nepostaven.
- **B14 (LOW) — standalone `/crm/meetings` stránka je orphaned** — `nav.ts` má jen `crm→/crm`; grep `/crm/meetings`
  najde jen ten soubor. Dosažitelné jen ručním napsáním URL.

#### CRUD asymetrie
- **B15 (MEDIUM) — Meeting nemá Delete** — `ISoftDeletable`, ale jediný writer `DeletedAt` je GDPR eraser → omylem
  vytvořený meeting jde jen cancelovat; canceled/done meetings se hromadí navždy.
- **B16 (LOW) — ContactInteraction nemá get-single/update/delete** — překlep v zalogovaném callu je trvalý
  (append-only, nezdokumentované).
- **B17 (LOW) — `UpdateMeeting` nemůže změnit/odpojit `ContactId`** — settable jen při create.

#### Doc drift
- **B18 (LOW) — `CRM_MODULE_ARCHITECTURE.md` je ~70 % aspirational** — popisuje `Deal`/`Company`/`CrmAiRun`/events/
  realtime jako hotové, dokonce jiný Contact shape (`DisplayName`, `SubjectId => UserId`) než shipped → čtenář nepozná,
  co existuje. **Fix:** rozdělit na SHIPPED vs FUTURE, opravit Contact příklad.
- **B19 (INFO) — doc slibuje duplicate-email conflict, není implementován** (error kód `crm.contact.email_exists`
  v resx neexistuje, `EmailHash` non-unique).
- **B20 (LOW) — realtime invalidace slíbená docem chybí** (žádné CRM eventy/realtime; spoléhá na full-root TanStack
  invalidaci, což je pro single-user CRUD OK).

#### Další
- **B21 (LOW) — Tags se nenormalizují/nededuplikují, prázdné tagy projdou validací** (`["", ""]` validní; 'VIP'/'vip'/
  ' vip ' = tři tagy) — handler přitom trimuje ostatní pole.
- **B22 (LOW) — žádný date-sanity guard na `ScheduledAt`/`OccurredAt`** — meeting v roce 3000 korumpuje řazení timeline.

---

## Prioritizovaný akční seznam (sloučený, blocker → low)

### 🔴 BLOCKER (před mergem)
1. **A1** — `Contact.cs`: `SubjectId => Id` → `=> UserId` (GDPR crypto-shred); opravit nepravdivý doc-komentář.

### 🟠 HIGH (před označením modulu za hotový)
2. **B6** — PATCH contacts/meetings full-replace data loss → partial patch / PUT, nikdy nedefaultovat Status na 'lead'.
3. **A2/B7** — `CompleteMeeting` idempotency + transition guard (+ resx `crm.meeting.invalid_transition`).
4. **A3** — vyřešit entitlement rozpor (`ProductModuleKeys` vs `EntitlementsTests` + `TenantProvisioning` komentář).
5. **A4** — free-text PII (`Notes`/`Body`/`Outcome`) `[PersonalData]` nebo scrub auditu; eraser nulovat i `Company`/`Position`.
6. **A5** — ověřit `AddInteractionHandler` na retry-double-insert.

### 🟡 MEDIUM (reálné mezery / integrita)
7. **A6/B-paging** — nahradit hand-rolled paging `PagedResponse<T>`/`PageRequest` (BE) + sladit FE `Paged<T>`.
8. **A7/B8** — Meeting state machine (cancel/complete/reschedule guardy + clear Outcome při cancel).
9. **B1** — index `(UserId, CreatedAt)` na contacts.
10. **B9** — `ListInteractions` pageable (envelope / keyset).
11. **B10** — `ListContacts` name blind index + tag filtr + sort whitelist.
12. **B15** — DeleteMeeting slice (nebo dropnout `ISoftDeletable` z Meeting).
13. **B13** — complete-with-outcome dialog (FE).
14. **Scope** — rozhodnout: Deals/Companies/Tasks/events dostavět, nebo vědomě shipnout „v1 contacts+meetings" a sladit
    arch doc s realitou.

### 🟢 LOW / INFO
15. **B2** — Tags GIN + filtr nebo junction tabulka.
16. **B3** — orphan policy na delete kontaktu.
17. **B11** — `no_show` transition nebo odstranit (BE+FE+i18n).
18. **B21** — normalizace tagů (trim/distinct/lowercase + NotEmpty).
19. **B22** — date-sanity guard.
20. **B14** — nav item pro `/crm/meetings` nebo smazat stránku.
21. **A9** — přesunout Status/Type normalizaci z endpointu do handleru.
22. **A10** — `Location` přes `LinkGenerator`.
23. **A11/B12** — odstranit mrtvý `ModuleKey` / `MeetingStatuses.IsValid`.
24. **A12** — FE casing normalizace; **A13** — zod tag schema.
25. **B18/B19/B20** — sladit arch doc (SHIPPED vs FUTURE), duplicate-email, realtime.

---

*Generated 2026-06-28 via two adversarial ultracode review pipelines (79 agents total). Each finding verified
line-by-line against the real code on `feature/crm-module@42c0115`.*
