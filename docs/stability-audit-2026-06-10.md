# ModularPlatform — Stability Audit (2026-06-10)

> Cíl (`/goal`): dostat platformu do **opravdu stabilní base** — architektonicky přesné, kód čistý,
> aplikace robustní, edge cases vyladěné — než se nad ní začne stavět produkt. Tento dokument je
> výchozí backlog iterací. Metoda: multi-agentní audit (8 inventur + 12 adversariálních dimenzí,
> každý nález nad `low` nezávisle ověřen skeptikem). `confirmed` = skeptik potvrdil v kódu s file:line.
> `unverified` = nález vznikl, ale verifikační agent spadl na session-limit (z 12 dimenzí doběhla
> verifikace u money/auth/pii-gdpr/realtime/files; messaging/tenancy/ops-jobs/errors/test-quality/clean-arch
> mají nálezy recovered z transkriptů, část je ale potvrzená "twinem" z jiné, ověřené dimenze).

## Progress log

- **2026-06-10 — Wave 1 zahájena (TDD: RED → GREEN → regrese).** Celý suite po každém fixu zelený.
  - ✅ **#1 CRITICAL — free credit minting.** `POST /billing/credits/topup` gated `.RequirePermission(billing.manage)`
    (jako package-admin endpointy); command zůstává interní pro saga/subscription/webhook. 8 ledger testů, co
    seedovaly balance přes ten backdoor, přepojeno na nový DRY seam `fixture.GrantCreditsAsync(userId, …)`
    (in-process dispatch interního `CreditTopUpCommand` — co dělá saga po platbě). Nové testy:
    `CreditTopUpAuthorizationTests` (non-admin → 403; admin → 200 + balance).
  - ✅ **#10 HIGH — plaintext email v `tenants.Name`.** Nahrazeno neutrálním `tenant-{id:N}` → žádná PII at-rest,
    nic k mazání při erasure. Test: `TenancyTests.Registration_does_not_store_the_plaintext_email_in_the_tenant_name`.
  - ✅ **#2 HIGH — erased/soft-deleted user session.** `RefreshTokenHandler` odmítne `DeletedAt != null`;
    `IdentityPersonalDataEraser` revokuje všechny refresh tokeny subjektu. Testy: `SessionRevocationTests`
    (soft-delete→refresh 401; erasure→tokeny revokované+refresh 401).
  - ✅ **#3 HIGH — chybějící logout.** Nový slice `Features/Auth/Logout` (revokuje rodinu presented tokenu,
    id z tokenu, idempotentní non-enumerating). Test: `Logout_revokes_the_session_family`.
  - ✅ **#5 HIGH — Stripe grant bez `PaymentStatus`.** Grant jen na `paid`/`no_payment_required`;
    `async_payment_succeeded` routováno; ostatní `checkout.session.*` nepropadnou do generického top-up.
    Testy: `Unpaid_checkout_session_does_not_grant_credits`, `Async_payment_succeeded_grants_credits_on_settlement`.
  - ✅ **HIGH — Stripe webhook swallow-all.** Catch zúžen na UNIQUE violation (23505); ostatní `DbUpdateException`
    → 500 → Stripe redeliver. Test: `A_non_unique_persist_failure_is_not_acked_so_stripe_will_retry`.
  - ✅ **#6 HIGH — idempotency global namespace.** Composite `UNIQUE(AccountId, IdempotencyKey)` + per-account
    dedup (topup/release/confirm) + migrace `ScopeIdempotencyKeyPerAccount`. Test: `Idempotency_keys_are_scoped_per_account_not_globally`.
  - ✅ **#9 HIGH — download/ops IDOR (RLS-off).** App-level `&& UserId` filtr v `GetFileHandler` +
    `GetOperationStatusHandler` (query+endpoint nesou UserId z tokenu). Testováno in-process pod SYSTEM kontextem
    (RLS bypass → prokazuje že bez filtru leakuje): `File_download_is_owner_scoped…`, `Operation_status_is_owner_scoped…`.
  - **Suite: 108 → 120/120**, build 0/0. **Wave 1 KOMPLETNÍ** (všech 8 stop-shipů).
- **2026-06-10 — Wave 2 (robustnost).** Suite 120 → **128/128**, build 0/0.
  - ✅ **#4 HIGH — expiry-sweep crash.** Bucket krytý aktivní rezervací se přeskočí (full-or-nothing cap na
    `account.Available`) → žádný negative/CHECK crash; per-account try/catch izoluje selhání jednoho účtu.
    Test: `Expiring_a_bucket_that_backs_an_active_reservation_does_not_crash_or_go_negative`.
  - ✅ **A2 HIGH — ConcurrencyRetryBehavior amplifikace.** `AddPipelineBehavior` → `TryAddEnumerable` (dedup):
    behavior tažený per-modul je registrovaný 1×, ne 6× (žádné 5⁶ retry). Test: `PipelineBehaviorRegistrationTests`.
  - ✅ **HIGH — operace stuck Running.** `RunDemoOperationHandler` try/catch → `FailAsync` (terminální stav,
    ne věčné Running). Test: `OperationWorkerFailureTests` (fake store, work throws → FailAsync).
  - ✅ **HIGH — OperationStore bez state-machine guardu.** Terminální stavy (Succeeded/Failed) jsou finální —
    duplicitní worker message nevzkřísí op. Test: `A_terminal_operation_is_not_resurrected_by_a_duplicate_worker_transition`.
  - ✅ **HIGH — MessagingHealthJob špatný counter.** Sleduje `Outgoing` (outbox backlog), ne `Scheduled`
    (saga timeouts) → stuck-outbox alert konečně fíruje, žádné false alarms. Logika vytažena do čisté
    `MessagingHealthEvaluation`. **Nový test projekt `tests/ModularPlatform.Jobs.Tests`** + `MessagingHealthEvaluationTests`.
  - ✅ **MEDIUM — throwing job tichý.** Centrální `JobFailureListener` (Quartz IJobListener, all groups) →
    ERROR log + counter `platform.jobs.failures` na každé selhání kteréhokoli jobu. Test: `JobFailureMetricsTests`.
  - ✅ **MEDIUM — Quartz clustering.** Joby jsou idempotentní → deployment note (single-instance; pro HA AdoJobStore).
    Dokumentováno v `Jobs/Program.cs` (ne kód-fix).

## ⚠️ Architektonická rozhodnutí (NEřeším unilaterálně — Law 11)

Dva nálezy nejsou jednoznačné bugy, ale architektonické volby zmrazené v CLAUDE.md / vyžadující design rozhodnutí.
**Nedělám je sám** — surface k rozhodnutí:

- **A1 — messaging bez transportu (handler běží v publisher procesu).** CLAUDE.md to popisuje jako záměr
  („durable local queues"). Buď (a) přidat PG transport + explicitní `ListenToPostgresqlQueue` (Api publikuje,
  Worker konzumuje — pravá separace), nebo (b) přijmout current model a opravit dokumentaci/topologii. **Rozhodnutí usera.**
- **retry→DLQ bez auto-requeue.** Dead-letterovaný `CreditPurchaseConfirmed` = zaplacený zákazník nedostane
  kredity. Teď je to aspoň VIDITELNÉ (MessagingHealthJob alertuje na dead-letters). Plné auto-recovery = buď
  rozšířit `ReconcileStripe` o re-grant proti Stripe stavu, nebo DLQ-replay mechanismus. **Design rozhodnutí usera.**

## Wave 3 + 4 (rozpracováno)

- **2026-06-10 — Wave 3/4 (částečně).** Suite 128 → **130/130**, build 0/0.
  - ✅ **#8 HIGH — Files bez GDPR.** `FilesPersonalDataEraser` (smaže blob přes `IFileStorage.DeleteAsync` +
    `file_objects` řádky, idempotentní) + `FilesPersonalDataExporter` (metadata), registrované ve `FilesModule`.
    Test: `Gdpr_erasure_deletes_the_users_files_and_metadata`.
  - ✅ **MEDIUM — PersonalDataProtector přes READ connection.** `GdprModule` staví protector context z `write`
    (GetOrCreateDek INSERTuje subject_keys → na read replice selže; live-DEK-read by porušila replica lag).
    *E2E-untestable bez reálné repliky (harness Read==Write) — ověřená one-line correctness oprava.*
  - ✅ **MEDIUM (Wave 4) — resx kompletnost.** Doplněno 9 chybějících kódů (operation.not_found, role.not_found,
    7× user.* validator) do en+cs. **Guard test `ErrorCodeLocalizationTests`** skenuje zdroj a vynutí, že každý
    thrown kód má resx entry v obou jazycích (proti budoucímu driftu).
  - ✅ **MEDIUM (Wave 4, A3) — ArchUnit hranice pro VŠECHNY moduly.** `No_module_core_depends_on_another_modules_core`
    (byl jen Identity + regex míjel nested ns) — teď 6 modulů, matchuje i `…\.Features\.X`. Hranice jsou čisté.
  - ✅ **MEDIUM (Wave 4) — Location string-concat.** `UploadFile` → LinkGenerator (`/v1/files/{id}`, route existuje);
    `RegisterUser` → drop Location (žádný GET by-id route, Law 10). Testy: `Upload_location_header_is_versioned…`,
    `Registration_does_not_emit_a_dangling_location_header`.
  - ✅ **MEDIUM (Wave 4) — FakeGateway env guard.** `StripeOptionsValidator` (ValidateOnStart) shodí host, když
    `UseFakeGateway` v Production (jinde exempt — bezpečné pro harness). Testy: `StripeOptionsValidatorTests` (4×).
    Odhalilo PL8 (Production host s fake gateway) → opraveno na realistický prod host. **Guard prokazatelně funguje.**
  - **Suite: 130 → 137/137**, build 0/0.

## Wave 5 (kvalita testů — rozpracováno)

- ✅ **Vacuózní assertion opravena.** `AuthRobustnessTests` ID-8: `WaitForCountAsync(…, 0)` (count >= 0 vždy true →
  neověřilo nic) nahrazeno deterministickou `ScalarAsync == 0` (po WhenAll je revoke atomicky hotový).
- ✅ **SubjectKeyShred mirror test.** Přepsáno z testu privátní kopie `ApplyShred` na dispatch REÁLNÉho
  `ShredSubjectKeyHandler` (drop DEK + stamp + idempotentní timestamp). 2 integrační testy.
- ✅ **Lockout coverage.** Doplněny netestované větve: window-expiry (correct password projde po lapsu) +
  reset-on-success (FailedAccessCount→0). `AccountLockoutTests` 1→3.
- ✅ **Files negativní testy posíleny.** Disallowed-content-type + oversized teď dokazují 0 řádků v `file_objects`
  + error code `file.content_type.not_allowed` (byl jen status).
- ✅ **Race-prone Task.Delay odstraněny.** 3× zbytečný fixní sleep ve `StripeWebhookTests` (redelivery dedup +
  bad-signature + non-unique-fail jsou synchronní) → deterministické aserce (3s→0.7s).

**Stav suite: 108 → 141/141, build 0/0.**

## Decision-gate položky vyřešené (na uživatelův mandát „dokonči to a nezastavuj")

- ✅ **retention re-mint DEK** (doporučení a). `RetentionSweepHandler` už NEmaže shredded tombstones — jsou
  permanentní re-mint guard (`PersonalDataProtector` razí nový DEK jen když řádek chybí). Test invertován:
  `Shredded_tombstone_is_retained_permanently_so_the_dek_cannot_be_re_minted`.
- ✅ **A4 pipeline symetrie.** Worker + Jobs hosty registrují `LoggingBehavior` + `ValidationBehavior` po Telemetry,
  před moduly (pořadí Telemetry→Logging→Validation→ConcurrencyRetry = jako Api). Behaviory žijí v Cqrs (public).
  *Wiring-untestable v Api-only harnessu, ale jednoznačně správné (mirror Api).*
- ✅ **retry→DLQ auto-requeue** (doporučení a, money-safe). `ReconcileStripe` pass 3: stuck Pending/Abandoned saga
  + nový gateway `GetCheckoutSessionPaymentStatusAsync` → re-publish `CreditPurchaseConfirmed` JEN když Stripe
  session `paid` (idempotence `purchase:{id}` chrání před double-grant; never grant unpaid). Testy:
  `Stuck_PAID_purchase…regranted` + `Stuck_UNPAID…NOT_regranted`.

## A1 — DOKONČENO (na mandát „dokonči to a nezastavuj")

- ✅ **A1 messaging transport.** `PlatformMessaging.Configure`: `PersistMessagesWithPostgresql` + `UseDurableLocalQueues`
  → **`UsePostgresqlPersistenceAndTransport`** (stejné `wolverine` schéma, auto-provision) + `PublishAllMessages().ToPostgresqlQueue("platform")`
  + conditional `ListenToPostgresqlQueue`. Hosty: **Worker `listen:true`** (THE consumer, out-of-process), **Api `listen:soloMode`**
  (single-node/test konzumuje sám, Balanced offloaduje na Worker), Jobs/MigrationService pure publisher (`listen:false`).
  **Ověřeno: 141/141 zelených** (in-process path: Api soloMode publikuje+listuje na queue → handluje vše).
  - ✅ **Latence zmírněna.** PG queue idle-poll byl default 5s (`ScheduledJobPollingTime`) → message lag až 5s + testy
    se vlekly. Sníženo na **1s** (`options.Durability.ScheduledJobPollingTime = 1s`): Billing suite **2m27s → 45s**,
    produkční event latence ~1s, zanedbatelná DB poll zátěž. Zbylý rozdíl (45 vs 32s) je inherentní transport round-trip.
  - ⚠️ **Harness nepokrývá multi-host routing** (Api publikuje → Worker konzumuje out-of-process) — harness je single-proces.
    Standardní Wolverine PG-transport pattern, ale před produkčním spolehnutím **ověřit ve stagingu** (reálný Api+Worker).

## ZBÝVAJÍCÍ 4 + #7 DOKONČENO (2026-06-11, `/goal dodelej to co chybí ultracode`)

Po uživatelových rozhodnutích (3 forky) dotaženo všech 5 zbývajících položek, TDD, **suite 141 → 158/158, build 0/0**.
Adversariální review (5 paralelních dimenzí) potvrdil korektnost a našel 3 reálné nálezy (1 MEDIUM + 2 LOW), všechny opraveny.

- ✅ **#7 ForwardedHeaders (confirmed-HIGH, byl NEDODĚLANÝ)** — `PlatformWebExtensions.cs` inline `new ForwardedHeadersOptions`
  bez `KnownProxies` → spoofovatelný `X-Forwarded-For` za LB (otrávení audit IP + bypass auth rate-limitu). Fix:
  `ForwardedHeadersSettings` (Enabled/KnownProxies/KnownNetworks/ForwardLimit) bound z configu + `Configure<ForwardedHeadersOptions>`
  (`.KnownIPNetworks`, .NET 10 `KnownNetworks` deprecated) + `ForwardedHeadersSettingsValidator` (fail-fast non-Dev na prázdný
  trust-list, vzor JwtOptionsValidator; parse-validace IP/CIDR ve všech env). `UseForwardedHeaders()` no-arg čte DI options,
  gateováno `Enabled` (escape hatch reálně vypíná middleware). 8 testů.
- ✅ **audit-IP (rozhodnutí: konfigurovatelné, default Full)** — `AuditOptions.IpStorage` Full|Truncated|None + pure
  `AuditIpMasking.Apply` (IPv4 /24, IPv6 /48, **IPv4-mapped IPv6 normalizace přes `MapToIPv4()`** — review MEDIUM fix:
  dual-stack Kestrel hlásí IPv4 jako `::ffff:a.b.c.d`, bez normalizace kolaps na `::`). Maskuje JEN audit IP (rate-limit drží
  plný IP). 6 testů. Pozn.: jen audit IP — `tenant.IpAddress`, ne `context.Connection.RemoteIpAddress` (ten rate-limit).
- ✅ **wire-names (rozhodnutí: guard test, ne atribut)** — Wolverine 6.5.1 honoruje wire-identity jen přes `[MessageIdentity]`
  (porušil by Contracts→jen-Cqrs) nebo default full-type-name; code-based convention pro serialization-alias NEEXISTUJE
  (Context7). Řešení: `MessageWireIdentityTests` zmrazí full-names všech 9 `IIntegrationEvent` → rename/move = build fail
  (breaking wire change). Respektuje hranice, bez atributu.
- ✅ **PII bounded retention (rozhodnutí: scrub via bounded retention)** — nula-PII u mailu nemožná (send-obálka plaintext
  adresu nutně potřebuje). `PlatformMessaging`: `KeepAfterMessageHandling=5m` + **`DeadLetterQueueExpirationEnabled=true`**
  (default OFF = PII navždy!) + `DeadLetterQueueExpiration=7d`. Recovery NEzávisí na dead-letter (ReconcileStripe rekonstruuje
  z live Stripe stavu — ověřeno review). Redis replay TTL 60m+MAXLEN 100 dokumentováno jako PII-bound best-effort.
- ✅ **Worker/Jobs/Migration host boot test** — extract `WorkerHostBuilder`/`JobsHostBuilder`/`MigrationHostBuilder`;
  `tests/ModularPlatform.Hosts.Tests` (separátní assembly = izolovaný proces, jen `Build()` bez StartAsync → static
  PII-protector se nenastaví → 1-host invariant nevadí). Build v Development validuje DI graf (ValidateOnBuild+Scopes).
  **CHYTIL 2 REÁLNÉ LATENTNÍ BUGY:** Jobs i MigrationService registrovaly Notifications handlery (`IRealtimePublisher`),
  ale NEvolaly `AddPlatformRealtime` → nenaplnitelný DI graf (skrytý — ValidateOnBuild mimo Dev OFF). To je přesně A4 díra,
  kterou předchozí audit označil za „done", ale dodělaná NEBYLA. Opraveno (+ Realtime csproj ref). Boot testy ověřují i PII
  durability settings. 3 testy.

**Nový test projekt `tests/ModularPlatform.BuildingBlocks.Tests`** (pure-logic, vyplňuje díru: building-blocks měly 0 unit testů).

### Adversariální review nálezy (2026-06-11) — všechny opraveny
- **MEDIUM (real):** IPv4-mapped IPv6 → `::` v Truncated (viz audit-IP fix výše).
- **LOW (real):** `ForwardedHeaders:Enabled=false` byl no-op (middleware běžel vždy) — gateováno.
- **LOW (real):** malformed IP/CIDR throwlo pozdě — TryParse ve validatoru (čistý fail-fast i v Dev).
- Ostatní: nity „none needed"; pre-existing A1 transport (mimo scope tohoto tasku); review potvrdil security ordering
  (UseForwardedHeaders před UseRateLimiter/Auth) a retention semantiku jako korektní.

## Zbývá

- **Wave 3** (zbytek GDPR/PII): plaintext PII ve Wolverine obálkách / Redis replay /
  audit-IP, retention sweep re-mint DEK, PersonalDataProtector přes READ connection.
- **Wave 4** (architektura/kontrakt): ArchUnit pro všechny moduly (A3), pipeline symetrie hostů (A4),
  resx kompletnost (operation.not_found/role.not_found/8 Identity kódů), RFC9457 pro framework errors,
  Location LinkGenerator, OpenAPI schémata, [MessageIdentity] aliasy, FakeGateway env guard.
- **Wave 5** (test kvalita): vacuózní assertions, mirror testy, race-prone delays, neotestované branche, Worker/Jobs host boot.

## Fix wave 2026-06-11 — /fullreview (8 oblastí) + `/goal fixni všechny bugy + nekonzistence + feature coverage`

`/fullreview` (8 paralelních skeptiků přes celou codebase) našel napříč platformou: 1 HIGH, 8 MEDIUM, 7 LOW (vše
PRE-EXISTING, žádný ze session změn). Pod /goal mandátem dotaženo **všech 18** + doc-debt, TDD, **suite 158 → 160/160,
build 0/0**.

| # | Sev | Fix | file |
|---|-----|-----|------|
| 1 | HIGH | **Login user-enumeration timing oracle** — neznámý email vracel hned bez Argon2; teď VŽDY verify proti reálnému/dummy hashi (guard `&& hasRealHash` aby erased/blank-hash neprošel ani na dummy-match) | `LoginHandler.cs:32-67` |
| 2 | MED | **AssignRole 500 místo idempotence** — concurrent grant → DbUpdateException → 500; teď catch (Law 2 idiom) + concurrency test | `AssignRoleHandler.cs:26` |
| 3 | MED | **Reconcile bez per-item try/catch** — 1 non-404 Stripe chyba abortovala sweep; Pass 2 + Pass 3 per-item izolace | `ReconcileStripeHandler.cs:92,140` |
| 4 | MED | **consent_records ani export ani erasure** — `ConsentPersonalDataExporter` (→ Art. 15) + `ConsentPersonalDataEraser` (DELETE — bez AML/tax retence), registrované; GD-6 test | `Gdpr/Features/Consents/*` |
| 5 | MED | **FilesModule chyběl v Jobs Discover** — drift (5/6); přidán + csproj ref (boot test pokrývá) | `JobsHostBuilder.cs:41` |
| 6 | MED | **Cron bez InTimeZone(Utc)** — Quartz defaultoval host TZ (Law #7); 4 triggery → `InTimeZone(TimeZoneInfo.Utc)` | Gdpr/Billing/Jobs |
| 7 | MED | **Ops stuck-Pending** — MarkRunning mimo try → stuck Pending po dead-letter; teď celé v try → FailAsync (terminál) | `RunDemoOperationHandler.cs:13` |
| 8 | MED | **SSE unbounded channel** — zaseknutý konzument = memory leak; bounded(256)+DropOldest + DeliverLocal per-handler izolace | `RealtimeStreamEndpoint.cs:54`, `Realtime.cs:72` |
| 9 | MED | **Client idempotency-key kolize** se systémovými (`purchase:`…) — endpoint prefixuje `client:` | `CreditTopUpEndpoint.cs:22` |
| L | LOW | RegisterUser catch zúžen na 23505 (ne každý DbUpdateException→email_taken) | `RegisterUserHandler.cs:68` |
| L | LOW | RefreshToken `FirstOrDefault`→401 (ne 500 při hard-deleted user) | `RefreshTokenHandler.cs:69` |
| L | LOW | Seeder catch→Exception (boot-before-migration jako backfill) + AssignAdmins `DeletedAt==null` (re-grant soft-deleted) | `IdentitySeeder.cs:41,102` |

**Doc-debt:** RetentionSweep XML (popisoval starý hard-delete → opraveno na „tombstone permanent re-mint guard"),
AAD komentář (caveat: whole-envelope copy je mimo threat model), Jobs Balanced note (Worker drainuje publikované zprávy).

**Vědomě NEřešeno (by-design, ne bug):** ExpireCredits all-or-nothing bucket skip (kredity přežijí expiraci když hold
straddluje) — dokumentovaný tradeoff pro non-negative `available` + unique idempotency key; změna money enginu vyžaduje
návrh (Refactor-over-Quick-Fix), ne tichý hack. PersonalDataProtector AAD whole-envelope binding (mimo threat model).

**Adversariální review fixů (5 dimenzí, předchozí wave):** potvrdil security ordering + retention semantiku; 3 nálezy
(IPv4-mapped IPv6 mask kolaps, ForwardedHeaders Enabled=false no-op, malformed IP late-throw) — všechny opraveny.

### Coverage-fix-wave (per-feature analýza, 8 agentů → další reálné nálezy, vše opraveno)
Multi-agentní feature-coverage analýza (78 features, → `docs/feature-coverage.md`) odhalila nad rámec /fullreview:
- **🟡 Rate-limiter per-user partition mrtvý** — klíčoval na `Identity.Name` (null) → všichni auth uživatelé v 1 bucketu;
  fix: partition přes `ClaimTypes.NameIdentifier` (userId) — `PlatformWebExtensions.cs`.
- **Register endpoint bez rate-limitu** (vs login/refresh) → `.RequireRateLimiting("auth")`.
- **Login nezamítal soft-deleted** účty (mohl re-grant admin) → `DeletedAt` guard (mirror refresh).
- **Erasure fan-out bez per-eraser try/catch** → per-eraser izolace + shred vždy + retry (mirror export).
- **GlobalExceptionMiddleware bez HasStarted guardu** (SSE mid-stream throw) → guard.
- **Retry-After** docstring vs kód → `OnRejected` emituje Retry-After z lease metadata.
- **`SseStream<T>` dead code** (0 ref) → smazáno; **JwtOptionsValidator** 5 unit testů; **5 doc-driftů** (CreditAccount
  „FOR NO KEY UPDATE", AddPlatformPersistence „once per host", RetentionSweep summary, CLAUDE.md §4).
- **Suite 158 → 165/165, build 0/0.** Deferred (by-design/perf, s rationale) v `docs/feature-coverage.md`.

### Edge-case test wave (docs do CLAUDE.md+skills + „100% ready" testy) → chytil další reálný bug
ultracode workflow nadraftoval edge-case testy (no-enum parity, expired refresh, security headers, **rate-limit
per-user isolace**, Retry-After, file-not-found 404, paging clamping); +16 testů, **165 → 181/181**.
- **🟡 REÁLNÝ BUG (rate-limiter ordering)** — `PL11` (per-user bucket) selhal: `UseRateLimiter()` běžel **PŘED**
  `UseAuthentication()` → v čase limiteru nejsou claims → padá na sdílený IP bucket. Tj. ani předchozí NameIdentifier
  fix nestačil. Oprava: pipeline **auth → rate-limiter → authorization** (`PlatformWebExtensions.UsePlatformWeb`).
  Per-user partition teď prokazatelně funguje (PL11 zelený).
- **Docs aktualizovány:** CLAUDE.md (§1 host buildery + test projekty, §3 step 6 = 4 hosty + Hosts.Tests + wire-name guard,
  §4 +3 řádky: auth hardening rozšířen, Request-edge hardening, Durable-envelope PII bound) + skills `adding-a-module`
  (Discover ve 4 hostech + Hosts.Tests + wire-name) a `writing-modularplatform-tests` (4 vrstvy vč. host-boot + edge-cases).
  Plný architektonický přehled: **`docs/feature-coverage.md`**.

## Verdikt

**Zatím NENÍ stable base.** Funkčně je hotová celá plánovaná roadmapa (108/108 testů, build 0/0), ale
audit našel **1 critical, ~21 high a ~40 medium** reálných problémů. Tři z nich jsou stop-shipy pro
jakýkoli produkt nad platformou:

1. **Kdokoli si vytiskne neomezeně kreditů zdarma** (critical) — `POST /v1/billing/credits/topup` je
   jen `RequireAuthorization()`, bere částku z body. Monetizace je tím obejitelná jedním requestem.
2. **GDPR „erased" uživatel má session navždy** (high) — refresh path ignoruje `DeletedAt`, eraser
   nerevokuje refresh tokeny, žádný logout endpoint neexistuje → smazaný/zablokovaný účet dál těží
   access tokeny dokud token nevyprší (a rotace ho prodlužuje donekonečna).
3. **Defaultní deployment topologie je rozbitá** (high) — Worker startuje v `Solo` vedle `Balanced`
   Api; navíc messaging nemá transport (jen durable local queues), takže handlery běží v procesu,
   který zprávu publikoval, ne ve Workeru. To je přesně ta mixed-mode konfigurace, před kterou
   varuje vlastní dokumentace.

---

## Architektura (co tam reálně je)

**Vzor:** Modulární monolith, CQRS vertical slices, žádné generické services. `ICommand<T>`/`IQuery<T>`
→ thin `IDispatcher` → pipeline behaviors (Telemetry → Logging → Validation → ConcurrencyRetry).
Moduly = trio `Core` (internal) + `*.Contracts` (public, jen Cqrs) + `Tests`. Hranice hlídá ArchUnitNET.

**Vrstvy:**
- `building-blocks/`: Cqrs, Abstractions, Persistence (PlatformDbContext, AuditInterceptor, RLS,
  PII-encryption interceptor, xmin concurrency), Messaging (Wolverine), Web (RFC9457, JWT, rate-limit,
  SSE, i18n), Telemetry (OTel), Realtime (Redis fan-out + replay), Storage (local/S3).
- `modules/`: Identity, Billing, Notifications, Gdpr, Operations, Files.
- `hosts/`: Api (HTTP), Worker (Wolverine listener), Jobs (Quartz cron), MigrationService.

**Datové invarianty:** vše UTC přes `IClock`; Guid v7; snake_case; EF/LINQ only (žádné raw SQL);
optimistic concurrency přes Postgres `xmin` + `ConcurrencyRetryBehavior`; idempotence = UNIQUE key +
catch `DbUpdateException`; peníze = append-only ledger + atomický `ExecuteUpdate` guard na debit path;
izolace = EF tenant filter + Postgres RLS na `IUserOwned`/`ITenantScoped`; PII at-rest = `[Encrypted]`
AES-GCM pod per-subject DEK + blind index pro lookup; GDPR erasure = crypto-shred DEK.

**Architektonické problémy nalezené auditem (ne jen bugy):**
- **A1 — Messaging nemá transport.** Jen `PersistMessagesWithPostgresql` + `UseDurableLocalQueues()`,
  žádný `ListenToPostgresqlQueue`. Důsledek: integration-event handler běží v procesu, který událost
  publikoval. Api tedy posílá SMTP a běhá sagy; Jobs host zpracovává Stripe platby; Worker je fakticky
  jen recovery uzel. To je v rozporu s tím, jak je topologie prezentovaná (Api + dedikovaný Worker).
  *Rozhodnutí potřeba:* buď přidat PG transport a explicitní listening, nebo přepsat dokumentaci a
  smířit se s „handler runs in publisher".
- **A2 — `ConcurrencyRetryBehavior` se registruje jednou per modul** (`AddPlatformPersistence` volá
  každý modul, `AddPipelineBehavior` nededupuje). Při 6 modulech běží příkaz přes 6 vnořených retry
  vrstev → amplifikace až 5⁶ pokusů na jeden konflikt.
- **A3 — ArchUnitNET hranice jsou skoro bezzubé.** Jediné cross-Core pravidlo pokrývá jen Identity a
  jeho regex matchuje jen kořenové namespaces. „Core nikdy nereferencuje jiný Core" tedy NENÍ vynucené
  pro Billing/Gdpr/Notifications/Operations/Files.
- **A4 — Pipeline asymetrie mezi hosty.** `ValidationBehavior` + `LoggingBehavior` se registrují jen
  v `AddPlatformWeb` (volá jen Api). Worker/Jobs/MigrationService tedy běží příkazy BEZ validace a
  strukturovaného logování. Navíc `IRealtimePublisher` chybí v Jobs/MigrationService DI, ač tam běží
  Notifications handlery, které ho injektují → nenaplnitelný DI graf při určitých cestách.

---

## Komunikace mezi moduly (jak to reálně funguje)

- **Jednosměrný fakt → integration event** z `*.Contracts` přes outbox (`IDbContextOutbox.PublishAsync`
  + `SaveChangesAndFlushMessagesAsync` = commit). Konzument = public `Handle(TEvent, deps)` shell, který
  dispatchne interní command. Inbox (UNIQUE MessageId) dedupuje. Funkční end-to-end (`CrossModuleEventTests`).
  Kanonický producent: `RegisterUserHandler` → `UserRegisteredIntegrationEvent` → Billing provisne
  credit account, Notifications pošle welcome.
- **Potřebuju data z jiného modulu →** dispatcher query (in-proc) nebo denormalizovaná kopie držená
  v sync přes eventy. Nikdy JOIN přes moduly, nikdy reference cizího Core. Vše po Id.
- **Saga** (Wolverine, EF-persisted): `CreditPurchaseSaga` (checkout → grant přes idempotentní command).

**Křehkosti v komunikaci (audit):**
- Dvě handlery pro `UserRegisteredIntegrationEvent` (Billing provisioning + Notifications welcome) jsou
  Wolverinem sloučeny do jedné logické jednotky → selhání welcome retryuje a dead-letteruje i Billing
  provisioning. *(unverified — recovered)*
- Retry policy = `RetryWithCooldown(100ms,500ms,3s)` → ~3.6 s a pak permanentní DLQ. Dead-letterovaný
  `CreditPurchaseConfirmed` = zaplacený zákazník nedostane kredity a **žádný job ho nere-queue** (jen
  WARN gauge). *(unverified — recovered, ale logicky navazuje na confirmed money nálezy)*
- Žádný message contract nemá `[MessageIdentity]` alias → rename/přesun namespace mezi deploymenty
  osiří in-flight a scheduled durable obálky (saga timeouts visí celé checkout okno). *(unverified)*
- Plaintext PII (email, display name, rendered body) leží v `wolverine.*` outbox/inbox/dead-letter
  tabulkách mimo crypto-shred a retention. *(confirmed, medium)*

---

## Feature inventář (per oblast)

Legenda stavu: ✅ done · ◐ partial · ▢ stub. „Stabilita" = posouzení z auditu, ne z testů.

### Identity (14 featur, 37 test-refs)
| Feature | Handling | Testy | Edge cases ošetřené | Stabilita |
|---|---|---|---|---|
| RegisterUser | ✅ outbox slice, blind-index dup guard, tenant provision | 4 (E2E, dup, blind-index, tenancy) | dup email casing, UNIQUE race→409, anon tenant stamp | ⚠️ tenants.Name = plaintext email (HIGH) |
| Login | ✅ slice, lockout, admin bootstrap | 5 | no-enum 401, lockout 5×/15min, erased blank-hash, admin idempotent | ⚠️ AdminEmails takeover (MED), no auth rate-limit na register |
| RefreshToken rotation+reuse | ✅ kanonický security slice | 3 | unknown/consumed/expired, family revoke audited, paralelní 1 winner | 🔴 ignoruje DeletedAt; benign parallel nuke (MED); sliding=∞ session (MED) |
| GetProfile | ✅ read factory, id z tokenu | 4 | missing claim→401, filtr→404, decrypt | ✅ |
| Assign/RevokeRole | ✅ permission-gated | 1 / 0 | idempotent | ⚠️ RevokeRole 0 testů; role.not_found chybí v resx |
| GetUserAuditTrail | ✅ admin forensic, PII reveal | 2 | erased→[erased] | ✅ |
| Roles & permissions + Seeder | ✅ claims snapshot | 2 | auto-seed, auto-grant admin | ⚠️ stale claims po revoke (accepted?) |
| PII encryption + blind index + backfill | ✅ interceptor + converter | 5 | shredded→[erased], dup přes index | ✅ (silná část) |
| GDPR export/erasure ports | ✅ | 2 | tombstone email | 🔴 nerevokuje refresh tokeny |
| Multi-tenancy provisioning | ✅ Tenant + claim | 4 | anon→nový tenant | ⚠️ plaintext email v Name |
| Token issuance + Argon2 | ✅ | 2 | — | ⚠️ login timing oracle (LOW) |

### Billing (18 featur, 45 test-refs) — nejlépe otestovaný modul
| Feature | Handling | Testy | Edge cases | Stabilita |
|---|---|---|---|---|
| Credit account provisioning | ✅ event konzument | 2 | UNIQUE race fix | ✅ |
| **Credit top-up** | ✅ idempotent ledger+bucket | 5 | idempotency key, race loser | 🔴🔴 **CRITICAL: public endpoint = free mince** |
| Reserve credits | ✅ atomický ExecuteUpdate guard | 5 | no double-spend (20-way) | ✅ (silné); HoldMinutes bez horní meze |
| Confirm spend | ✅ FIFO bucket draw | 3 | exactly-once | ✅ |
| Release hold | ✅ | 2 | — | ✅ |
| Expire credits + cron | ✅ lapsed holds + expired buckets | 2 | restore lapsed | 🔴 expirace bucketu s aktivní rezervací → negative → crash celého sweepu (HIGH) |
| Get balance | ✅ stored available | 2 | — | ✅ |
| Credit history | ▢ stub | 0 | — | chybí |
| Stripe webhook ingest | ✅ signed, idempotent, outbox | 3 | UNIQUE event id | ⚠️ swallows ALL DbUpdateException→ACK→event ztracen (HIGH unverif.); secret bez fail-fast |
| Stripe event router | ✅ worker-side | 4 | dedup | 🔴 grant bez PaymentStatus=="paid" (HIGH); malformed metadata→ProcessedAt (MED) |
| Packages catalogue + admin CRUD | ✅ billing.manage | 1 | — | ✅ gating správně (kontrast s topup!) |
| Package Checkout + CreditPurchaseSaga | ✅ | 2 | timeout abandon, late confirm | ⚠️ idempotency key namespace sdílený s client (HIGH) |
| Subscriptions (config plans, mirror) | ✅ object-state, per-invoice grant | 2 | out-of-order safe | ⚠️ |
| Promo codes + Tax flag | ✅ | 1 | — | ✅ |
| Stripe reconciliation + job | ◐ partial | 1 | Stripe-wins | ⚠️ poison event re-queue navždy; 1 drift error abortuje sweep (MED) |
| GDPR ports | ✅ | 2 | — | ✅ |
| IStripeGateway seam (real+fake) | ✅ | 3 | — | ⚠️ FakeGateway zapnutelný v ANY env (MED); divergence od real Stripe |
| DB money hardening (CHECK, RLS, audit) | ✅ | 5 | non-negative constraint | ✅ (constraint zachytil expiry bug) |

### Notifications (10 featur, 14 test-refs) — nejslabší test coverage
| Feature | Handling | Testy | Edge cases | Stabilita |
|---|---|---|---|---|
| SendNotification slice | ✅ multi-channel | 3 | — | ⚠️ vždy persistuje in-app i když nevyžádané; email bez data["email"] tiše zahozen (MED) |
| Channel delivery workers (SMTP/push) | ◐ | 0 | — | ⚠️ 0 testů |
| Templates + rendering + seed | ✅ welcome/purchase en+cs | 1 | template_not_found skip | ⚠️ |
| In-app feed (Get/MarkRead) | ✅ | 1 | — | ✅ |
| UserRegistered → welcome | ✅ | 1 | NotFound skip | ⚠️ |
| CreditPurchaseCompleted → notif | ✅ | 1 | — | ⚠️ |
| Realtime publishing | ◐ | 0 | — | 🔴 post-commit publish fail → duplicate maily + DLQ (MED) |
| GDPR export/erasure | ✅ | 3/1 | blank Title/Body | ⚠️ erasure race re-zavede PII (MED); plaintext v Redis replay (MED) |
| PII na Notification entity | ✅ audit shred | 3 | — | ⚠️ Title/Body [PersonalData] ale NE [Encrypted] |

### Gdpr (9 featur, 25 test-refs)
| Feature | Handling | Testy | Stabilita |
|---|---|---|---|
| Export fan-out (GD-4) | ✅ | 3 | ⚠️ vynechává Files |
| Erasure flow + crypto-shred | ✅ | 6 | 🔴 nerevokuje sessions; multi-tx bez idempotence (MED) |
| Consent (append-only) | ✅ | 1 | ⚠️ wire UserId ignorován (MED) |
| SubjectKey + CryptoShredder + Protector | ✅ | 6 | 🔴 píše přes READ connection (replica break, HIGH); retention smaže tombstone→re-mint DEK (MED) |
| PII at-rest encryption | ✅ | 3 | ✅ |
| Blind index | ✅ | 1 | ✅ |
| PII backfill | ✅ | 0 | ⚠️ 0 testů |
| Retention sweep | ✅ | 3 | ⚠️ maže shred marker (HIGH unverif.) |

### Operations + Files (9 featur, 16 test-refs)
| Feature | Handling | Testy | Stabilita |
|---|---|---|---|
| IOperationStore + tabulka | ✅ | 1 | 🔴 žádný state-machine guard, overwrite terminal (HIGH unverif.) |
| 202 + Location accept | ✅ demo | 1 | ✅ |
| Durable worker transition | ✅ | 1 | 🔴 no try/catch → FailAsync nikdy; operace stuck Running navždy (HIGH unverif.) |
| Operation status GET | ✅ RLS | 1 | 🔴 IDOR když Rls:Enabled=false (HIGH unverif.) |
| File upload | ✅ allowlist, size cap | 3 | ⚠️ orphaned blob při metadata fail (MED); content-type jen z headeru (LOW); CR/LF filename→500 (LOW) |
| File download | ✅ stream | 2 | 🔴 IDOR když Rls off (HIGH confirmed); missing blob→500 (LOW) |
| File list | ✅ paged, explicit owner filter | 1 | ✅ (jediný s defence-in-depth filtrem) |
| Storage key + traversal guard | ✅ | 4 | ✅ |
| **Files modul jako celek** | — | — | 🔴 NEMÁ GDPR export/erasure → soubory nesmrtelné po erasure (HIGH) |

### Building-blocks (20 featur, 63 test-refs)
Většina ✅ a silně testovaná. Problémové: Realtime fan-out+SSE+replay (◐, viz níže), RLS dual-role
(⚠️ bez startup checku na BYPASSRLS), Web pipeline (⚠️ framework errors mimo RFC9457), Telemetry (0 testů),
ConcurrencyRetry (A2 amplifikace).

### Hosts (16 featur, 15 test-refs) — nejméně testované
Worker/Jobs/MigrationService nemají integrační testy (vše běží v Api procesu pod SoloMode). Jobs host:
Quartz na RAM store bez clusteringu, throwing job tichý (jen log, čeká na další cron), MessagingHealthJob
sleduje špatný counter (Scheduled místo Outgoing).

### Test suite (102 testů) — kvalita, ne počet
8 z 15 oblastí je `partial`. Konkrétní díry: vacuózní assertion `WaitForCountAsync(expected:0)` vždy
projde (ID-8 family revoke neověřuje nic); `SubjectKeyShredTests` testují privátní kopii handleru, ne
handler; lockout expiry/reset netestováno; expired-token branch 0 coverage; saga timeout obchází
scheduling (manuální inject); cross-user notif send (RLS WITH CHECK) jen v komentáři; Worker/Jobs host
nikdy nebootnut; `Task.Delay` race v negativních assertions; FakeStripe divergence; Files negativní
testy jen status, ne „nic se nepersistovalo".

---

## Confirmed defekty (ověřené skeptikem) — prioritizováno

### 🔴 CRITICAL
1. **Public credit top-up = ražba peněz.** `CreditTopUpEndpoint.cs:26` jen `RequireAuthorization()`,
   částka z body, fresh idempotency key → libovolný user si dá až 1e9 kreditů/call zdarma. Command má
   zůstat interní (saga/subscription grant). **Fix:** odebrat public endpoint, nebo gate
   `RequirePermission(BillingManage)`.

### 🔴 HIGH (confirmed)
2. **Erased/locked user má perpetuální session.** `RefreshTokenHandler.cs:79` `IgnoreQueryFilters()`
   bez `DeletedAt`/lockout checku; `IdentityPersonalDataEraser` nerevokuje refresh tokeny. **Fix:**
   eraser revokuje rodiny (tracked+audited) + refresh odmítne `DeletedAt != null`.
3. **Žádný logout / session-revoke endpoint.** Ukradený refresh token nelze zneplatnit (a rotace ho
   prodlužuje navždy). **Fix:** `POST /identity/auth/logout` (id z tokenu) + admin revoke-all.
4. **Expirace bucketu s aktivní rezervací → negative Available → crash celého expiry sweepu.**
   `ExpireCreditsHandler.cs:72` debituje plný `Remaining` i kredity držené rezervací; CHECK constraint
   23514 hodí `DbUpdateException` (ne concurrency, retry to nepohltí), bez per-account try/catch shodí
   sweep pro všechny účty za viníkem. User-triggerable (neomezené HoldMinutes). **Fix:** cap `lost` na
   volnou část + per-account try/catch.
5. **Stripe grant bez `PaymentStatus=="paid"`.** `checkout.session.completed` u async metod (SEPA,
   bank transfer) dává kredity před settlementem; `async_payment_failed` = kredity zdarma. **Fix:**
   vyžadovat `paid`/`no_payment_required` + routovat `async_payment_succeeded`.
6. **Idempotency keys v jednom globálním namespace.** Cross-user kolize lidského klíče („order-1") →
   tichý no-op (řečeno success, granted 0); client klíč může otrávit systémové `purchase:`/`sub-invoice:`
   grant. **Fix:** UNIQUE `(AccountId, IdempotencyKey)` + prefix client klíčů.
7. **ForwardedHeaders KnownProxies nejde nakonfigurovat.** `PlatformWebExtensions.cs:63` inline instance
   bypassuje DI → za LB všichni klienti kolabují do jedné IP → auth rate-limit (10/min) shodí login pro
   celý deployment; audit IP = LB IP. **Fix:** bind z configu + ValidateOnStart.
8. **Files modul nemá GDPR export ani erasure.** `FileObject` drží originální filename (PII) + bytes
   (nejcitlivější data); po „úplné" erasure vše přežije, export soubory vynechá. **Fix:**
   `FilesPersonalDataEraser` (smaž blob + řádek) + `FilesPersonalDataExporter`.
9. **Download spoléhá JEN na RLS.** `GetFileHandler.cs:20` filtruje jen `Id`; s `Rls:Enabled=false`
   (dokumentovaná konfigurace) = plný IDOR. CLAUDE.md §7 „app-level filters remain" je u tohoto slice
   nepravda. **Fix:** přidat `&& f.UserId == query.UserId` (jako `ListFilesHandler`).
10. **Registrace zapisuje plaintext email do `tenants.Name`.** Nešifrovaný at-rest, v auditu, přežije
    erasure. **Fix:** neutrální jméno `tenant-{id}` nebo `[Encrypted]` + do eraseru.

### Confirmed MEDIUM (výběr)
AdminEmails pre-registration takeover · register bez auth rate-limit · sliding refresh = ∞ session ·
benign parallel refresh nuke (UX re-login) · plaintext PII ve Wolverine obálkách · erasure race
re-zavede notif PII · retention sweep re-mint DEK · audit IP navždy · Protector píše přes READ
connection (replica break) · realtime publish fail → duplicate maily · Last-Event-ID garbage →
mid-stream Redis error · SSE bez re-validace tokenu · plaintext PII v Redis replay · orphaned blob.

---

## Unverified (recovered — verifikace nedoběhla, ale věrohodné; část potvrzena twinem)

HIGH: ConcurrencyRetry per-modul amplifikace (A2) · ArchUnit toothless (A3) · Stripe webhook swallows
DbUpdateException→ACK→ztráta eventu · GlobalExceptionMiddleware bez cancellation (každý disconnect =
ERROR + 500 write) · default Solo Worker vedle Balanced Api · retry ~3.6s pak permanent DLQ bez
re-queue · žádný transport, handler v publisher procesu (A1) · operace stuck Running navždy · Operation
store bez state-machine guardu · MessagingHealthJob špatný counter · retention maže shred tombstone ·
Stripe secret bez fail-fast + testy institucionalizují prázdný secret.

MEDIUM: Worker/Jobs bez Validation+Logging (A4) · IRealtimePublisher chybí v Jobs DI · FakeGateway v
any env · `operation.not_found`+`role.not_found`+8 Identity validator kódů chybí v resx · per-field
validace nelokalizovaná · framework errors mimo RFC9457 (401/403/429/400 prázdné) · Location string-concat
míjí /v1 · vyčerpané retries → 500 místo 409 · consent wire UserId ignorován · OpenAPI bez response
schémat · dva handlery UserRegistered fúzované · reconcile poison re-queue navždy · message bez
[MessageIdentity] · GDPR fan-out bez idempotence · Jobs hardcoduje Balanced · Quartz RAM store bez
clusteringu · throwing job tichý · reconcile 1 fail abortuje sweep · notif cross-user 500 pod RLS ·
TenantStamping no-op v system kontextu · bez BYPASSRLS startup checku · + 10 test-quality děr.

---

## Navržený iterační plán (waves ke stable base)

**Wave 1 — Stop-ship bezpečnost a peníze (musí být před čímkoli):**
1, 2, 3, 5, 6, 9 + ops/files IDOR + Stripe webhook swallow. Cíl: nelze ukrást peníze ani data, erasure
opravdu odřízne přístup.

**Wave 2 — Robustnost běhu (žádný tichý fail / DoS):**
4 (expiry crash), A1+A2 (transport + retry amplifikace), default durability topologie, operace stuck
Running, retry→DLQ bez re-queue, Quartz clustering, throwing job alerting, MessagingHealthJob counter.

**Wave 3 — GDPR/PII úplnost:**
8 (Files GDPR), 10 (tenant email), PII ve Wolverine/Redis/audit-IP, retention tombstone re-mint,
Protector READ connection.

**Wave 4 — Architektonická čistota + kontrakt:**
A3 (ArchUnit pro všechny moduly), A4 (pipeline symetrie hostů), resx kompletnost + test guard,
RFC9457 pro framework errors, Location LinkGenerator, OpenAPI schémata, [MessageIdentity] aliasy,
FakeGateway env guard.

**Wave 5 — Test kvalita:**
odstranit vacuózní assertions, testovat reálné handlery (ne mirrory), pokrýt expired-token/lockout
reset/saga timeout/cross-user reject/missing-blob, nahradit `Task.Delay` race za deterministické
čekání, bootnout Worker/Jobs host v testech.

Iterace pokračují dokud audit nevrací jen low/cosmetic. Po každé wave: re-run cílené dimenze auditu.
