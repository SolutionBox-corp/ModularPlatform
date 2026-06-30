# ModularPlatform — Feature Coverage (USE / Edge / Test cases)

> Komplexní mapa: každá feature, její **use cases**, **edge cases** (a jak se k nim platforma staví —
> handled/partial/unhandled s file:line), **test cases** (co je pokryté + gapy), a verdikt jestli to máme
> správně. Vzniklo multi-agentní analýzou (8 paralelních architektů, read-only, ~1M tokenů) napříč celou
> codebase pod `/goal ... sepiš USE/Edge/Test cases ... máme správně vše`. Doplňuje `docs/test-scenarios.md`
> (Given/When/Then) o feature-centrický pohled.

**Stav: 165/165 testů zelených, build 0/0/0 (0 warnings, 0 errors).** 78 features, 8 oblastí.

## Jak číst
- **Verdikt:** ✅ correct · 🟢 minor-gaps (drobné, vesměs test-coverage) · 🟡 has-gaps · 🔴 risky.
- **Edge case status:** ✓ handled · ◐ partial · ✗ unhandled · – n/a.
- Přehledová tabulka verdiktů je hned pod tímto úvodem; detail per feature následuje po oblastech.

## Vyřešeno PO analýze (tato session — coverage-fix-wave)
Analýza odhalila nálezy nad rámec `/fullreview`; reálné z nich opraveny (TDD, build-green). Edge-case statusy
v tabulkách níže jsou **snapshot PŘED** těmito fixy:
- **Rate-limiter per-user partition byl mrtvý** (jediný 🟡 has-gaps) — klíčoval na `Identity.Name` (null, žádný
  name claim) → všichni autentizovaní uživatelé v JEDNOM bucketu. Fix: partition přes `NameIdentifier` claim (userId).
- **Register endpoint bez rate-limitu** (na rozdíl od login/refresh) — unthrottled Argon2 + tenant INSERT + enumeration
  (409 vs 201). Fix: `.RequireRateLimiting("auth")`.
- **Login nezamítal soft-deleted účty** — deaktivovaný (ne-erased) účet se mohl přihlásit + re-grant admin. Fix:
  `DeletedAt` guard (mirror refresh).
- **Erasure fan-out bez per-eraser try/catch** (asymetrie s exportem) — 1 eraser throw → blok ostatních + crypto-shred.
  Fix: per-eraser izolace + shred vždy + retry na selhání.
- **GlobalExceptionMiddleware bez `HasStarted` guardu** — write problem-body po flushnuté SSE odpovědi → throw. Fix: guard.
- **Retry-After header** — docstring tvrdil „429 + Retry-After", kód neemitoval. Fix: `OnRejected` + Retry-After z lease metadata.
- **`SseStream<T>` dead code** (0 referencí, duplikoval inline bounded channel endpointu) → smazáno.
- **JwtOptionsValidator bez testů** (sibling ForwardedHeaders měl 7) → 5 unit testů doplněno.
- **Doc-drifty opraveny:** CreditAccount „FOR NO KEY UPDATE" (reálně atomic ExecuteUpdate), AddPlatformPersistence
  „once per host" (reálně per-module/idempotent), RetentionSweep summary + CLAUDE.md §4 (tombstones permanentní, ne purge).

## Vědomě NEvyřešeno (by-design / nižší priorita — s rationale)
- **ExpireCredits all-or-nothing bucket skip** — kredity přežijí expiraci když hold straddluje bucket; dokumentovaný
  tradeoff pro non-negative `available` + unique idempotency key. Změna money enginu = návrh, ne tichý hack.
- **ConfirmSpend „bucket exhaustion"** — analýza flagla jako partial, ale money-reviewer dokázal invariantem, že
  `SUM(remaining) ≥ pending ≥ hold.Amount` vždy platí → under-draw je nedosažitelný. Ponecháno (proven safe).
- **Subscription PastDue bez reakce** — mapuje se, ale žádná revokace/notifikace; feature gap (dunning flow), ne bug.
- **`invoice.payment_succeeded` alias** k `invoice.paid` — route now handles both success events; idempotence
  `sub-invoice:{id}` chrání před double-grant, Stripe staging still validates which event set the deployment emits.
- **CreateSubscriptionCheckout TOCTOU** (double subscription v okně před prvním webhookem) — inherentní „Stripe = source
  of truth, no pre-created rows" designu.
- **Files orphan-blob při metadata fail** — uzavřeno: upload handler při selhání metadata `SaveChanges` best-effort smaže už zapsaný blob a test pinne cleanup.
- **Operations stuck-reaper** — uzavřeno: Operations má vlastní reconcile job, který staré Pending/Running operace terminalizuje jako Failed.
- **ExpireCredits N+1** přes účty — perf/škálovatelnost, ne korektnost.
- **LocalRealtimePublisher.PublishToTenantAsync no-op** vs Redis publikuje — tenant broadcast asymetrie (lokální fallback).
- **AAD whole-envelope** (mimo threat model),
  **multi-instance distributed rate-limiting** (Redis seam připravený, neimplementovaný), **duplikovaná account-provisioning
  logika** (EnsureCreditAccount vs inline v CreditTopUp) — DRY.

## Přehled — verdikt per feature

| Oblast | Feature | Verdikt |
|---|---|---|
| identity | User registration (RegisterUser) | 🟢 minor-gaps |
| identity | Login (no-enumeration, timing-equalized verify, lockout) | ✅ correct |
| identity | Refresh-token rotation + reuse detection | ✅ correct |
| identity | Logout / session-family revocation | 🟢 minor-gaps |
| identity | Profile read (GetProfile /me) | ✅ correct |
| identity | Roles & permissions (assign/revoke + token claim snapshot) | 🟢 minor-gaps |
| identity | Authorization seeding + admin bootstrap (IdentitySeeder + login-time grant) | 🟢 minor-gaps |
| identity | Audit trail reveal (admin forensics) + [erased] after shred | ✅ correct |
| identity | Multi-tenancy provisioning + isolation | ✅ correct |
| identity | GDPR export + erasure (Identity ports) | ✅ correct |
| identity | PII encryption backfill (legacy rows) | 🟢 minor-gaps |
| identity | Token issuance + password hashing (Security primitives) | ✅ correct |
| billing-credits | Credit account provisioning (EnsureCreditAccount + ProvisionCreditAccountHandler) | ✅ correct |
| billing-credits | Credit top-up (CreditTopUp) | ✅ correct |
| billing-credits | Reserve credits (atomic debit guard) | ✅ correct |
| billing-credits | Confirm spend (FIFO bucket draw) | 🟢 minor-gaps |
| billing-credits | Release hold | ✅ correct |
| billing-credits | Expire credits sweep | 🟢 minor-gaps |
| billing-credits | Get credit balance (read) | ✅ correct |
| billing-credits | Append-only double-entry ledger + projection invariant | ✅ correct |
| billing-commerce | Package catalogue (CRUD) + purchasable listing | 🟢 minor-gaps |
| billing-commerce | Package purchase checkout + CreditPurchaseSaga | ✅ correct |
| billing-commerce | Stripe webhook ingest (signed, atomic, idempotent) | ✅ correct |
| billing-commerce | Stripe event router (ProcessStripeEvent) | 🟢 minor-gaps |
| billing-commerce | Subscriptions: checkout, object-state mirror, per-invoice grant, cancel | 🟢 minor-gaps |
| billing-commerce | Promo-code validation (coupons) | ✅ correct |
| billing-commerce | Stripe Tax flag | 🟢 minor-gaps |
| billing-commerce | Stripe reconcile sweep (3 passes, per-item isolation) | 🟢 minor-gaps |
| billing-commerce | IStripeGateway anti-corruption seam (real + fake) | ✅ correct |
| gdpr-pii | PII at rest — [Encrypted] interceptor + decrypting converter | 🟢 minor-gaps |
| gdpr-pii | PersonalDataProtector (audit-PII crypto envelope) | ✅ correct |
| gdpr-pii | CryptoShredder (AES-256-GCM primitive) | ✅ correct |
| gdpr-pii | Blind index (HMAC) + fail-fast key validation | 🟢 minor-gaps |
| gdpr-pii | Erasure fan-out + crypto-shred (UserErasureRequested -> shred) | 🟢 minor-gaps |
| gdpr-pii | Consent log (append-only, exported + erased) | ✅ correct |
| gdpr-pii | Export fan-out (IExportPersonalData) | ✅ correct |
| gdpr-pii | Retention sweep (tombstone permanent re-mint guard) | 🟢 minor-gaps |
| notifications-realtime | SendNotification (multi-channel dispatch) | ✅ correct |
| notifications-realtime | Email / Push channel delivery (Worker-side) | 🟢 minor-gaps |
| notifications-realtime | Templates + rendering + seeding | 🟢 minor-gaps |
| notifications-realtime | In-app feed (get / mark-read) | ✅ correct |
| notifications-realtime | Cross-module reaction handlers (welcome + purchase-completed) | ✅ correct |
| notifications-realtime | Realtime fan-out (publisher + Redis + registry) | ✅ correct |
| notifications-realtime | SSE stream endpoint (/realtime/stream) | 🟢 minor-gaps |
| notifications-realtime | Replay buffer (Last-Event-ID, TTL) | ✅ correct |
| notifications-realtime | GDPR export / erasure (Notifications) | 🟢 minor-gaps |
| operations-files | Long-running operation accept (202 + outbox) | ✅ correct |
| operations-files | Operation state machine + terminal guard (OperationStore) | 🟢 minor-gaps |
| operations-files | Operation status polling (owner-scoped read) | ✅ correct |
| operations-files | File upload (server key, allowlist, size cap) | 🟢 minor-gaps |
| operations-files | File download (stream, IDOR/404) | 🟢 minor-gaps |
| operations-files | File list (paged, owner-scoped) | ✅ correct |
| operations-files | Blob storage providers (local + S3) & path-traversal guard | ✅ correct |
| operations-files | Files GDPR export + erasure | 🟢 minor-gaps |
| persistence-cqrs | CQRS dispatcher + pipeline ordering | ✅ correct |
| persistence-cqrs | Validation behavior | ✅ correct |
| persistence-cqrs | Error types -> HTTP mapping | ✅ correct |
| persistence-cqrs | Logging behavior (PII-safe) | ✅ correct |
| persistence-cqrs | xmin optimistic concurrency + ConcurrencyRetryBehavior | 🟢 minor-gaps |
| persistence-cqrs | Audit interceptor (changed-fields, stamps, converter values) | 🟢 minor-gaps |
| persistence-cqrs | Audit IP masking (data minimization) | ✅ correct |
| persistence-cqrs | Tenant query filter + TenantStampingInterceptor | ✅ correct |
| persistence-cqrs | Postgres RLS (bootstrap, dual role, GUC stamping) | ✅ correct |
| persistence-cqrs | Read DbContext factory | ✅ correct |
| persistence-cqrs | PII at rest: encryption interceptor + decrypting converter | ✅ correct |
| persistence-cqrs | Paging (PageRequest / PagedResponse / ToPagedResponseAsync) | 🟢 minor-gaps |
| persistence-cqrs | Entity base + conventions (xmin, soft-delete, IUserOwned/ITenantScoped) | 🟢 minor-gaps |
| messaging-hosts-web | Wolverine durable messaging configuration (PlatformMessaging.Configure) | ✅ correct |
| messaging-hosts-web | Host composition & DI graph (Api/Worker/Jobs/Migration builders) | ✅ correct |
| messaging-hosts-web | Jobs host: Quartz cron (UTC), idempotency, single-instance posture | ✅ correct |
| messaging-hosts-web | Messaging-health job + evaluation | ✅ correct |
| messaging-hosts-web | RFC 9457 error contract + i18n (GlobalExceptionMiddleware + resx) | 🟢 minor-gaps |
| messaging-hosts-web | Rate limiting (global + auth policy) | ✅ correct |
| messaging-hosts-web | Forwarded headers (proxy trust) + audit IP | ✅ correct |
| messaging-hosts-web | JWT bearer auth + options validation | 🟢 minor-gaps |
| messaging-hosts-web | Security headers middleware | 🟢 minor-gaps |
| messaging-hosts-web | SSE building block (SseStream + native SSE endpoint) | 🟢 minor-gaps |
| messaging-hosts-web | Telemetry (OTel pipeline + TelemetryBehavior + PlatformMetrics) | ✅ correct |

Legenda: ✅ correct · 🟢 minor-gaps · 🟡 has-gaps · 🔴 risky. Edge-case status: ✓ handled · ◐ partial · ✗ unhandled.

> **Pozn.:** Tabulky níže zachycují snapshot analýzy. Část tehdy-otevřených edge cases byla v téže session opravena — viz `docs/stability-audit-2026-06-10.md` (fix-wave tabulky) a sekci „Vyřešeno po analýze" na konci.


---

## Identity — auth, authorization, tenancy, PII

### User registration (RegisterUser) — 🟢 minor-gaps
*Anonymously create a user, provision a tenant, publish UserRegisteredIntegrationEvent atomically via the outbox.*

**Use cases:** Self-service signup; Bootstrap a new tenant per registrant (B2C per-user model); Trigger downstream provisioning (Billing credit account, Notifications welcome) via the integration event

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Duplicate email (pre-check) | ✓ | Blind-index AnyAsync pre-check throws ConflictException(user.email_taken) — RegisterUserHandler.cs:33-36 |
| Concurrent duplicate races past pre-check | ✓ | UNIQUE(EmailHash) violation narrowed to Postgres 23505 -> ConflictException; unrelated DbUpdateException surfaces — RegisterUserHandler.cs:69-75 |
| Email case/whitespace normalization | ✓ | Trim().ToUpperInvariant() before blind-index hash — RegisterUserHandler.cs:29; verified by Duplicate_email_is_still_rejected_through_the_blind_index |
| PII at rest (email/displayName) | ✓ | User.Email/DisplayName [Encrypted]+[PersonalData]; sealed by interceptor — User.cs:18-28 |
| Tenant name leaking PII | ✓ | tenant.Name is neutral tenant-{id:N}, never email/displayName — RegisterUserHandler.cs:42-43; test Registration_does_not_store_the_plaintext_email_in_the_tenant_name |
| Abuse/DoS/enumeration via unthrottled signup | ✓ | MapRegisterUser is anonymous but explicitly `.RequireRateLimiting("auth")`; `PlatformContractTests.Register_endpoint_uses_the_auth_rate_limit_policy` proves a low auth limit returns 429. |
| Validation (email format, password length, displayName length, accepted terms version length) | ✓ | RegisterUserValidator.cs:9-22 with dotted error codes; terms version now uses user.accepted_terms_version.too_long instead of FluentValidation's default code. |

**Testy:** RegisterUserValidatorTests.Registration_validator_uses_stable_dotted_error_codes; RegisterUserValidatorTests.Registration_validator_accepts_valid_input; IdentityE2ETests.Register_login_refresh_rotation_reuse_detection_and_profile; AuthRobustnessTests.Duplicate_email_registration_is_conflict_and_creates_exactly_one_user; PiiColumnEncryptionTests.Email_and_display_name_are_ciphertext_at_rest_but_plaintext_through_the_api; PiiColumnEncryptionTests.Duplicate_email_is_still_rejected_through_the_blind_index; TenancyTests.Registration_provisions_a_tenant_and_the_token_carries_it; TenancyTests.Registration_does_not_store_the_plaintext_email_in_the_tenant_name
**Test gaps:** No remaining focused registration validator gap in this slice.

_Canonical write slice; anonymous signup now shares the auth limiter with login/refresh/reset/verify endpoints._

### Login (no-enumeration, timing-equalized verify, lockout) — ✅ correct
*Verify credentials and issue access+refresh tokens while leaking neither account existence nor timing.*

**Use cases:** Password authentication; Per-account brute-force lockout; First-admin bootstrap by configured email on login

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Unknown email — timing leak | ✓ | Always runs Argon2 Verify against a cached dummy hash; passwordValid gated on hasRealHash — LoginHandler.cs:34-58 |
| Unknown email — generic error | ✓ | Throws UnauthorizedException(auth.invalid_credentials) identical to wrong password — LoginHandler.cs:56-59,80 |
| GDPR-erased account (blank PasswordHash) authenticating | ✓ | hasRealHash=false so passwordValid can never be true even if dummy matches — LoginHandler.cs:49-53; test Erasure_tombstones...reLogin 401 |
| Email case/whitespace normalization | ✓ | Login hashes Email.Trim().ToUpperInvariant() before lookup (LoginHandler.cs:55-57); proven by PiiColumnEncryptionTests.Login_email_lookup_is_case_and_whitespace_insensitive_through_the_blind_index. |
| Locked-out account with CORRECT password | ✓ | LockoutEndUtc>now rejected with auth.locked_out before issuing — LoginHandler.cs:62-66; test Locks_out_after_threshold...rejects_correct_credentials |
| Lockout threshold + window expiry | ✓ | 5 strikes -> 15min lockout, counter reset on lockout/success — LoginHandler.cs:29-30,71-88; tests Lockout_expires..., A_successful_login_resets_the_failed_attempt_counter |
| Lockout counter persistence on failure | ✓ | SaveChangesAsync on the failure branch — LoginHandler.cs:78 (tracked entity -> xmin + audit) |
| Brute-force throttling at the edge | ✓ | Endpoint has RequireRateLimiting("auth") per-IP — LoginEndpoint.cs:22; proven by PlatformContractTests.Login_endpoint_uses_the_auth_rate_limit_policy. |
| Concurrent failed logins racing the counter | ✓ | Counter increment is a tracked SaveChanges (xmin + ConcurrencyRetryBehavior), so racing wrong-password requests re-run against the fresh count. Proven by AccountLockoutTests.Parallel_wrong_password_attempts_still_lock_the_account_after_the_threshold. |

**Testy:** AuthRobustnessTests.Unknown_email_and_wrong_password_return_identical_401_invalid_credentials; AccountLockoutTests.Locks_out_after_threshold_failures_and_rejects_correct_credentials; AccountLockoutTests.Parallel_wrong_password_attempts_still_lock_the_account_after_the_threshold; AccountLockoutTests.Lockout_expires_after_the_window_and_the_correct_password_works_again; AccountLockoutTests.A_successful_login_resets_the_failed_attempt_counter; PlatformContractTests.Login_endpoint_uses_the_auth_rate_limit_policy; IdentityE2ETests login leg; PiiColumnEncryptionTests erased-login-fails leg; PiiColumnEncryptionTests.Login_email_lookup_is_case_and_whitespace_insensitive_through_the_blind_index
**Test gaps:** No remaining focused login lockout/concurrency gap in this slice.

_Strong: dummy-hash timing equalization + hasRealHash gate is the right pattern; erased accounts can't authenticate by construction; known-wrong vs unknown-email response parity is pinned directly._

### Refresh-token rotation + reuse detection — ✅ correct
*Rotate refresh tokens one-time-use, and on replay of a consumed token revoke the entire family (theft response).*

**Use cases:** Silent session renewal; Detect a stolen/replayed refresh token and kill the session family; Reject tokens belonging to erased/missing accounts

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Unknown token hash | ✓ | FirstOrDefault null -> 401 auth.refresh_token_invalid — RefreshTokenHandler.cs:33-37 |
| Replay of consumed token | ✓ | ConsumedAt!=null -> revoke whole family (tracked SaveChanges so it is audited) -> 401 reused — RefreshTokenHandler.cs:39-58; test Refresh_reuse_revokes_whole_family_and_is_audited |
| Expired/revoked-but-not-consumed token | ✓ | IsActive(now) false -> 401 invalid — RefreshTokenHandler.cs:60-63; tests Expired_refresh_token_is_rejected_as_invalid and Explicitly_revoked_refresh_token_is_rejected_as_invalid_not_reuse |
| Token outliving a soft-deleted/erased account | ✓ | User loaded IgnoreQueryFilters; DeletedAt!=null -> 401 — RefreshTokenHandler.cs:65-75; test Refresh_is_rejected_when_the_account_is_soft_deleted |
| Token outliving a hard-deleted/missing user | ✓ | FirstOrDefault (not First) -> null -> clean 401, not 500 — RefreshTokenHandler.cs:69-72 |
| Parallel refresh of same token | ✓ | Exactly one 200, one 401, family ends fully revoked, no 5xx — proven by AuthRobustnessTests.Parallel_refresh_with_same_token_yields_one_winner_no_server_error (xmin serializes the consume) |
| Claims snapshot staleness | ✓ | Roles/permissions reloaded each refresh via UserAuthorizationQuery — RefreshTokenHandler.cs:92 |

**Testy:** IdentityE2ETests reuse-detection leg; AuthRobustnessTests.Refresh_reuse_revokes_whole_family_and_is_audited; AuthRobustnessTests.Parallel_refresh_with_same_token_yields_one_winner_no_server_error; AuthRobustnessTests.Expired_refresh_token_is_rejected_as_invalid; AuthRobustnessTests.Explicitly_revoked_refresh_token_is_rejected_as_invalid_not_reuse; SessionRevocationTests.Refresh_is_rejected_when_the_account_is_soft_deleted; SessionRevocationTests.Erasure_revokes_all_of_the_subjects_refresh_tokens
**Test gaps:** No remaining focused refresh-token rotation/reuse gap in this slice.

_Security-critical slice is thorough; the deliberate tracked-SaveChanges (not ExecuteUpdate) for the family revoke so the AuditInterceptor + xmin engage is correct and tested._

### Logout / session-family revocation — 🟢 minor-gaps
*User/admin kill-switch: revoke the whole rotation family of a presented refresh token.*

**Use cases:** Explicit sign-out; Revoke a stolen/stale session

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Identity from token not body | ✓ | UserId from ITenantContext.UserId at the endpoint; command carries it — LogoutEndpoint.cs:20-22 |
| Token not owned by caller / unknown token | ✓ | Silent idempotent no-op (non-enumerating) when token null or UserId mismatch — LogoutHandler.cs:24-27 |
| Family revoke audited + concurrency-safe | ✓ | Tracked SaveChanges (not ExecuteUpdate) -> AuditInterceptor + xmin — LogoutHandler.cs:30-39 |
| Unauthenticated logout | ✓ | RequireAuthorization + UnauthorizedException(auth.required) if no UserId — LogoutEndpoint.cs:20-25 |

**Testy:** SessionRevocationTests.Logout_revokes_the_session_family; SessionRevocationTests.Logout_with_another_users_refresh_token_is_a_silent_noop; SessionRevocationTests.Logout_with_an_unknown_refresh_token_is_a_silent_success
**Test gaps:** No remaining logout/session-family revocation gap in this slice.

_Logic is correct; the cross-user-no-op IDOR guard and unknown-token idempotency are both pinned by tests._

### Profile read (GetProfile /me) — ✅ correct
*Return the authenticated user's own profile via the no-tracking read factory.*

**Use cases:** Show current user info; Verify token-derived identity

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Identity from token | ✓ | UserId from ITenantContext; 401 if absent — GetProfileEndpoint.cs:19-21 |
| Tenant filter (no null-escape) | ✓ | Read factory applies tenant filter IsSystem\|\|TenantId==claim; no CurrentTenantId==null short-circuit — proven by TenantIsolationTests |
| Soft-deleted/erased user reading /me | ✓ | Read factory applies soft-delete filter (DeletedAt==null) -> 404; token already revoked on erasure anyway — PlatformDbContext.cs:101-103 |
| Encrypted columns decrypted on read | ✓ | Model-level converter decrypts on the read factory — test Email_and_display_name...plaintext_through_the_api |
| Missing user | ✓ | NotFoundException(user.not_found) — GetProfileHandler.cs:24 |

**Testy:** IdentityE2ETests profile leg; PiiColumnEncryptionTests decrypt-on-read leg; TenantIsolationTests.A_user_reading_through_the_tenant_filter_sees_only_their_own_tenant_data; TenantIsolationTests.Anonymous_caller_with_no_tenant_claim_is_rejected_not_granted_global_visibility; TenantIsolationTests.My_profile_is_not_returned_after_the_account_is_soft_deleted; SessionRevocationTests.Erased_user_access_token_can_no_longer_read_profile
**Test gaps:** No remaining focused profile-read gap in this slice.

_Canonical read slice._

### Roles & permissions (assign/revoke + token claim snapshot) — 🟢 minor-gaps
*Admin grants/revokes roles; the token bakes role+permission claims so endpoints gate without a per-request DB hit.*

**Use cases:** Admin role management; Permission-gated endpoint authorization; Claims refresh on re-auth/refresh

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Assign to non-existent user/role | ✓ | NotFoundException user.not_found / role.not_found — AssignRoleHandler.cs:18-24 |
| Idempotent re-assign | ✓ | Pre-check + UNIQUE(UserId,RoleId) catch DbUpdateException -> idempotent success — AssignRoleHandler.cs:26-42; test Concurrent_identical_role_grants_are_idempotent_not_a_500 |
| Revoke not-assigned role | ✓ | No-op when role missing or assignment absent — RevokeRoleHandler.cs:12-24 |
| Permission gating across users/tenants | ✓ | Admin endpoints RequirePermission(IdentityManageRoles); IgnoreQueryFilters for cross-tenant user lookup — AssignRoleEndpoint.cs:27, AssignRoleHandler.cs:18 |
| Claims become stale after grant | ✓ | Snapshot refreshed on next login/refresh (UserAuthorizationQuery) — documented + test AuthzTests re-login picks up new permission |
| Permission union across multiple roles | ✓ | Distinct join over RolePermissions — UserAuthorizationQuery.cs:32-35 |
| Revoke does not invalidate already-issued tokens | ◐ | By design: a revoked role still authorizes until the access token expires (snapshot model). Documented (UserAuthorizationQuery.cs:8-10) but no forced-revocation path; acceptable given short access-token lifetime, no test asserts the bounded staleness window on revoke. |

**Testy:** AuthzTests.Permission_gated_endpoint_rejects_non_admins_and_admins_can_grant_roles; AuthzTests.Refreshed_token_carries_role_changes_while_the_old_access_token_stays_a_snapshot; AuthzTests.Concurrent_identical_role_grants_are_idempotent_not_a_500; AuthzTests.Assign_role_returns_not_found_for_unknown_user_or_role; AuthzTests.Revoke_role_is_idempotent_and_removes_permission_only_from_new_tokens; AuthzTests.Revoke_role_is_a_tracked_delete_that_writes_audit
**Test gaps:** No remaining focused assign/revoke role gap in this slice.

_Snapshot semantics are intentional: revoke affects the next login/refresh token, not already-issued access tokens._

### Authorization seeding + admin bootstrap (IdentitySeeder + login-time grant) — 🟢 minor-gaps
*Idempotently seed PlatformPermissions, the system admin role with all permissions, and grant admin to configured emails.*

**Use cases:** Startup seeding of the authz model; Bootstrap the first admin without a seeded password; Catch admins who registered after startup (granted on login)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Host starts before migrations finish | ✓ | Broad catch + LogWarning, retried next boot (read-first would hit missing-table) — IdentitySeeder.cs:41-49 |
| Concurrent multi-host seeding | ✓ | Unique constraints make duplicate inserts no-ops; idempotent — IdentitySeeder.cs:18-22. Proven by IdentitySeederTests.Concurrent_startup_seeders_leave_one_complete_authorization_model. |
| Admin registers after startup | ✓ | EnsureConfiguredAdminAsync grants on login before token issue — LoginHandler.cs:90-140; test AuthzTests admin bootstrap on login |
| Soft-deleted admin email re-granted on restart | ✓ | AssignAdminsAsync excludes DeletedAt!=null so a deactivated admin is not silently re-granted — IdentitySeeder.cs:107-111. Proven by IdentitySeederTests.Startup_seeder_does_not_regrant_admin_to_a_soft_deleted_configured_admin. |
| Admin role not yet seeded at login time | ✓ | EnsureConfiguredAdminAsync returns early; next login catches it — LoginHandler.cs:129-133 |
| New permission added later | ✓ | Upsert loop adds missing permissions + links to admin role on each boot — IdentitySeeder.cs:56-90. Proven by IdentitySeederTests.Startup_seeder_relinks_missing_platform_permissions_to_the_admin_role_on_reboot. |
| Login-time admin grant must not re-grant a soft-deleted configured admin | ✓ | EnsureConfiguredAdminAsync has its own DeletedAt guard, matching startup seeding. Proven by IdentitySeederTests.Login_time_admin_bootstrap_does_not_grant_admin_to_a_soft_deleted_configured_admin. |

**Testy:** AuthzTests admin-token bootstrap (login-time grant); IdentitySeederTests.Startup_seeder_does_not_regrant_admin_to_a_soft_deleted_configured_admin; IdentitySeederTests.Login_time_admin_bootstrap_does_not_grant_admin_to_a_soft_deleted_configured_admin; IdentitySeederTests.Startup_seeder_relinks_missing_platform_permissions_to_the_admin_role_on_reboot; IdentitySeederTests.Concurrent_startup_seeders_leave_one_complete_authorization_model; AuditPiiEncryptionTests admin reveal (exercises admin role seeding)
**Test gaps:** No direct missing-table startup race test for the broad catch path.

_Two admin-grant paths (startup seeder + login) now share the same soft-delete guard; startup path and login-time bootstrap both have focused deleted-admin coverage._

### Audit trail reveal (admin forensics) + [erased] after shred — ✅ correct
*Admin reads a user's identity_audit_entries with PII decrypted, surfacing [erased] once the DEK is shredded.*

**Use cases:** Forensic review of account changes; Prove PII is crypto-shredded post-erasure

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Live key -> decrypt | ✓ | protector.TryReveal -> plaintext — GetUserAuditTrailHandler.cs:63-65; test ...decryptable_by_an_admin |
| Shredded key -> [erased] | ✓ | IsProtected && !reveal -> ErasedMarker [erased] — GetUserAuditTrailHandler.cs:68; test Erasing_the_subject_makes_audit_pii_unrecoverable |
| Non-PII / non-string values pass through | ✓ | Non-string returns raw text or null; non-protected string returns raw — GetUserAuditTrailHandler.cs:55-68 |
| Malformed/empty NewValues JSON | ✓ | Null/whitespace -> '{}'; non-object root -> empty dict — GetUserAuditTrailHandler.cs:41-44 |
| Admin-only, route id is target subject (not IDOR) | ✓ | RequirePermission(AuditRead); route userId is intentional admin-over-subject like role assignment — GetUserAuditTrailEndpoint.cs:10-27 |
| Read via read factory (no tracking) | ✓ | IReadDbContextFactory — GetUserAuditTrailHandler.cs:22 |

**Testy:** AuditPiiEncryptionTests.User_pii_is_enveloped_in_audit_and_decryptable_by_an_admin; AuditPiiEncryptionTests.Erasing_the_subject_makes_audit_pii_unrecoverable; AuditPiiEncryptionTests.Tenant_audit_requires_permission_and_does_not_cross_tenant; AuditPiiEncryptionTests.Platform_audit_requires_platform_permission_and_keeps_erased_pii_unreadable; AuditPiiEncryptionTests.Audit_trail_tolerates_non_object_new_values_json; IdentityE2ETests audit-row-count leg
**Test gaps:** No remaining focused Identity audit-read gap in this slice.

_Crypto-shred reveal is solid and end-to-end tested both pre- and post-erasure._

### Multi-tenancy provisioning + isolation — ✅ correct
*Registration provisions a tenant, stamps the user, and the token carries the tenant claim; reads are tenant-filtered with no null-escape.*

**Use cases:** Per-registrant tenant; Cross-tenant data isolation on the users table; Token-carried tenant context

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Anonymous registration with no tenant in context | ✓ | Provisions a new Tenant and sets TenantId via shadow property explicitly — RegisterUserHandler.cs:38-56 |
| Missing tenant claim = see everything | ✓ | Tenant filter IsSystem\|\|TenantId==claim, NO null short-circuit — PlatformDbContext.cs:97; test TenantIsolationTests |
| Cross-tenant read of another user | ✓ | Not expressible — identity always from token, no foreign-id route — TenantIsolationTests.A_user_reading...sees_only_their_own_tenant_data |
| Anonymous caller | ✓ | 401, never global visibility — TenantIsolationTests.Anonymous_caller...rejected |
| Tenant name PII leak | ✓ | Neutral tenant-{id:N} name — RegisterUserHandler.cs:42-43 |
| Distinct tenants per registration | ✓ | test Registration_provisions_a_tenant_and_the_token_carries_it |
| Cross-user-within-same-tenant isolation | ◐ | Acknowledged residual: no admin 'list users in my tenant' query exists yet, so within-tenant multi-user isolation is not exercised — documented in TenancyTests.cs:11-12. Each registration gets its own tenant today, so cross-user==cross-tenant in practice. |

**Testy:** TenancyTests.Registration_provisions_a_tenant_and_the_token_carries_it; TenancyTests.Registration_does_not_store_the_plaintext_email_in_the_tenant_name; TenancyTests.Registration_does_not_emit_a_dangling_location_header; TenantIsolationTests (cross-tenant isolation, client-supplied id ignored, soft-delete filter, forged tenant claim rejected)
**Test gaps:** No within-tenant multi-user isolation test (no listing query to exercise it — acknowledged)

_Isolation null-escape is provably closed; the only residual is the absence of a multi-user-per-tenant listing surface to test, which is documented._

### GDPR export + erasure (Identity ports) — ✅ correct
*Export account PII for portability; anonymize+soft-delete the user and revoke sessions on erasure (DEK shred kills ciphertext).*

**Use cases:** GDPR data portability; Right-to-erasure with crypto-shred; Session termination on erasure

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Erasure idempotency | ✓ | RequestErasure short-circuits if the subject key is already shredded (RequestErasureHandler.cs:24-27), and Identity eraser itself guards with WHERE DeletedAt==null (IdentityPersonalDataEraser.cs:28); proven by PiiColumnEncryptionTests.Erasure_can_be_replayed_without_restamping_the_identity_tombstone. |
| Email tombstone keeps UNIQUE(EmailHash) | ✓ | Deterministic erased-{id:N}@erased.invalid + its blind-index hash — IdentityPersonalDataEraser.cs:24-33 |
| Erased account fails login on credentials | ✓ | PasswordHash blanked, not just non-routable email — IdentityPersonalDataEraser.cs:34; test Erasure_tombstones_the_row_blanks_the_password |
| Sessions persist after erasure | ✓ | All outstanding refresh tokens revoked via set-based ExecuteUpdate (no PII) — IdentityPersonalDataEraser.cs:40-42; test Erasure_revokes_all_of_the_subjects_refresh_tokens |
| ExecuteUpdate bypasses audit/encryption interceptors | ✓ | Intentional — tombstone constants are not PII and must stay readable; documented — IdentityPersonalDataEraser.cs:14-16 |
| Export of a missing/erased user | ✓ | Exporter returns {profile: null} for a missing/soft-deleted user (read factory filters DeletedAt) — IdentityPersonalDataExporter.cs:21-26; post-erasure shape proven by GdprIntegrationTests.Export_after_erasure_returns_null_identity_profile. |
| Identity export profile shape | ✓ | IdentityPersonalDataExporter returns profile {email, displayName, locale, createdAt}; proven through GDPR fan-out by GdprIntegrationTests.Export_assembles_one_document_keyed_by_module_with_each_modules_section. |

**Testy:** SessionRevocationTests.Erasure_revokes_all_of_the_subjects_refresh_tokens; PiiColumnEncryptionTests.Erasure_tombstones_the_row_blanks_the_password_and_kills_the_ciphertext; PiiColumnEncryptionTests.Erasure_can_be_replayed_without_restamping_the_identity_tombstone; AuditPiiEncryptionTests.Erasing_the_subject_makes_audit_pii_unrecoverable; GdprIntegrationTests.Export_assembles_one_document_keyed_by_module_with_each_modules_section; GdprIntegrationTests.Export_after_erasure_returns_null_identity_profile
**Test gaps:** No remaining focused Identity GDPR export/erasure gap in this slice.

_Erasure is the most thoroughly tested Identity concern; live export, post-erasure export, and replay/idempotency paths are asserted._

### PII encryption backfill (legacy rows) — 🟢 minor-gaps
*One-time idempotent sealing of pre-encryption user rows (empty EmailHash) by computing the blind index and re-saving through the interceptors.*

**Use cases:** Migrate legacy plaintext rows to penc:v2 envelopes + blind index without downtime

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Fresh DB / no pending rows | ✓ | Early return when pending.Count==0 — PiiEncryptionBackfill.cs:31-34 |
| Schema not migrated / concurrent host already backfilled | ✓ | Broad catch (except OperationCanceled) + LogWarning, retried next boot — PiiEncryptionBackfill.cs:50-54 |
| Null DisplayName not force-touched | ✓ | Only marks DisplayName modified when non-null — PiiEncryptionBackfill.cs:42-44 |
| Filtered unique index tolerates empty-hash legacy rows | ✓ | UNIQUE(EmailHash) WHERE EmailHash<>'' so pre-backfill rows don't collide — User.cs:57 |

**Testy:** PiiColumnEncryptionTests.Startup_backfill_seals_legacy_plaintext_user_rows_and_computes_email_hash
**Test gaps:** No remaining focused PII encryption backfill gap in this slice.

_Logic mirrors the seeder's non-fatal retry pattern; the legacy-row path is now covered by raw SQL setup that bypasses the interceptors and then lets the hosted backfill seal the row on startup._

### Token issuance + password hashing (Security primitives) — ✅ correct
*HMAC-signed JWT access tokens with role/permission/tenant claims; CSPRNG refresh tokens stored SHA-256 hashed; Argon2id passwords.*

**Use cases:** Issue access tokens with authz snapshot; Generate + hash refresh tokens; Hash/verify passwords

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Refresh token stored as plaintext | ✓ | Only TokenHash (SHA-256 hex) persisted; raw returned once to client — TokenIssuer.cs:67-77, RefreshToken.cs:16; test TokenIssuerTests.Refresh_token_hash_is_deterministic_sha256_hex_and_never_the_raw_token |
| Tenant claim omitted when null | ✓ | Claim added only if tenantId!=null — TokenIssuer.cs:43-46 |
| Multi-valued role/permission claims | ✓ | One Claim per role + per permission — TokenIssuer.cs:50-51; test AuthzTests ClaimValues handles scalar+array |
| Password hashing rolled by hand | ✓ | Isopoh Argon2id (battle-tested) — PasswordHasher.cs:12-16 |
| Refresh token entropy | ✓ | RandomNumberGenerator.GetBytes(64) base64url — TokenIssuer.cs:69 |
| Access-token expiry drift | ✓ | ExpiresAt comes from IClock.UtcNow + JwtOptions.AccessTokenMinutes — TokenIssuer.cs:34-36; test TokenIssuerTests.Access_token_expiry_honors_configured_lifetime |
| JWT signing key fail-fast outside Dev | ✓ | JwtOptionsValidator enforces signing key at startup (Web building-block, per CLAUDE.md §8) — not in Identity but covers this primitive |

**Testy:** AuthzTests JWT claim decoding (role/permission); TenancyTests JWT tenant_id claim decode; TokenIssuerTests.Access_token_expiry_honors_configured_lifetime; TokenIssuerTests.Refresh_token_hash_is_deterministic_sha256_hex_and_never_the_raw_token
**Test gaps:** No remaining focused token-issuer primitive gap in this slice.

_Primitives delegate to battle-tested libs; refresh tokens are hashed at rest, never logged._

**Nekonzistence v oblasti (5):**
- Rate-limiting alignment: RegisterUserEndpoint now applies `.RequireRateLimiting("auth")` like login/refresh/reset/verify; `Register_endpoint_uses_the_auth_rate_limit_policy` covers the low-limit 429 path.
- Admin-grant soft-delete guard divergence: IdentitySeeder.AssignAdminsAsync filters DeletedAt==null (IdentitySeeder.cs:109) to avoid re-granting a deactivated admin, but the login-time grant LoginHandler.EnsureConfiguredAdminAsync (LoginHandler.cs:118-140) has no such guard. Unreachable in practice (an erased admin has a blanked PasswordHash and fails auth before the grant runs), but the two paths that grant the same role apply different deactivation rules.
- Refresh-token family revoke uses tracked SaveChanges in BOTH RefreshTokenHandler.cs:45-54 and LogoutHandler.cs:30-39 (so audit+xmin engage), but the GDPR eraser revokes tokens with a set-based ExecuteUpdate (IdentityPersonalDataEraser.cs:40-42). The divergence is justified in-comment (refresh_tokens carry no PII and the erasure path deliberately bypasses interceptors), so this is intentional code-vs-code drift, not a bug — noted for completeness.
- Doc-vs-code (benign): AuthRobustnessTests.cs:16 XML comment references a 'users (NormalizedEmail)' column, but the User entity has no NormalizedEmail — the column is EmailHash (User.cs:23, the test SQL itself correctly uses EmailHash). Stale comment only.
- Validator caps Email at 256 / DisplayName at 128 (RegisterUserValidator.cs:11,20) while the entity columns are sized 1024 (envelope size) (User.cs:49,52) and DisplayName 1024 — intentional (plaintext input bound vs ciphertext storage bound), but the two limits are not co-located so a future change could drift.


---

## Billing — credit ledger & money

### Credit account provisioning (EnsureCreditAccount + ProvisionCreditAccountHandler) — ✅ correct
*Idempotently create one zero-balance wallet per user, driven by Identity's UserRegistered event.*

**Use cases:** New user registers -> Billing auto-provisions a credit account via the cross-module event (worker, inbox-deduped); Any internal flow can call EnsureCreditAccountCommand to guarantee an account exists before granting

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Concurrent provisioning race for the same user (duplicate accounts) | ✓ | UNIQUE index on CreditAccount.UserId (CreditAccount.cs:44) + AnyAsync pre-check then catch DbUpdateException non-concurrency and detach the losing Added entity (EnsureCreditAccountCommand.cs:16-29). Proven 8-way by LedgerBackstopTests.EV5 and by top-up's nested command race test. |
| Event redelivery (same UserRegistered twice) | ✓ | Wolverine inbox dedup + EnsureCreditAccount is an idempotent no-op; ProvisionCreditAccountHandler.cs:15-16 is a thin public shell. |
| DbUpdateConcurrencyException (xmin) on provisioning insert | – | An insert has no xmin conflict; the when-filter deliberately excludes DbUpdateConcurrencyException (EnsureCreditAccountCommand.cs:23) so only the unique-violation race is swallowed. |

**Testy:** LedgerBackstopTests.EV5_concurrent_account_provisioning_yields_exactly_one_account; LedgerBackstopTests.EV5_existing_account_provisioning_is_a_noop; CrossModuleEventTests (EV-1, register -> account provisioned)
**Test gaps:** No remaining focused account-provisioning idempotency gap in this slice.

_Credit account creation now has one canonical slice: both UserRegistered handling and late-arriving top-up grants dispatch EnsureCreditAccountCommand, which owns the UNIQUE(UserId) race recovery._

### Credit top-up (CreditTopUp) — ✅ correct
*Idempotent, client-key-namespaced grant that mints a bucket + balanced ledger entry and bumps posted/available atomically via the outbox.*

**Use cases:** Stripe saga / subscription grant / webhook router dispatch the internal CreditTopUpCommand after a real payment; Admin hand-grants credits over HTTP POST /billing/credits/topup (gated billing.manage); Tests seed a starting balance via the same grant primitive

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Same idempotency key applied twice / raced | ✓ | Pre-check AnyAsync (CreditTopUpHandler.cs:44) + UNIQUE(AccountId,IdempotencyKey) as final guard; loser catches DbUpdateException, re-reads real posted, returns AlreadyApplied (CreditTopUpHandler.cs:95-109). Proven by BillingLedgerTests.Top_up_with_the_same_idempotency_key_credits_exactly_once. |
| Idempotency key collision across accounts | ✓ | Idempotency scoped per-account via composite UNIQUE(AccountId,IdempotencyKey) (CreditEntry.cs:58). Proven by BillingLedgerTests.Idempotency_keys_are_scoped_per_account_not_globally. |
| Client key collides with structured system keys (purchase:/sub-invoice:) | ✓ | Endpoint namespaces the client key as client:{key} (CreditTopUpEndpoint.cs:25-26) so it can never collide with system keys in the per-account UNIQUE space. |
| Account does not yet exist when top-up arrives (event ordering) | ✓ | CreditTopUp dispatches EnsureCreditAccountCommand and reloads the account, so the single provisioning slice owns the UNIQUE(UserId) create/race recovery. Proven by BillingLedgerTests.Top_up_creates_credit_account_when_provisioning_event_has_not_run_yet and Top_up_recovers_when_another_writer_creates_the_missing_credit_account_first. |
| Overflow of long posted/available | ✓ | Pre-check rejects with credit.amount.too_large (CreditTopUpHandler.cs:52-55); validator caps single amount at 1e9 (CreditTopUpValidator.cs:12,18); DB CHECK is backstop. Proven by LedgerBackstopTests.BL11 + CreditAmountBoundsTests. |
| Non-admin user self-minting credits over HTTP | ✓ | Endpoint requires billing.manage permission (CreditTopUpEndpoint.cs:30-31). Proven by CreditTopUpAuthorizationTests.Public_topup_endpoint_rejects_a_non_admin_user. |
| Negative / zero amount | ✓ | CreditTopUpValidator.cs:17 GreaterThan(0). Proven by CreditAmountBoundsTests. |
| BucketExpiryDays <= 0 | ✓ | Validator GreaterThan(0).When(HasValue) (CreditTopUpValidator.cs:22-24); null = non-expiring bucket. Proven by LedgerLifecycleTests.Non_expiring_topup_bucket_is_not_touched_by_expiry_sweep. |
| Cumulative overflow across many sub-MaxAmount top-ups | ✓ | Per-call overflow pre-check (CreditTopUpHandler.cs:52) compares against current account.Posted/Available, so it catches accumulation, not just a single oversized amount; DB CHECK is the final backstop. |

**Testy:** BillingLedgerTests.Top_up_with_the_same_idempotency_key_credits_exactly_once; BillingLedgerTests.Top_up_creates_credit_account_when_provisioning_event_has_not_run_yet; BillingLedgerTests.Top_up_recovers_when_another_writer_creates_the_missing_credit_account_first; BillingLedgerTests.Idempotency_keys_are_scoped_per_account_not_globally; LedgerLifecycleTests.Non_expiring_topup_bucket_is_not_touched_by_expiry_sweep; LedgerBackstopTests.BL11_topup_that_would_overflow_is_rejected_and_balance_unchanged; CreditAmountBoundsTests (positive/cap bounds, unit); CreditTopUpAuthorizationTests (admin-only HTTP gate, both directions)
**Test gaps:** No remaining focused credit top-up gap in this slice.

_CreditsToppedUpIntegrationEvent is published in the same outbox transaction (CreditTopUpHandler.cs:82-93). Solid._

### Reserve credits (atomic debit guard) — ✅ correct
*Pessimistic reservation via a single conditional ExecuteUpdate (WHERE Available >= amount) that locks the row and prevents double-spend without raw SQL.*

**Use cases:** A product action reserves credits before doing work; gets a reservationId + remaining available; Concurrent reservations serialize at the DB so the sum of holds never exceeds the balance

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Concurrent reservations draining the balance (double-spend) | ✓ | Atomic ExecuteUpdate guard (ReserveCreditsHandler.cs:37-41); rows==0 -> 422. Proven by BillingConcurrencyTests (20 parallel, exactly 10 succeed, available never < 0). |
| Insufficient available balance | ✓ | debited==0 -> BusinessRuleException credit.insufficient_balance -> 422 with NO hold/entry written (the guard runs before the hold insert). Proven by LedgerLifecycleTests.Reserving_more_than_available_is_rejected_with_no_side_effects. |
| Account not found | ✓ | FirstOrDefault accountId == Guid.Empty -> NotFoundException (ReserveCreditsHandler.cs:29-32); test Reserve_without_credit_account_returns_not_found. |
| Negative/zero/oversized amount | ✓ | ReserveCreditsValidator.cs:17-18. Proven by CreditAmountBoundsTests. |
| HoldMinutes <= 0 / default/custom | ✓ | Validator GreaterThan(0).When(HasValue) (ReserveCreditsValidator.cs:19-21); null -> DefaultHoldMinutes 15 (ReserveCreditsHandler.cs:19,54); custom value sets ExpiresAt (Reserve_honors_custom_hold_minutes). |
| Hold + ledger entry must be atomic with the debit | ✓ | Explicit BeginTransaction wraps the ExecuteUpdate debit + hold + Reservation entry, commit at ReserveCreditsHandler.cs:34,71-72. |
| No upper bound on number/total of simultaneous active holds per account | – | By design availability is the only cap; pending can grow only as far as available allows, so no separate hold-count limit is needed. |

**Testy:** BillingConcurrencyTests.Concurrent_reservations_never_exceed_balance; LedgerLifecycleTests.Reserving_more_than_available_is_rejected_with_no_side_effects; CreditAmountBoundsTests (Reserve bounds); ReserveCreditsTests.Reserve_without_credit_account_returns_not_found; ReserveCreditsTests.Reserve_honors_custom_hold_minutes
**Test gaps:** No remaining focused reserve-credits gap in this slice.

_This is the money-critical path and it is the best-covered. Reservation entry uses IdempotencyKey reserve:{holdId}; the holdId is fresh per reserve so re-reservations are distinct by design._

### Confirm spend (FIFO bucket draw) — 🟢 minor-gaps
*Confirm an active reservation into a posted spend, drawing buckets soonest-to-expire; keeps available = posted - pending.*

**Use cases:** After work completes, confirm the reservation to permanently consume the held credits; Idempotent re-confirm returns the already-posted state

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Concurrent double-confirm of one reservation | ✓ | hold.Status==Confirmed early return (ConfirmSpendHandler.cs:33-36); xmin on tracked hold/account serializes; UNIQUE spend:{holdId} is the final guard, loser catches DbUpdateException and returns committed state (ConfirmSpendHandler.cs:97-111). Proven by BillingLedgerTests (10 parallel, exactly one Spend, posted -1 once). |
| Confirming an expired or non-active reservation | ✓ | Status != Active OR ExpiresAt <= now -> BusinessRuleException credit.reservation_not_active (ConfirmSpendHandler.cs:41-44). Proven by ConfirmSpendTests.Confirm_released_reservation_is_rejected_without_spend and ConfirmSpendTests.Confirm_expired_but_unswept_reservation_is_rejected_without_spend. |
| FIFO ordering soonest-to-expire, non-expiring last | ✓ | OrderBy(ExpiresAt==null) ThenBy(ExpiresAt) ThenBy(CreatedAt) (ConfirmSpendHandler.cs:47-49). Proven by LedgerLifecycleTests.Confirm_draws_buckets_soonest_to_expire_first. |
| Account / reservation not found | ✓ | NotFoundException credit.account_not_found / credit.reservation_not_found (ConfirmSpendHandler.cs:26-31). |
| Reservation belongs to another user (IDOR) | ✓ | Hold looked up by h.AccountId == account.Id where account is resolved from token UserId (ConfirmSpendHandler.cs:29-34); a foreign reservationId resolves to NotFound. Proven by ConfirmSpendTests.Confirm_foreign_reservation_is_a_404_and_does_not_spend. |
| Buckets exhausted before the hold amount is fully drawn (remaining > 0 after loop) | ✓ | The FIFO loop tracks `remaining`; if active buckets cannot fully cover the hold, the handler throws credit.bucket_underflow before writing Spend/account changes (ConfirmSpendHandler.cs:46-73). Proven by ConfirmSpendTests.Confirm_fails_loudly_when_buckets_no_longer_cover_the_hold. |

**Testy:** BillingLedgerTests.Confirming_a_reservation_is_exactly_once_under_concurrency; LedgerLifecycleTests.Confirm_draws_buckets_soonest_to_expire_first; LedgerLifecycleTests.Ledger_invariants_hold_after_a_mixed_run; ConfirmSpendTests.Confirm_expired_but_unswept_reservation_is_rejected_without_spend; ConfirmSpendTests.Confirm_foreign_reservation_is_a_404_and_does_not_spend; ConfirmSpendTests.Confirm_fails_loudly_when_buckets_no_longer_cover_the_hold
**Test gaps:** No remaining confirm-spend correctness gap in this slice; broader ledger-entry immutability is tracked in the ledger read section.

_The previous undershoot soft spot is now defended in code and covered by a direct integration test._

### Release hold — ✅ correct
*Cancel an active reservation, restoring availability; idempotent.*

**Use cases:** Work aborted -> release the reservation and get credits back as available; Double-release is a safe no-op

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Releasing an already-resolved hold (released/confirmed/expired) | ✓ | Status != Active -> returns current available unchanged (ReleaseHoldHandler.cs:28-31). Proven idempotent by LedgerLifecycleTests.Releasing_a_hold_restores_availability_and_is_idempotent and ReleaseHoldTests.Release_after_confirm_does_not_restore_spent_credits. |
| Concurrent double-release | ✓ | xmin on tracked entities + UNIQUE release:{holdId}; loser catches DbUpdateException and returns committed available (ReleaseHoldHandler.cs:52-67). |
| Account / reservation not found / IDOR | ✓ | NotFoundException both cases; hold scoped to token-derived account (ReleaseHoldHandler.cs:21-26); test Release_unknown_reservation_is_a_404. |
| Releasing a CONFIRMED hold tries to double-restore availability | ✓ | The Status != Active guard (ReleaseHoldHandler.cs:28) covers Confirmed too, so a confirmed reservation cannot be released back into availability; test Release_after_confirm_does_not_restore_spent_credits. |

**Testy:** LedgerLifecycleTests.Releasing_a_hold_restores_availability_and_is_idempotent; LedgerBackstopTests.PL2 (release flips hold Active->Released, audit assertion); ReleaseHoldTests.Release_unknown_reservation_is_a_404; ReleaseHoldTests.Release_after_confirm_does_not_restore_spent_credits
**Test gaps:** No remaining focused release-hold gap in this slice.

_Symmetric with confirm; both rely on the same xmin + UNIQUE-key idempotency pattern._

### Expire credits sweep — 🟢 minor-gaps
*Cron-dispatched sweep that materializes lapsed holds (restore availability) and expired buckets (destroy free credits) into the ledger, idempotently and per-account isolated.*

**Use cases:** Jobs host runs BillingExpireCreditsJob on a cron to reconcile lapsed holds/buckets into the ledger; Keeps the stored projection eventually consistent with the live availability query

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Expired bucket whose Remaining backs an active hold (would drive available negative) | ✓ | Skip the bucket fully when bucket.Remaining > account.Available (ExpireCreditsHandler.cs:69-72); expired on a later sweep once the hold resolves. Proven by LedgerLifecycleTests.Expiring_a_bucket_that_backs_an_active_reservation_does_not_crash_or_go_negative. |
| Sweep run twice (idempotency) | ✓ | UNIQUE expire-hold:{id} / expire-bucket:{id} keys + the hold is no longer Active and bucket Remaining==0 on re-scan (ExpireCreditsHandler.cs:48,83). Proven by LedgerLifecycleTests BL-9 second sweep. |
| One account's persistence failure aborting the whole sweep | ✓ | Per-account try/catch DbUpdateException (non-concurrency) clears the change tracker and continues (ExpireCreditsHandler.cs:95-103); explicit fix over prior abort-all bug. Proven by LedgerLifecycleTests.Expiry_sweep_isolates_one_accounts_persistence_failure_and_continues_with_others. |
| Concurrency conflict (xmin) during sweep | ✓ | Deliberately NOT caught (comment ExpireCreditsHandler.cs:99-101) -> bubbles to ConcurrencyRetryBehavior which retries the whole sweep; expire-*:{id} keys dedup already-applied accounts. |
| Lapsed hold restored | ✓ | Active && ExpiresAt<=now -> Status Expired, Release-type credit entry expire-hold:{id}, available += / pending -= (ExpireCreditsHandler.cs:33-54). Proven by BL-9. |
| Multiple accounts in one sweep | ✓ | The command collects only candidate account ids from lapsed holds / expired buckets and loops those accounts (ExpireCreditsHandler.cs:25-36); proven by LedgerLifecycleTests.Expiry_sweep_processes_multiple_accounts_in_one_run. |
| Accounts with no expired work | ✓ | Not scanned: candidate ids come from CreditHolds/CreditBuckets via EF Union, so unrelated accounts are skipped before the per-account loop. |
| Expired bucket has remaining LESS than available but a different bucket is partly reserved | ✓ | The skip guard is per-bucket against account.Available, and account.Available is recomputed in memory after each hold restore within the same account iteration, so ordering is consistent within a sweep. |
| Non-expiring bucket (ExpiresAt null) | ✓ | Expiry query requires ExpiresAt != null and ExpiresAt <= now (ExpireCreditsHandler.cs:57); proven by LedgerLifecycleTests.Non_expiring_topup_bucket_is_not_touched_by_expiry_sweep. |

**Testy:** LedgerLifecycleTests.Expiry_sweep_restores_lapsed_holds_destroys_expired_buckets_and_is_idempotent; LedgerLifecycleTests.Expiring_a_bucket_that_backs_an_active_reservation_does_not_crash_or_go_negative; LedgerLifecycleTests.Non_expiring_topup_bucket_is_not_touched_by_expiry_sweep; LedgerLifecycleTests.Expiry_sweep_processes_multiple_accounts_in_one_run; LedgerLifecycleTests.Expiry_sweep_isolates_one_accounts_persistence_failure_and_continues_with_others
**Test gaps:** No remaining focused expiry-sweep correctness gap in this slice.

_Logic is careful and covers the prior abort-all bug. The sweep now bounds per-account work to accounts that actually have lapsed holds or expired buckets._

### Get credit balance (read) — ✅ correct
*Return the authoritative stored posted/available projection for the caller's account.*

**Use cases:** UI / client reads the user's spendable balance; the shown available equals what a reservation will allow

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Returns stored projection, not a live ledger recompute | ✓ | Selects stored Posted/Pending/Available via the no-tracking read factory (GetCreditBalanceHandler.cs:20-23). Proven by LedgerBackstopTests.BL10 (skewed stored value is what is returned) and CreditBalanceTests.Balance_is_token_scoped_and_tracks_reserve_release_confirm (Posted remains stable while Pending lowers Available). |
| Account not found | ✓ | FirstOrDefault null -> NotFoundException credit.account_not_found (GetCreditBalanceHandler.cs:24). Proven directly at query level and through the HTTP endpoint by CreditBalanceTests.Missing_account_returns_a_clear_not_found_from_the_endpoint. |
| Identity from token not route/body | ✓ | Endpoint uses tenant.UserId (GetCreditBalanceEndpoint.cs:19-21); no client-supplied subject id. CreditBalanceTests.Balance_is_token_scoped_and_tracks_reserve_release_confirm proves Alice cannot read Bob's projection by passing any body/route id because there is none. |
| Expired-but-unswept holds make available slightly conservative | ✓ | Documented intentional (GetCreditBalanceHandler.cs:10-11): available stays conservative until the sweep reconciles; never over-reports spendable. |

**Testy:** CreditBalanceTests.Fresh_user_gets_a_provisioned_zero_balance; Missing_account_returns_a_clear_not_found_from_the_query; Missing_account_returns_a_clear_not_found_from_the_endpoint; Balance_is_token_scoped_and_tracks_reserve_release_confirm; LedgerBackstopTests.BL10_balance_read_returns_the_stored_projection_not_a_recompute
**Test gaps:** No remaining focused gap for the balance read slice.

_Pure query, no transaction; uses the read factory per the platform law._

### Append-only double-entry ledger + projection invariant — ✅ correct
*Immutable credit_entries as the source of truth; account posted/pending/available is a verified projection with DB CHECK backstops.*

**Use cases:** Every mutation appends a balanced, typed, idempotency-keyed entry (Topup/Reservation/Spend/Release/Expiry); Audit/AML retention: ledger rows are never updated or deleted

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Raw negative write to projection columns | ✓ | Three CHECK constraints ck_credit_accounts_*_non_negative (CreditAccount.cs:35-37). Proven by LedgerBackstopTests.BL5 (23514). |
| posted == sum(bucket.Remaining) and available == posted - pending after mixed run | ✓ | Verified by LedgerLifecycleTests.Ledger_invariants_hold_after_a_mixed_run (BL-12); the test docstring corrects the naive double-entry expectation to posted == sum(bucket.Remaining). |
| Audit Update rows record only changed columns + enum as string | ✓ | AuditInterceptor on tracked saves; LedgerBackstopTests.PL2 asserts Released string present, immutable Amount absent. |
| ExecuteUpdate debit bypasses AuditInterceptor + xmin | ✓ | By design (CLAUDE.md): the credit_entries ARE the audit for the debit guard; the Reservation entry records the debit. Acceptable per documented caveat. |
| Ledger entry never updated/deleted | ✓ | No UPDATE/DELETE on CreditEntry anywhere; entity documented immutable (CreditEntry.cs:24-27). BillingDbContext rejects tracked Modified/Deleted CreditEntry rows before SaveChanges, so module code must append a compensating entry instead. Erasure anonymizes rather than deletes (per module IExportPersonalData, outside this slice). |
| Owner reads ledger entries paged, scoped and newest-first | ✓ | GetCreditLedgerHandler.cs:29-38 owner-scopes by AccountId and orders by CreatedAt desc; covered by CreditLedgerReadTests.Ledger_entries_are_paged_and_scoped_to_the_token_owner. |

**Testy:** LedgerLifecycleTests.Ledger_invariants_hold_after_a_mixed_run; LedgerLifecycleTests.Transaction_id_groups_the_expected_reservation_lifecycle_entries; LedgerBackstopTests.BL5 / PL2; LedgerBackstopTests.Credit_entries_are_append_only_even_through_the_billing_db_context; CreditLedgerReadTests.Ledger_entries_are_paged_and_scoped_to_the_token_owner
**Test gaps:** No remaining focused append-only ledger immutability gap in this slice.

_Strong defence-in-depth: app-level append-only guard + DB CHECK + per-account UNIQUE idempotency. The projection-vs-ledger reconciliation is only ever asserted in tests, not by a runtime reconciliation job for credits (Stripe has one; the credit ledger relies on correct arithmetic + the expiry sweep)._

**Nekonzistence v oblasti (1):**
- Reservation ledger entry uses Type=Reservation as a Debit that only moves available->pending without reducing Posted, while Spend is a separate Debit that reduces Posted (ReserveCreditsHandler.cs:59-69 + ConfirmSpendHandler.cs:63-81). This is intentional and documented in LedgerLifecycleTests BL-12, but it means the naive double-entry rule posted == sum(credits) - sum(debits) does NOT hold — a subtle convention captured only in a test comment, not in the CreditEntry entity docs.


---

## Billing — Stripe commerce, saga, subscriptions

### Package catalogue (CRUD) + purchasable listing — 🟢 minor-gaps
*Admin-managed credit-package catalogue (create/update) and a buyer-facing active-only listing.*

**Use cases:** Admin (billing.manage) creates a package mapping CreditAmount+Price to a Stripe Price id (CreateCreditPackageEndpoint.cs); Admin updates a package (name/amount/price/expiry/active/priceId) (UpdateCreditPackageHandler.cs); Buyer lists only Active packages, cheapest first (ListCreditPackagesHandler.cs:18-22)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Non-admin tries to create/update a package | ✓ | Both endpoints .RequirePermission(PlatformPermissions.BillingManage) (CreateCreditPackageEndpoint.cs:33, UpdateCreditPackageEndpoint.cs:26) |
| Update of a non-existent package | ✓ | UpdateCreditPackageHandler.cs:16-17 throws NotFoundException billing.package_not_found |
| Negative price / non-positive credit amount / non-positive expiry | ✓ | CreateCreditPackageValidator.cs / UpdateCreditPackageValidator.cs enforce GreaterThan(0)/GreaterThanOrEqualTo(0) with error codes |
| Admin edits amount/price of a package with in-flight purchases | ✓ | Saga snapshots amounts at purchase time (CreditPurchaseSaga carries CreditAmount); documented at UpdateCreditPackageHandler.cs:8-9 — live purchases unaffected |
| Inactive package shown to buyers | ✓ | ListCreditPackagesHandler.cs:18 filters p.Active; PurchaseCreditPackageHandler.cs:35-38 also rejects inactive on checkout (billing.package_inactive) |
| Tenant payment gateway missing on checkout | ✓ | PurchaseCreditPackageHandler resolves the tenant gateway and maps PaymentGatewayUnavailableException to a BusinessRuleException (payment.gateway_not_configured); proven by PurchaseCreditPackageTests.Package_checkout_fails_when_tenant_gateway_is_not_configured. |

**Testy:** BillingCommerceTests.Package_purchase_completes_end_to_end_via_checkout_webhook_and_saga (admin create + buyer list + checkout); AdminPackageCatalogueTests.Admin_package_list_requires_billing_manage_permission; UpdateCreditPackageTests.Update_package_requires_billing_manage_permission; UpdateCreditPackageTests.Update_package_returns_not_found_for_unknown_or_foreign_package; UpdateCreditPackageTests.Update_package_can_disable_public_visibility_and_writes_audit_entry; UpdateCreditPackageTests.Update_package_does_not_rewrite_started_purchase_snapshot; PurchaseCreditPackageTests.Package_checkout_rejects_missing_or_disabled_package_before_provider_call
**Test gaps:** No remaining focused catalogue CRUD/visibility/auth gap in this slice. The old billing.package.price_not_configured branch no longer exists in the provider-agnostic package checkout path; checkout now validates the tenant payment gateway instead.

_Catalogue CRUD is correct and guarded: admin writes require billing.manage, public listing excludes inactive packages, and checkout rejects inactive/missing packages before provider work._

### Package purchase checkout + CreditPurchaseSaga — ✅ correct
*Accept a package purchase, create a Stripe Checkout session, and drive credit grant via the canonical self-healing saga.*

**Use cases:** Buyer POSTs /billing/packages/{id}/checkout → Stripe session + outboxed CreditPurchaseStarted (PurchaseCreditPackageHandler.cs); Worker materializes saga Pending, schedules abandon timeout (CreditPurchaseSaga.Start); checkout.session.completed (paid) → CreditPurchaseConfirmed → idempotent CreditTopUp → saga Completed + CreditPurchaseCompletedIntegrationEvent; Buyer polls GET /billing/purchases/{id} for status (GetCreditPurchaseHandler.cs)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Unauthenticated checkout | ✓ | PurchaseCreditPackageEndpoint.cs:21-22 throws auth.required when tenant.UserId null; identity from token, never body |
| Duplicate confirmation / saga replay / Stripe webhook redelivery | ✓ | Grant always via CreditTopUp key purchase:{Id} (CreditPurchaseSaga.cs:62-63); Status==Completed short-circuits the event re-publish (cs:65-68). Proven by BillingCommerceTests.Duplicate_paid_purchase_webhook_grants_credits_exactly_once. |
| Checkout abandoned (no payment) | ✓ | CreditPurchaseTimeout flips Pending→Abandoned (CreditPurchaseSaga.cs:85-97); nothing charged/granted |
| Late payment AFTER abandon/timeout (saga row deleted) | ✓ | static NotFound(CreditPurchaseConfirmed) still grants idempotently (CreditPurchaseSaga.cs:103-106); timeout re-arriving after Pending no-ops (cs:87-90) |
| Confirmation message dead-letters (grant never lands) on a PAID purchase | ✓ | Reconcile Pass 3 re-publishes CreditPurchaseConfirmed only when Stripe payment_status is paid/no_payment_required (ReconcileStripeHandler.cs:137-175) |
| Delayed payment method: completed fires while unpaid | ✓ | ProcessStripeEventHandler.IsPaid gate (cs:60,100-101) skips grant on unpaid; async_payment_succeeded later grants |
| Malformed/forged session metadata (missing purchase_id/user_id/amount<=0) | ✓ | PublishPurchaseConfirmed returns without granting on parse failure (ProcessStripeEventCommand.cs:106-116). Proven by BillingCommerceTests.Paid_package_checkout_with_missing_purchase_metadata_does_not_grant_credits. |
| Purchase status read by a different user (IDOR) | ✓ | GetCreditPurchaseHandler.cs:21-26 filters UserId==query.UserId, 404 otherwise (no existence oracle); also RLS (IUserOwned). Proven by PurchaseStatusTests.Purchase_status_is_owner_scoped_and_moves_through_pending_abandoned_completed. |
| Saga row read before Worker materializes it | ✓ | Documented async appearance (GetCreditPurchaseHandler.cs:8-13); 404 until present |
| Concurrent saga state writes | ✓ | Wolverine Saga.Version optimistic concurrency configured (CreditPurchaseSaga.cs:122-123) |

**Testy:** BillingCommerceTests.Package_purchase_completes_end_to_end_via_checkout_webhook_and_saga; BillingCommerceTests.Duplicate_paid_purchase_webhook_grants_credits_exactly_once; BillingCommerceTests.Paid_package_checkout_with_missing_purchase_metadata_does_not_grant_credits; BillingCommerceTests.Unpaid_checkout_session_does_not_grant_credits; BillingCommerceTests.Async_payment_succeeded_grants_credits_on_settlement; BillingCommerceTests.Saga_timeout_abandons_pending_purchase_and_late_confirmation_still_grants; PurchaseStatusTests.Purchase_status_is_owner_scoped_and_moves_through_pending_abandoned_completed; StripeReconcileTests.Stuck_PAID_purchase_whose_confirmation_dead_lettered_is_regranted; StripeReconcileTests.Stuck_UNPAID_purchase_is_NOT_regranted
**Test gaps:** No remaining focused package purchase/saga gap in this slice.

_The strongest-covered feature: happy path, unpaid gate, async settlement, abandon+late, and dead-letter regrant all tested. Deliberate no-MarkCompleted design (row = purchase record) is sound._

### Stripe webhook ingest (signed, atomic, idempotent) — ✅ correct
*Verify Stripe signature against the raw body, persist the event id under a UNIQUE key, and enqueue durable processing in one transaction.*

**Use cases:** Stripe POSTs a signed event → 200 + stripe_events row + outboxed ProcessStripeEventMessage (StripeWebhookEndpoint.cs); Worker drains ProcessStripeEventMessage → idempotent router

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Invalid signature | ✓ | EventUtility.ConstructEvent throws → 400 before any persist (StripeWebhookEndpoint.cs:44-51) |
| Duplicate event id (Stripe retry) | ✓ | AnyAsync pre-check returns 200 no-op (cs:55-60); UNIQUE race caught as PostgresErrorCodes.UniqueViolation → 200 (cs:78-89) |
| Transient/non-unique persist failure (e.g. column overflow) | ✓ | Only the UNIQUE violation is ACKed; any other DbUpdateException propagates → 500 → Stripe redelivers (cs:78-89, comment 84-88) |
| Webhook rate-limited / burst on retry | ✓ | .DisableRateLimiting() so Stripe is never 429'd (cs:94) |
| Row persisted but message not enqueued window | ✓ | SaveChangesAndFlushMessagesAsync commits row + envelope atomically (cs:72-76) |

**Testy:** StripeWebhookTests.Valid_signed_event_is_accepted_and_enqueues_durable_ledger_work; StripeWebhookTests.Redelivering_the_same_signed_event_is_exactly_once; StripeWebhookTests.A_non_unique_persist_failure_is_not_acked_so_stripe_will_retry; StripeWebhookTests.Bad_signature_is_rejected_and_nothing_is_persisted; StripeWebhookTests.Non_empty_webhook_secret_rejects_a_body_signed_with_the_default_empty_secret
**Test gaps:** No remaining focused Stripe webhook ingest gap in this slice.

_Ingest invariants (200/row/enqueue/exactly-once/500-on-non-unique/400-on-bad-sig) are all tested deterministically. The empty-secret test caveat is documented in the test class header._

### Stripe event router (ProcessStripeEvent) — 🟢 minor-gaps
*Idempotently refetch each event from Stripe and route checkout/subscription/invoice/generic-metadata events to the right command.*

**Use cases:** checkout.session.completed|async_payment_succeeded (purchase_type=package, paid) → CreditPurchaseConfirmed; customer.subscription.created|updated|deleted → UpsertSubscriptionFromStripe; invoice.paid|invoice.payment_succeeded → GrantSubscriptionCredits; invoice.payment_failed → subscription mirror refresh; any other event carrying user_id+credit_amount metadata → direct CreditTopUp (key = event id)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Already-processed or missing event row | ✓ | Early return if record null or ProcessedAt set (ProcessStripeEventCommand.cs:41-45) |
| Webhook payload order / stale payload | ✓ | Always refetches via gateway.GetEventAsync (cs:47) — current object state, never the delivered payload |
| Non-package or unpaid checkout event leaking metadata into the generic top-up | ✓ | A catch-all case for checkout.session.* breaks before the default top-up branch (cs:77-80). Proven by BillingCommerceTests.Non_package_checkout_session_with_stray_credit_metadata_does_not_top_up. |
| invoice.paid without a subscription id | ✓ | when-guard requires Parent.SubscriptionDetails.SubscriptionId length>0 (cs:93-96); else falls to default (no metadata → no-op). Proven by BillingCommerceTests.Invoice_paid_without_subscription_id_is_processed_without_credit_grant. |
| Generic event metadata: bad guid / amount<=0 | ✓ | TryExtractTopUp validates guid + long + amount>0 (cs:154-166) |
| ProcessedAt stamp vs dispatched grant not in one transaction | ✓ | Subscription/invoice grants run their own SaveChanges on the same scoped context, then ProcessedAt commits separately; redelivery is safe because every downstream command is idempotent (sub-invoice:{id}, purchase:{id}, event-id). Package-confirm publish IS co-committed with ProcessedAt (cs:109-111) |
| Stripe's invoice.payment_succeeded (legacy/alternate) instead of invoice.paid | ✓ | Router accepts invoice.paid OR invoice.payment_succeeded (ProcessStripeEventCommand.cs:93) and both converge on GrantSubscriptionCreditsCommand with the same sub-invoice:{invoiceId} idempotency key. Proven by BillingCommerceTests.Invoice_payment_succeeded_grants_subscription_credits_like_invoice_paid. |
| Stripe's invoice.payment_failed for a renewal | ✓ | Router refreshes the subscription mirror through UpsertSubscriptionFromStripeCommand and does NOT grant invoice credits. Proven by BillingCommerceTests.Invoice_payment_failed_refreshes_subscription_to_past_due_without_granting_credits. |

**Testy:** BillingCommerceTests.Signed_topup_event_applies_ledger_topup_exactly_once_and_stamps_processed (generic metadata path + redelivery exactly-once); BillingCommerceTests.Non_package_checkout_session_with_stray_credit_metadata_does_not_top_up; BillingCommerceTests.Invoice_paid_without_subscription_id_is_processed_without_credit_grant; BillingCommerceTests.Invoice_payment_succeeded_grants_subscription_credits_like_invoice_paid; BillingCommerceTests.Invoice_payment_failed_refreshes_subscription_to_past_due_without_granting_credits; BillingCommerceTests.Subscription_lifecycle_... (subscription + invoice.paid routing); StripeReconcileTests.Stuck_stripe_event_...end_to_end (default top-up via reconcile)
**Test gaps:** No remaining focused Stripe event router gap in this slice.

_Routing is careful and idempotent. Both invoice success event names now share the same per-invoice idempotency key, so receiving both cannot double-grant._

### Subscriptions: checkout, object-state mirror, per-invoice grant, cancel — 🟢 minor-gaps
*Config-driven subscription plans mirrored from Stripe object state, with exactly-once per-period credit grants and proration-safe cancel.*

**Use cases:** User starts a subscription checkout for a config plan (CreateSubscriptionCheckoutHandler.cs); customer.subscription.* webhooks (or reconcile) upsert the local mirror from Stripe state (UpsertSubscriptionFromStripeCommand.cs); invoice.paid or invoice.payment_succeeded grants plan.CreditsPerPeriod exactly once per invoice (GrantSubscriptionCreditsCommand.cs); User cancels (Stripe-first, at period end by default) (CancelSubscriptionHandler.cs); GET /billing/subscriptions/me and /plans reads

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Out-of-order delivery (updated before created, invoice before subscription) | ✓ | Upsert reads Stripe OBJECT state and converges (UpsertSubscriptionFromStripeCommand.cs:12-18); GrantSubscriptionCredits upserts-then-retries when mirror missing (cs:31-40) |
| Subscription not provisioned by this platform (no user_id metadata) | ✓ | Upsert returns no-op (cs:35-38); grant returns no-op if still missing after upsert (cs:37-39) |
| Duplicate invoice / webhook redelivery / reconcile replay | ✓ | CreditTopUp idempotency key sub-invoice:{invoiceId} (cs:48-52). Proven by BillingCommerceTests.Duplicate_invoice_success_events_grant_subscription_credits_exactly_once. |
| Creation race on the local mirror (UNIQUE StripeSubscriptionId) | ✓ | catch DbUpdateException (not concurrency) — both writers mirrored same Stripe state (UpsertSubscriptionFromStripeCommand.cs:72-80) |
| Activated/Canceled integration events double-fire | ✓ | Published only on actual status transitions guarded by previousStatus (cs:60-70) |
| Plan has no credit grant (access-only) | ✓ | GrantSubscriptionCredits returns no-op when plan null or CreditsPerPeriod<=0 (cs:42-45). Proven by BillingCommerceTests.Access_only_subscription_plan_does_not_grant_invoice_credits. |
| User starts a second subscription while one is active | ✓ | CreateSubscriptionCheckoutHandler.cs:30-35 rejects when a live mirror already exists; Billing DB also enforces one non-canceled local mirror per user via partial UNIQUE(UserId) WHERE Status<>'Canceled' (Subscription.cs:44-47, migration OneLiveSubscriptionPerUser). Two abandoned checkout sessions without a mirror are still allowed by design; two live mirrors are not. Proven by SubscriptionCheckoutTests.Subscription_checkout_rejects_user_with_existing_live_subscription and Concurrent_live_subscription_mirrors_leave_only_one_non_canceled_subscription_per_user. |
| Cancel when no active subscription | ✓ | CancelSubscriptionHandler.cs:24-28 throws billing.subscription.not_found |
| Cancel-at-period-end then period elapses | ✓ | Eager local CancelAtPeriodEnd=true; authoritative Canceled arrives via webhook→upsert (CancelSubscriptionHandler.cs:11-14,33-40) |
| Immediate cancel config | ✓ | With Billing:Subscriptions:CancelAtPeriodEnd=false, CancelSubscriptionHandler sets Status=Canceled inline and /subscriptions/me stops returning it. Proven by CancelSubscriptionTests.Cancel_subscription_can_immediately_mark_the_local_mirror_canceled. |
| past_due/unpaid subscription (failed renewal / dunning) | ✓ | MapStatus maps past_due/unpaid/paused→PastDue (UpsertSubscriptionFromStripeCommand.cs:101-105) and GetMySubscription still returns it as the live subscription (Status!=Canceled). Failed invoice webhooks refresh the mirror without granting credits. Proven by MySubscriptionTests.My_subscription_reflects_past_due_and_cancel_at_period_end_from_reconciled_state and BillingCommerceTests.Invoice_payment_failed_refreshes_subscription_to_past_due_without_granting_credits. There is intentionally no dunning/revocation behavior yet; that is product scope, not a current handler bug. |
| Subscription period end living on Stripe items not the subscription | ✓ | StripeGateway.ToState takes max item CurrentPeriodEnd (StripeGateway.cs:122-128) |

**Testy:** BillingCommerceTests.Subscription_lifecycle_reconciles_object_state_out_of_order_and_grants_per_invoice (updated-before-created, invoice grant, cancel-at-period-end, deleted→Canceled, me→404); BillingCommerceTests.Duplicate_invoice_success_events_grant_subscription_credits_exactly_once; BillingCommerceTests.Access_only_subscription_plan_does_not_grant_invoice_credits; BillingCommerceTests.Subscription_plans_come_from_config_and_promo_codes_validate_through_stripe; SubscriptionCheckoutTests.Subscription_checkout_rejects_user_with_existing_live_subscription; SubscriptionCheckoutTests.Concurrent_live_subscription_mirrors_leave_only_one_non_canceled_subscription_per_user; CancelSubscriptionTests.Cancel_subscription_can_immediately_mark_the_local_mirror_canceled; MySubscriptionTests.My_subscription_reflects_past_due_and_cancel_at_period_end_from_reconciled_state
**Test gaps:** No remaining focused already-active subscription race gap in this slice.

_Object-state mirroring is the right design and the happy/out-of-order/cancel/failed-renewal paths are tested. The live mirror race is DB-enforced; dunning/PastDue revocation or notification remains a product decision rather than a handler bug._

### Promo-code validation (coupons) — ✅ correct
*Pre-checkout read so the UI can confirm a promotion code before redirecting to Stripe Checkout (Stripe enforces it authoritatively).*

**Use cases:** User checks a code via GET /billing/promo-codes/{code}/validate (ValidatePromoCodeHandler.cs); Checkout sessions surface the promo box via AllowPromotionCodes flag

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Unknown/inactive code | ✓ | FindActivePromotionCodeAsync returns null → NotFoundException billing.coupon.invalid (ValidatePromoCodeHandler.cs:16-17) |
| Discount math / redemption limits / expiry | ✓ | Deliberately delegated to Stripe at checkout (ValidatePromoCodeHandler.cs:6-9); this is a non-authoritative read only — coupons live entirely in Stripe |
| AllowPromotionCodes off | ✓ | StripeOptions.AllowPromotionCodes default true; threaded into CheckoutSessionSpec for both package and subscription checkouts (StripeGateway.cs:41) |
| percent-off vs amount-off shape | ✓ | PromotionCodeState carries PercentOff/AmountOff/Currency from coupon (StripeGateway.cs:106-120) |

**Testy:** BillingCommerceTests.Subscription_plans_come_from_config_and_promo_codes_validate_through_stripe (valid SUMMER10 percentOff + invalid NOPE→404); PromoCodeTests.Promo_code_validate_returns_discount_shape_for_active_code; PromoCodeTests.Promo_code_validate_trims_ui_input_before_provider_lookup (amountOff+currency); PromoCodeTests.Promo_code_validate_returns_404_for_invalid_or_expired_code; PromoCodeTests.Promo_code_validate_translates_provider_rate_limit_to_domain_error
**Test gaps:** No remaining focused promo-code validation gap in this slice.

_Thin, correctly-scoped passthrough; authoritative enforcement is rightly left to Stripe. Coverage is adequate for the read._

### Stripe Tax flag — 🟢 minor-gaps
*Toggle Stripe automatic Tax (merchant-of-record VAT) on every checkout session via config.*

**Use cases:** Deployment sets Billing:Stripe:AutomaticTax=true; subscription checkout requests AutomaticTax through the Stripe anti-corruption port (CreateSubscriptionCheckoutHandler.cs:56-62). Package checkout now uses the tenant payment-gateway plane, not the module Stripe gateway.

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| AutomaticTax off (default) | ✓ | StripeGateway.cs:42 sets AutomaticTax to null when false (no tax) |
| AutomaticTax on | ✓ | StripeGateway.cs:42 sets SessionAutomaticTaxOptions{Enabled=true}; comment notes it requires Stripe Tax enabled in dashboard (StripeOptions.cs); test Subscription_checkout_propagates_automatic_tax_config_to_provider_session |

**Testy:** SubscriptionCheckoutTests.Subscription_checkout_propagates_automatic_tax_config_to_provider_session
**Test gaps:** No remaining focused Stripe Tax flag gap for the module Stripe checkout path.

_A simple config flag correctly threaded for subscription checkout. Real tax math is Stripe's responsibility._

### Stripe reconcile sweep (3 passes, per-item isolation) — 🟢 minor-gaps
*Periodic self-healing: requeue stuck events, correct subscription drift against live Stripe, and re-grant paid-but-stuck purchases.*

**Use cases:** Cron (Jobs host) dispatches ReconcileStripeCommand; Pass 1: re-publish stripe_events stuck >30min unprocessed (cap 200); Pass 2: compare non-Canceled local subscriptions to live Stripe; on drift upsert + increment platform.billing.stripe_drift (cap 500); Pass 3: re-grant Pending/Abandoned sagas whose Stripe session is actually paid (cap 200)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Runaway volume | ✓ | Each pass capped + WARN log at cap + response cap flags (ReconcileStripeHandler.cs:60-65,84-89,143-148); cap branches covered for stuck events, subscriptions and stuck purchases. |
| One subscription's Stripe call errors (429/500/timeout) | ✓ | Per-item try/catch logs and continues; next run retries (cs:96-129). Proven by StripeReconcileTests.Provider_errors_are_isolated_per_subscription. |
| One stuck purchase's session lookup errors | ✓ | Per-item try/catch in Pass 3 (cs:154-174) |
| Re-grant of an unpaid/abandoned purchase | ✓ | Only re-publishes when payment_status is paid/no_payment_required (cs:156-160); proven by Stuck_UNPAID_purchase_is_NOT_regranted |
| Double-credit from a race between reconcile re-grant and a late real confirmation | ✓ | Both grant through CreditTopUp key purchase:{id} (cs:166-167; saga NotFound) — idempotent |
| Subscription canceled locally but reactivated in Stripe | ◐ | Pass 2 filters s.Status != Canceled (cs:78-79), so a locally-Canceled row reactivated upstream is never re-checked. One-directional drift correction; acceptable given cancel is user-initiated and Stripe-first, but not symmetric. |
| Subscription deleted in Stripe (404) | ✓ | GetSubscriptionAsync returns null → continue/skip (cs:98-102); upsert also no-ops on null |
| Stripe drift surfaced for ops | ✓ | WARN log + platform.billing.stripe_drift counter (cs:42-45,113-119); response drift count is proven by StripeReconcileTests.Subscription_drift_is_corrected_from_live_stripe_state and the metric export is pinned by StripeReconcileTests.Subscription_drift_records_the_platform_metric. |

**Testy:** StripeReconcileTests.Stuck_stripe_event_is_requeued_and_processed_end_to_end_by_the_reconcile_sweep (Pass 1); StripeReconcileTests.Stuck_event_reconcile_pass_reports_when_the_per_run_cap_is_reached (Pass 1 cap); StripeReconcileTests.Subscription_drift_is_corrected_from_live_stripe_state (Pass 2 drift correction); StripeReconcileTests.Subscription_drift_records_the_platform_metric (Pass 2 metric export); StripeReconcileTests.Provider_errors_are_isolated_per_subscription (Pass 2 per-item isolation); StripeReconcileTests.Subscription_reconcile_pass_reports_when_the_per_run_cap_is_reached (Pass 2 cap); StripeReconcileTests.Stuck_PAID_purchase_whose_confirmation_dead_lettered_is_regranted (Pass 3 positive); StripeReconcileTests.Stuck_UNPAID_purchase_is_NOT_regranted (Pass 3 negative); StripeReconcileTests.Stuck_purchase_reconcile_pass_is_capped_per_run (Pass 3 cap)
**Test gaps:** No remaining focused Stripe reconcile observability/cap gap in this slice; the live StripeGateway network path remains intentionally external.

_All three passes have focused behavioral coverage including cap branches and the drift metric export. The asymmetric (non-Canceled only) drift scope is a minor design limitation worth documenting._

### IStripeGateway anti-corruption seam (real + fake) — ✅ correct
*The single seam to Stripe; absorbs SDK quirks and enables offline end-to-end testing.*

**Use cases:** Production StripeGateway over Stripe.net (lazy client, key from config); FakeStripeGateway under Billing:Stripe:UseFakeGateway=true for the harness

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Stripe not configured (no ApiKey) | ✓ | Lazy client throws billing.stripe.not_configured (StripeGateway.cs:20-27); webhook ingest still works (only verifies) |
| Subscription/session 404 from Stripe | ✓ | GetSubscriptionAsync / GetCheckoutSessionPaymentStatusAsync catch NotFound → null (StripeGateway.cs:69-72,84-87) |
| Fake gateway shipped to Production | ✓ | StripeOptionsValidator fails host start in Production if UseFakeGateway (StripeOptionsValidator.cs) — prevents forged paid purchases |
| Promotion→coupon nesting / period on items | ✓ | Quirks absorbed in StripeGateway (ToState items max, coupon nesting cs:106-128) |
| Concurrent test access to the fake | ✓ | All fake state is ConcurrentDictionary/ConcurrentQueue (FakeStripeGateway.cs:15-19) — shared host hit concurrently |

**Testy:** StripeOptionsValidatorTests (production fake-gateway guard); All BillingCommerceTests/StripeReconcileTests exercise the fake gateway path
**Test gaps:** The real StripeGateway is necessarily untested (network); no contract test pins the fake's behavior to the real SDK shapes (acceptable, standard tradeoff)

_Clean ACL with a hard production safety guard on the fake. The validator is a strong, often-forgotten safety control._

**Nekonzistence v oblasti (1):**
- Reconcile drift scope is asymmetric (ReconcileStripeHandler.cs:78-79): Pass 2 only inspects local non-Canceled subscriptions, so a row Canceled locally but reactivated in Stripe is never reconciled back — the doc/comment claims 'subscription drift … Stripe wins' without noting this one-directional limit.


---

## GDPR & PII crypto

### PII at rest — [Encrypted] interceptor + decrypting converter — 🟢 minor-gaps
*Seal [Encrypted] string columns under the subject DEK on write and decrypt transparently on read.*

**Use cases:** users.Email/DisplayName ciphertext at rest under per-subject DEK; transparent decrypt on both the write context and the interceptor-free read factory; shredded subject -> column reveals [erased] instead of plaintext

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Save of [Encrypted] property with no protector registered (Gdpr disabled) | ✓ | PersonalDataEncryptionInterceptor throws InvalidOperationException rather than persisting plaintext (PersonalDataEncryption.cs:172-175) |
| Double-encryption / re-encrypting an already-sealed value | ✓ | skips values where LooksProtected(plaintext) is true and skips unmodified properties (PersonalDataEncryption.cs:160-170) |
| Restore in-memory plaintext after save so tracked entity stays usable and unchanged prop not re-written | ✓ | RestorePlaintext on SavedChanges/Failed resets CurrentValue+OriginalValue+IsModified (PersonalDataEncryption.cs:184-205) |
| Interceptor ordering vs audit | ✓ | PersonalDataEncryptionInterceptor registered AFTER AuditInterceptor so audit sees model-side plaintext and protects it itself (Messaging/DependencyInjection.cs:44-49) |
| Read in a process with no protector (design-time/Gdpr off) | ✓ | PersonalDataDecryptingConverter.Reveal returns raw envelope rather than crashing (PersonalDataEncryption.cs:64-68) |
| ExecuteUpdate/ExecuteDelete bypass the interceptor | ✓ | documented caveat; used deliberately only for erasure tombstones/constants (PersonalData.cs:18-19) |
| empty-string [Encrypted] value | ✓ | plaintext.Length == 0 skipped (PersonalDataEncryption.cs:166) |

**Testy:** PiiColumnEncryptionTests.Email_and_display_name_are_ciphertext_at_rest_but_plaintext_through_the_api; PersonalDataEncryptionInterceptorTests.Save_of_encrypted_plaintext_without_a_protector_is_rejected; PersonalDataEncryptionInterceptorTests.Decrypting_converter_surfaces_erased_marker_when_protected_value_cannot_be_revealed; GdprIntegrationTests.Subject_key_creation_is_never_audited_so_the_dek_never_reaches_the_audit_trail (indirect)
**Test gaps:** No remaining focused [Encrypted] live-column encryption/decrypt/refuse-plaintext gap in this slice.

_Logic is solid and now pinned at both levels: Identity proves real user rows are ciphertext at rest and plaintext through API reads; building-block tests prove the refuse-plaintext guard and [erased] read fallback._

### PersonalDataProtector (audit-PII crypto envelope) — ✅ correct
*IPersonalDataProtector impl: encrypt audit/column PII under the subject DEK, reading the DEK live each call.*

**Use cases:** AuditInterceptor protects [PersonalData] audit values; interceptor write-encryption of [Encrypted] columns; decrypt on read; refuse after shred

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Encrypt for an already-shredded subject | ✓ | GetOrCreateDek returns null for WrappedDek null OR DeletedAt set -> Protect returns RedactedMarker, never re-mints or writes plaintext (PersonalDataProtector.cs:36-47,97-108) |
| Concurrent first-use DEK insert race (UNIQUE UserId) | ✓ | catch DbUpdateException, reload winner; rethrow if no row (transient, not a unique race) (PersonalDataProtector.cs:112-129). Proven by SubjectKeyShredTests.Concurrent_first_use_protect_calls_share_one_subject_key. |
| Corrupted/format-drifted envelope on read | ✓ | catch FormatException/ArgumentException/OverflowException and Guid-length issues -> returns false, never crashes audit read (PersonalDataProtector.cs:67-77) |
| AAD mismatch / wrong-key decrypt | ✓ | catch CryptographicException -> TryReveal false (PersonalDataProtector.cs:91-94) |
| DEK retained in process memory | ✓ | DEK read LIVE from DB every call, AsNoTracking, never cached — cross-process shred honoured immediately (PersonalDataProtector.cs:99-107,132-137) |
| v1 (no AAD) legacy envelope vs v2 (AAD=subjectId) | ✓ | prefix-discriminated; v1 decrypts without AAD, writes are always v2 (PersonalDataProtector.cs:28-29,57-58,88) |
| Whole-envelope copied verbatim into another subject's row | ◐ | documented as out-of-scope DB-tampering, not a confidentiality break (read takes subjectId from envelope so its own AAD authenticates) — CryptoShredder.cs:35-38; acceptable given the stated threat model |
| Envelope subject id swapped to another live subject | ✓ | v2 embeds subjectId and decrypts with subjectId AAD; tampering the subject id makes TryReveal false. Proven by SubjectKeyShredTests.V2_envelope_cannot_be_re_attached_to_another_subject. |
| Protector context must be WRITE primary not read replica | ✓ | GdprModule forces RlsConnectionString.ForRuntime(write) so the INSERT/live-read guarantee isn't broken by replica lag (GdprModule.cs:50-72) |

**Testy:** CryptoShredderTests.Encrypt_then_Decrypt_with_same_dek_round_trips; CryptoShredderTests.Decrypt_with_a_different_dek_fails_modeling_crypto_shredding; CryptoShredderTests.Decrypt_with_matching_aad_round_trips_and_wrong_aad_fails; SubjectKeyShredTests.Post_shred_protect_redacts_instead_of_re_minting_a_readable_dek; SubjectKeyShredTests.Concurrent_first_use_protect_calls_share_one_subject_key; SubjectKeyShredTests.V2_envelope_cannot_be_re_attached_to_another_subject
**Test gaps:** No remaining focused PersonalDataProtector first-use race gap in this slice.

_Crypto seam is careful and well-reasoned. Protector-level redaction after shred, concurrent first-use, and v2 subject binding are now pinned._

### CryptoShredder (AES-256-GCM primitive) — ✅ correct
*Generate DEK and AES-GCM encrypt/decrypt with optional AAD; deleting the DEK makes ciphertext unrecoverable.*

**Use cases:** per-subject DEK generation; authenticated encryption of PII with subjectId AAD

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Blob too short to hold nonce+tag | ✓ | throws CryptographicException with clear message (CryptoShredder.cs:69-72); test CryptoShredderTests.Decrypt_rejects_blob_too_short_for_nonce_and_tag |
| null plaintext/dek | ✓ | ArgumentNullException.ThrowIfNull guards (CryptoShredder.cs:42-43,67-68) |
| Tamper / wrong key / wrong AAD | ✓ | AesGcm.Decrypt throws CryptographicException (caller catches); test CryptoShredderTests.Decrypt_with_matching_aad_round_trips_and_wrong_aad_fails |

**Testy:** CryptoShredderTests.Encrypt_then_Decrypt_with_same_dek_round_trips; CryptoShredderTests.Decrypt_with_a_different_dek_fails_modeling_crypto_shredding; CryptoShredderTests.Decrypt_with_matching_aad_round_trips_and_wrong_aad_fails; CryptoShredderTests.Decrypt_rejects_blob_too_short_for_nonce_and_tag
**Test gaps:** No remaining focused CryptoShredder primitive gap in this slice.

_Standard, correct AES-GCM layout [nonce|tag|ciphertext]. AAD and malformed-blob behavior are pinned directly at the primitive level._

### Blind index (HMAC) + fail-fast key validation — 🟢 minor-gaps
*Deterministic HMAC-SHA256 keyed hash enabling equality lookups on encrypted columns (login by email).*

**Use cases:** users.EmailHash UNIQUE lookup for login/duplicate-check; platform-wide secret key, dev placeholder rejected in prod

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Placeholder/empty/short key outside Development | ✓ | GdprEncryptionOptionsValidator fails startup (ValidateOnStart) when key missing, == placeholder, or < 32 chars (HmacBlindIndexHasher.cs:22-43; GdprModule.cs:76-79); test BlindIndexHasherTests.Validator_rejects_missing_placeholder_or_short_key_outside_development |
| Dev fallback when key unset | ✓ | falls back to DevKeyPlaceholder so dev/tests work (HmacBlindIndexHasher.cs:52-55) |
| null normalizedValue | ✓ | ArgumentNullException.ThrowIfNull (HmacBlindIndexHasher.cs:59) |
| Same input lookup stability | ✓ | HMAC-SHA256 over the caller-normalized value is deterministic and key-bound (HmacBlindIndexHasher.cs:57-63); test BlindIndexHasherTests.Hash_is_deterministic_for_same_normalized_value_and_changes_for_other_values |
| Caller-side normalization (Trim/ToUpperInvariant) consistency | ✓ | hasher hashes the value as-given; normalization is the CALLER's contract (PersonalData.cs:28-29). Register/Login/ForgotPassword normalize before hashing; login is directly pinned by PiiColumnEncryptionTests.Login_email_lookup_is_case_and_whitespace_insensitive_through_the_blind_index. |

**Testy:** BlindIndexHasherTests.Hash_is_deterministic_for_same_normalized_value_and_changes_for_other_values; BlindIndexHasherTests.Validator_rejects_missing_placeholder_or_short_key_outside_development; BlindIndexHasherTests.Validator_allows_dev_placeholder_in_development_only; PiiColumnEncryptionTests.Login_email_lookup_is_case_and_whitespace_insensitive_through_the_blind_index
**Test gaps:** No remaining focused blind-index key/normalization gap in this slice.

_Key handling and fail-fast are correct. The hasher stays deliberately dumb; Identity's login lookup now pins the caller-side normalization contract._

### Erasure fan-out + crypto-shred (UserErasureRequested -> shred) — 🟢 minor-gaps
*On erasure, run each module's IErasePersonalData then crypto-shred the subject DEK as the authoritative act.*

**Use cases:** self-service POST /gdpr/me/erase -> durable outbox event -> Worker fan-out; per-module anonymization (Notifications blanks Title/Body; Billing retains ledger; Gdpr deletes consents); DEK shred renders all ciphertext unrecoverable incl. backups/append-only

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Idempotent replay of the erasure event | ✓ | Wolverine inbox dedups; ShredSubjectKeyHandler guards on DeletedAt==null so a replay never re-stamps (ShredSubjectKeyHandler.cs:27-32; SubjectKeyShredTests) |
| Post-erasure PII write must not re-mint a readable DEK | ✓ | PersonalDataProtector returns `[pii-redacted]` when the subject key tombstone has WrappedDek null/DeletedAt set, and old envelopes no longer reveal. Proven by SubjectKeyShredTests.Post_shred_protect_redacts_instead_of_re_minting_a_readable_dek. |
| Identity from token not body | ✓ | RequestErasureEndpoint takes tenant.UserId, no body id (RequestErasureEndpoint.cs:14-22) |
| Missing subject key row (nothing ever encrypted) | ✓ | ShredSubjectKeyHandler no-op when row absent (ShredSubjectKeyHandler.cs:24-32) |
| One eraser throws mid fan-out | ✓ | UserErasureRequestedHandler wraps each eraser, logs failures, still runs the remaining erasers, then dispatches ShredSubjectKeyCommand before throwing a retry exception for the failed module (UserErasureRequestedHandler.cs:40-67). Proven by ErasureFanOutTests.Throwing_eraser_does_not_block_other_erasers_or_crypto_shred. |
| Erasure ordering: anonymize before shred | ✓ | erasers run first, then ShredSubjectKeyCommand; shred is described as authoritative, per-module anonymization is defence-in-depth (UserErasureRequestedHandler.cs:40-61) |
| Durable, not fire-and-forget | ✓ | RequestErasureHandler publishes via outbox + SaveChangesAndFlushMessagesAsync (RequestErasureHandler.cs:22-27) |

**Testy:** GdprIntegrationTests.Erasure_blanks_notification_pii_shreds_the_subject_key_and_retains_the_billing_ledger; GdprIntegrationTests.Consent_history_is_exported_and_deleted_on_erasure; SubjectKeyShredTests.Shred_drops_the_dek_and_stamps_deleted_at; SubjectKeyShredTests.Shred_is_idempotent_and_preserves_the_first_erasure_timestamp; SubjectKeyShredTests.Post_shred_protect_redacts_instead_of_re_minting_a_readable_dek; ErasureFanOutTests.Throwing_eraser_does_not_block_other_erasers_or_crypto_shred
**Test gaps:** No remaining focused erasure fan-out / crypto-shred gap in this slice.

_End-to-end erasure is well tested and correct. Per-eraser isolation now matches the export fan-out: failed module anonymization retries, but the authoritative crypto-shred is not blocked._

### Consent log (append-only, exported + erased) — ✅ correct
*Append-only grant/withdraw consent transitions per subject, included in export and deleted on erasure.*

**Use cases:** grant/withdraw appends a row (never updates); GET history newest-first; exported in Art.15 doc; deleted on erasure (no AML/tax retention)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Identity from token not body | ✓ | Grant/Withdraw endpoints use tenant.UserId; the wire request has no UserId. Extra body userId is ignored by model binding and proven by GdprIntegrationTests.Consent_grant_ignores_a_body_user_id_and_uses_the_token_subject. |
| Append-only (no in-place mutation) | ✓ | both handlers Add a new ConsentRecord (GrantConsentHandler.cs:17-25; WithdrawConsentHandler.cs:17-25) |
| Erasure of consents bypasses audit/xmin via ExecuteDelete | ✓ | documented sanctioned GDPR-scrub path (ConsentPersonalDataEraser.cs:9-24) |
| Consent rows excluded from AML/tax retention | ✓ | eraser deletes them, unlike the credit ledger (ConsentPersonalDataEraser.cs:9-12) |

**Testy:** GdprIntegrationTests.Consent_grant_then_withdraw_is_append_only_and_get_reflects_the_latest_state; GdprIntegrationTests.Consent_grant_ignores_a_body_user_id_and_uses_the_token_subject; GdprIntegrationTests.Consent_grant_trims_type_and_policy_version_before_persistence; GdprIntegrationTests.Consent_history_is_exported_and_deleted_on_erasure; ConsentValidatorTests.Grant_consent_validator_uses_stable_error_codes; ConsentValidatorTests.Withdraw_consent_validator_uses_stable_error_codes; ConsentValidatorTests.Consent_validators_accept_valid_input
**Test gaps:** No remaining focused consent log/validator gap in this slice.

_Clean append-only model, correct token-identity, stable validation error codes, and trimmed persisted consent values._

### Export fan-out (IExportPersonalData) — ✅ correct
*Assemble one Art.15 document keyed by module, resilient to a single exporter failing.*

**Use cases:** GET /gdpr/me/export aggregates Identity/Billing/Notifications/Gdpr.Consents sections; partial export beats a 500 when one module throws

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| One exporter throws | ✓ | per-exporter try/catch -> {"error":"export_failed"} for that module, others continue (ExportUserDataHandler.cs:23-43). Proven at handler level and through HTTP by injecting a throwing exporter into a same-DB derived host. |
| OperationCanceledException not swallowed | ✓ | filtered out of the catch so cancellation propagates (ExportUserDataHandler.cs:30) |
| subject_keys envelope excluded from export | ✓ | ConsentPersonalDataExporter deliberately exports only consents, not key material (ConsentPersonalDataExporter.cs:10-11) |

**Testy:** ExportResilienceUnitTests.A_throwing_exporter_yields_an_error_marker_and_does_not_break_the_others; ExportResilienceTests.Export_endpoint_returns_200_with_error_marker_when_one_exporter_throws; GdprIntegrationTests.Export_assembles_one_document_keyed_by_module_with_each_modules_section
**Test gaps:** No remaining focused export fan-out resilience gap in this slice.

_Resilience pattern is solid and now tested both at handler level and through the real HTTP endpoint._

### Retention sweep (tombstone permanent re-mint guard) — 🟢 minor-gaps
*Nightly sweep that deliberately purges NOTHING — shredded subject_key tombstones are retained permanently as the DEK re-mint guard.*

**Use cases:** block PersonalDataProtector.GetOrCreateDek from minting a fresh readable DEK for an already-erased subject; seam/cron/metric kept for future module-owned purgeable retention

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Shredded tombstone older than the old 30-day window | ✓ | retained (handler returns 0, deletes nothing) — RetentionSweepHandler.cs:25-31; asserted by RetentionSweepTests |
| Tombstone blocks post-erasure DEK re-mint | ✓ | A later Protect call returns `[pii-redacted]` instead of creating a fresh key, and the old envelope remains unrevealable. Proven by SubjectKeyShredTests.Post_shred_protect_redacts_instead_of_re_minting_a_readable_dek. |
| Concurrent sweep runs | ✓ | [DisallowConcurrentExecution] on the Quartz job (GdprRetentionSweepJob.cs:12) |
| Cron timezone | ✓ | InTimeZone(Utc) so 03:00 UTC fires correctly on non-UTC hosts (GdprModule.cs:108-114) |
| Live key never touched | ✓ | handler deletes nothing at all (RetentionSweepTests.Live_subject_key_is_not_deleted) |

**Testy:** RetentionSweepTests.Shredded_tombstone_is_retained_permanently_so_the_dek_cannot_be_re_minted; RetentionSweepTests.Shredded_key_within_retention_window_is_not_deleted; RetentionSweepTests.Live_subject_key_is_not_deleted_by_retention_sweep; SubjectKeyShredTests.Post_shred_protect_redacts_instead_of_re_minting_a_readable_dek
**Test gaps:** No remaining focused tombstone retention / DEK re-mint guard gap in this slice.

_Behaviour is correct (retain-forever, purge-nothing), and the regression that previously reopened the crypto-shred is fixed and now directly covered through the protector._

---

## Notifications & Realtime

### SendNotification (multi-channel dispatch) — ✅ correct
*Persist one in-app feed row + hand off durable per-channel (email/push) delivery via the outbox, plus an after-commit realtime push for in-app.*

**Use cases:** Admin/system sends a templated notification to a user over email+push+inapp; Welcome + purchase-completed handlers reuse this single slice rather than re-implementing delivery; Worker performs the actual SMTP/push send out-of-band (HTTP request returns immediately)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Template missing for requested key | ✓ | SendNotificationHandler.cs:30-34 falls back to en locale then throws NotFoundException('notification.template_not_found'); HTTP 404. Cross-module callers (welcome/purchase) catch it as non-fatal. |
| Locale missing/blank in data | ✓ | SendNotificationHandler.cs:28 defaults to 'en'; line 30-33 also falls back to the en template row if the locale-specific row is absent. |
| Email channel requested but no 'email' key in data | ✓ | SendNotificationHandler.cs:61 sends ToAddress=string.Empty; EmailDeliveryHandler ChannelDeliveryHandlers.cs:13-15 short-circuits to Task.CompletedTask when ToAddress is blank — no SMTP attempt, no throw. |
| Duplicate channels in the array (e.g. ['inapp','inapp']) | ✓ | SendNotificationHandler.cs:51 .Distinct() collapses duplicates so only one delivery message / one inapp flag results. |
| Unknown channel string slips past validation | ✓ | Validator restricts to email\|push\|inapp (SendNotificationValidator.cs:7,21-22); handler switch (line 53-79) has no default branch so an unknown value is silently ignored — defence-in-depth. |
| Caller passes someone else's UserId over HTTP | ✓ | Realtime push deliberately fires AFTER commit (SendNotificationHandler.cs:82-95) so RLS WITH CHECK on the IUserOwned notifications row rejects the insert and the commit fails before any phantom realtime event. Documented in-code. |
| In-app row always written even when only email/push requested | ✓ | By design a single inapp row is persisted per send regardless of channels (Notification.cs:8-12, handler line 39-48 always Adds); Channel is hard-coded 'inapp' (line 43) recording the feed origin. Intentional, documented. |
| email/push delivered but commit later fails | ✓ | Both are outbox PublishAsync inside the same SaveChangesAndFlushMessagesAsync transaction (line 56-86) — delivered only if commit succeeds; that flush IS the commit (no separate tx.Commit). |

**Testy:** NotificationsIntegrationTests.SendNotification_persists_an_inapp_row_and_enqueues_channel_delivery_via_the_outbox (NT-1); NotificationsIntegrationTests.SendNotification_persists_durable_delivery_messages_for_email_and_push_channels; NotificationsIntegrationTests.SendNotification_deduplicates_channels_before_publishing_delivery_messages; NotificationsIntegrationTests.SendNotification_with_missing_template_key_returns_not_found; NotificationsIntegrationTests.SendNotification_falls_back_to_english_template_when_requested_locale_is_missing; TemplateRendererTests.Render_replaces_all_matching_placeholders
**Test gaps:** No remaining focused SendNotification outbox handoff gap in this slice; durable EmailDeliveryRequested/PushDeliveryRequested messages are now pinned via Wolverine's public message store API.

_Clean reuse-first slice. The 'one inapp row regardless of channels' is intentional but slightly surprising; well documented._

### Email / Push channel delivery (Worker-side) — 🟢 minor-gaps
*Durable Worker handlers that perform the real SMTP (MailKit) or push (stub) send for outboxed delivery messages.*

**Use cases:** Worker drains EmailDeliveryRequested -> SmtpEmailSender connects to a relay and sends plaintext mail; Worker drains PushDeliveryRequested -> NoOpPushSender stub (real FCM/Expo later)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Empty ToAddress on the email message | ✓ | ChannelDeliveryHandlers.cs:13-15 returns Task.CompletedTask without touching SMTP. Proven by ChannelDeliveryHandlersTests.Email_delivery_handler_skips_missing_address_without_calling_smtp. |
| SMTP relay unreachable / send throws | ✓ | Handler lets the exception bubble; PlatformMessaging retry-with-cooldown + durable dead-letter (per CLAUDE.md §4 messaging resilience) governs it. SmtpEmailSender connects per-send (SmtpEmailSender.cs:24-33). Proven at handler boundary by ChannelDeliveryHandlersTests.Email_delivery_handler_propagates_smtp_failures_for_wolverine_retry_and_dlq. |
| Worker-side SMTP delivery uses the rendered locale-specific payload | ✓ | `WorkerEmailDeliveryTests.Worker_delivers_email_channel_to_smtp_with_the_requested_locale_template` runs API as publisher-only, starts a real `ModularPlatform.Worker` child process, points MailKit at a local SMTP listener, and verifies the delivered message contains the `cs` template, not the `en` fallback. |
| Push delivery | ◐ | NoOpPushSender.cs:9 is a deliberate stub — push is a documented no-op until a real provider lands; the handler contract and no-op provider are directly tested. No device receives anything until a real provider replaces the stub. |
| SMTP auth optional | ✓ | SmtpEmailSender.cs:27-30 only authenticates when User is non-empty; StartTlsWhenAvailable used. |

**Testy:** WorkerEmailDeliveryTests.Worker_delivers_email_channel_to_smtp_with_the_requested_locale_template; ChannelDeliveryHandlersTests.Email_delivery_handler_sends_exact_rendered_payload_to_email_sender; ChannelDeliveryHandlersTests.Email_delivery_handler_skips_missing_address_without_calling_smtp; ChannelDeliveryHandlersTests.Email_delivery_handler_propagates_smtp_failures_for_wolverine_retry_and_dlq; ChannelDeliveryHandlersTests.Push_delivery_handler_sends_exact_rendered_payload_to_push_sender; ChannelDeliveryHandlersTests.Push_delivery_handler_propagates_provider_failures_for_wolverine_retry_and_dlq; ChannelDeliveryHandlersTests.Noop_push_sender_completes_without_external_provider
**Test gaps:** SmtpEmailSender still has no external relay test (acknowledged — environment concern), but Worker-side SMTP delivery is now covered against a local SMTP listener.

_Push being a no-op is by design and documented; worker-side email delivery now has an out-of-process Worker + SMTP harness, while real external relay behaviour remains an environment-backed integration concern._

### Templates + rendering + seeding — 🟢 minor-gaps
*Reusable {placeholder} message templates keyed by (Key,Locale), rendered at send time, idempotently seeded on startup.*

**Use cases:** welcome (en/cs) and purchase_completed (en/cs) seeded so the cross-module handlers find a template; Render substitutes data dict values into Subject/Body; Admin can insert custom templates (tests do via SQL)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Unmatched {placeholder} in template | ✓ | TemplateRenderer.cs:9-23 leaves unmatched placeholders intact (only replaces keys present in data). Proven by TemplateRendererTests.Render_leaves_unmatched_placeholders_intact. |
| Empty template or empty data dict | ✓ | TemplateRenderer.cs:11-14 early-returns the template unchanged. Proven by TemplateRendererTests.Render_returns_empty_template_unchanged and Render_returns_template_unchanged_when_data_is_empty. |
| Concurrent seed across multiple hosts | ✓ | NotificationsSeeder.cs:46-67 checks AnyAsync then relies on UNIQUE(Key,Locale) (NotificationTemplate.cs:30); a concurrent duplicate insert is caught as DbUpdateException and logged benign. Repeated same-host runs are proven by NotificationsIntegrationTests.Notifications_seeder_can_run_repeatedly_without_duplicate_builtin_templates; parallel host startup is proven by Notifications_seeder_concurrent_hosts_leave_one_complete_builtin_template_set. |
| Seeder runs as a non-tenant hosted service | ✓ | NotificationTemplate is NOT ITenantScoped (NotificationTemplate.cs:9-11) so the seeder's plain context inserts platform-shared rows without a tenant filter. |
| Placeholder value containing braces could re-trigger substitution | ✓ | Render iterates data once with Ordinal Replace (TemplateRenderer.cs:17-21); a value that itself contains '{otherKey}' is only replaced if a LATER dict key matches — order-dependent but values are short controlled strings; no infinite loop. Proven by TemplateRendererTests.Render_does_not_loop_when_value_contains_placeholder_text. |

**Testy:** TemplateRendererTests.Render_replaces_all_matching_placeholders; TemplateRendererTests.Render_leaves_unmatched_placeholders_intact; TemplateRendererTests.Render_returns_empty_template_unchanged; TemplateRendererTests.Render_returns_template_unchanged_when_data_is_empty; TemplateRendererTests.Render_does_not_loop_when_value_contains_placeholder_text; NotificationsIntegrationTests.SendNotification_uses_requested_locale_template_when_it_exists; NotificationsIntegrationTests.SendNotification_falls_back_to_english_template_when_requested_locale_is_missing; NotificationsIntegrationTests.Notifications_seeder_can_run_repeatedly_without_duplicate_builtin_templates; NotificationsIntegrationTests.Notifications_seeder_concurrent_hosts_leave_one_complete_builtin_template_set
**Test gaps:** No remaining focused template rendering/seeding gap in this slice.

_Rendering logic is directly covered; seeding is covered for repeated same-host runs and parallel host startup._

### In-app feed (get / mark-read) — ✅ correct
*Per-user paged feed of in-app notifications with unread filter and an idempotent mark-as-read.*

**Use cases:** GET /v1/notifications/me?unreadOnly=&page=&pageSize= — owner-scoped paged feed, newest first; POST /v1/notifications/{id}/read — mark own notification read

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Identity must come from token, not body | ✓ | Both endpoints derive userId from ITenantContext.UserId (GetMyNotificationsEndpoint.cs:22-23, MarkNotificationReadEndpoint.cs:20-21); no client-supplied subject id. RLS on IUserOwned adds DB-level isolation. |
| Mark-read on another user's notification | ✓ | MarkNotificationReadHandler.cs:17-19 filters n.UserId == command.UserId; a foreign id -> NotFoundException (and RLS would 404 it regardless); test Mark_notification_read_is_owner_scoped_and_idempotent. |
| Mark-read called twice (idempotency) | ✓ | MarkNotificationReadHandler.cs:21-25 only stamps ReadAt when null and only then SaveChanges — second call is a no-op, no spurious audit/concurrency write; test Mark_notification_read_is_owner_scoped_and_idempotent. |
| Pagination bounds (page<=0, oversized pageSize) | ✓ | GetMyNotificationsHandler.cs:29 delegates to ToPagedResponseAsync(query.Page,...) with a PageRequest(page,pageSize); PageRequest clamps page/pageSize; test My_notifications_default_feed_includes_read_and_unread_and_clamps_page_bounds. |
| unreadOnly default | ✓ | Endpoint defaults unreadOnly to false (GetMyNotificationsEndpoint.cs:25); handler applies ReadAt==null filter only when true (GetMyNotificationsHandler.cs:21-24); test My_notifications_default_feed_includes_read_and_unread_and_clamps_page_bounds. |

**Testy:** NotificationsIntegrationTests.Unread_feed_and_mark_read_round_trip (NT-4); NotificationsIntegrationTests.Mark_notification_read_is_owner_scoped_and_idempotent; NotificationsIntegrationTests.My_notifications_default_feed_includes_read_and_unread_and_clamps_page_bounds; NotificationsIntegrationTests.My_notifications_feed_is_paged_and_owner_scoped
**Test gaps:** No remaining focused in-app feed / mark-read gap in this slice.

_Solid identity-from-token + idempotent design; owner scoping, unread filtering, default feed and clamping are pinned end-to-end._

### Cross-module reaction handlers (welcome + purchase-completed) — ✅ correct
*React to Identity UserRegistered and Billing CreditPurchaseCompleted by dispatching SendNotificationCommand, reusing the one delivery slice.*

**Use cases:** New signup -> welcome notification (email+inapp); Credit purchase completes -> purchase_completed in-app notification

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Template not seeded in a fresh deploy | ✓ | Both handlers catch NotFoundException and LogWarning instead of dead-lettering (SendWelcomeHandler.cs:30-34, SendPurchaseCompletedHandler.cs:33-39). Proven by Welcome_handler_tolerates_missing_template_and_deduplicates_retries and Purchase_completed_handler_is_idempotent_and_missing_template_is_non_fatal. |
| Duplicate delivery of the integration event | ✓ | Wolverine inbox dedups by MessageId; handler-level idempotency keys also collapse direct duplicate/retry calls to one row. Proven by Welcome_handler_tolerates_missing_template_and_deduplicates_retries and Purchase_completed_handler_is_idempotent_and_missing_template_is_non_fatal. |
| DisplayName null on UserRegistered | ✓ | SendWelcomeHandler.cs:22 coalesces message.DisplayName ?? message.Email for the {displayName} placeholder. |
| Welcome cross-user send is rejected by RLS | ✓ | These run in the Worker under the SYSTEM context (no per-user RLS principal), so they legitimately write a row for another user's id — unlike the HTTP path. Documented in the test's AdminTokenAsync rationale (NotificationsIntegrationTests.cs:246-258). |
| A non-NotFound exception (e.g. DB outage) during dispatch | ✓ | Only NotFoundException is swallowed; any other exception propagates to Wolverine retry/dead-letter — correct (don't silently drop real failures). |

**Testy:** NotificationsIntegrationTests.Register_creates_welcome_notification_after_seeder_has_seeded_the_template (EV-2); NotificationsIntegrationTests.Welcome_handler_tolerates_missing_template_and_deduplicates_retries; NotificationsIntegrationTests.CreditPurchaseCompleted_event_creates_purchase_completed_notification; NotificationsIntegrationTests.Purchase_completed_handler_is_idempotent_and_missing_template_is_non_fatal
**Test gaps:** No remaining focused cross-module notification reaction-handler gap in this slice.

_The non-fatal-missing-template and retry/idempotency behavior is now pinned directly for both reaction handlers._

### Realtime fan-out (publisher + Redis + registry) — ✅ correct
*Deliver an after-commit event to whichever Api instance holds the user's SSE connection, via Redis pub/sub or a local fallback.*

**Use cases:** SendNotification inapp push reaches the user's open SSE stream; Multi-instance: Redis subscriber forwards to the instance with the live connection — no sticky sessions; Single-instance dev: LocalRealtimePublisher delivers straight to the in-process registry

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Redis not configured | ✓ | DependencyInjection.cs:24-40 selects LocalRealtimePublisher when Redis:ConnectionString is blank; the registry + ports are always present so producers don't change. |
| One connection's delivery throws and starves the user's other tabs | ✓ | RealtimeConnectionRegistry.DeliverLocal Realtime.cs:72-90 try/catches per handler so a failing subscriber can't starve siblings. Proven by RealtimeSseTests.Registry_isolates_a_throwing_connection_from_other_connections. |
| Redis envelope malformed / wrong channel guid | ✓ | RealtimeRedisSubscriber.cs:58-69 guards Guid.TryParse + null/empty value + null envelope before delivering. |
| PublishToTenantAsync on the local publisher | ✓ | LocalRealtimePublisher.PublishToTenantAsync now fails loud with NotSupportedException instead of silently dropping tenant broadcasts; proven by RealtimeSseTests.Local_tenant_broadcast_fails_loud_instead_of_silently_dropping_events. |
| Connection registered then disposed (client disconnect) | ✓ | Subscribe returns an IDisposable that TryRemoves the connection (Realtime.cs:58-69); the SSE endpoint disposes it on enumerator cancel. |

**Testy:** RealtimeSseTests.Registry_delivers_an_event_only_to_the_owning_user; RealtimeSseTests.Registry_isolates_a_throwing_connection_from_other_connections; RealtimeSseTests.Local_tenant_broadcast_fails_loud_instead_of_silently_dropping_events
**Test gaps:** No test for the Redis publisher/subscriber path (acknowledged — needs live Redis)

_Owner-scoping + local fan-out are sound; unsupported tenant broadcast now fails loudly instead of pretending to work._

### SSE stream endpoint (/realtime/stream) — 🟢 minor-gaps
*Native .NET 10 Server-Sent-Events endpoint that bridges the push registry to an IAsyncEnumerable, emitting replay then live events, owner-scoped.*

**Use cases:** Browser opens an authenticated EventSource to receive its own realtime events; On reconnect, replays missed events (Last-Event-ID) before switching to live

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Unauthenticated request | ✓ | RequireAuthorization + tenant.UserId null -> UnauthorizedException (RealtimeStreamEndpoint.cs:32-33,41). Tested: Unauthenticated_stream_is_rejected. |
| Slow/dead consumer back-pressure (unbounded memory growth) | ✓ | Channel.CreateBounded(256, DropOldest) (RealtimeStreamEndpoint.cs:57-62) — TryWrite never blocks/fails; oldest unread dropped under back-pressure. Best-effort by design, documented and covered by RealtimeSseTests.Sse_live_buffer_drops_oldest_events_under_back_pressure. |
| Live events arriving while replay is being emitted | ✓ | Subscribes BEFORE emitting replay (RealtimeStreamEndpoint.cs:63-77) so no live event is lost in the gap; replay then live. |
| Client disconnect mid-stream | ✓ | CancellationToken cancels ReadAllAsync; `using` disposes the subscription, removing the registry entry. |
| Replay vs live duplicate (an event both replayed and delivered live) | ◐ | Possible narrow window: an event published between the registry Subscribe and replay read can be both replayed AND delivered live, yielding a duplicate SSE frame with the same EventId. This is now an explicit at-least-once contract; clients deduplicate by SSE EventId (RealtimeMessage and RealtimeStreamEndpoint docs). |

**Testy:** RealtimeSseTests.Unauthenticated_stream_is_rejected; RealtimeSseTests.Sse_live_buffer_drops_oldest_events_under_back_pressure
**Test gaps:** The authenticated streaming round-trip is explicitly NOT tested over TestServer (buffers infinite SSE) — acknowledged; only manual/real-server verified; No test for the replay-then-live ordering or the replay/live duplicate window

_The replay/live duplicate-by-id window is the only substantive correctness nuance; it is now documented as the SSE contract._

### Replay buffer (Last-Event-ID, TTL) — ✅ correct
*Per-user short-lived event buffer (Redis Streams or in-memory ring) replayed on SSE reconnect; PII-minimized via MAXLEN + TTL.*

**Use cases:** Client reconnects with Last-Event-ID and receives events missed during the gap; Bounded retention caps how long replayed PII lingers (no crypto-shred in Redis)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Null/empty/whitespace Last-Event-ID | ✓ | Both impls return [] (no full-history replay by design) — Realtime.cs:140-143 (Redis) and 232-236/271-272 (local). Tested. |
| Replay disabled via config | ✓ | Enabled=false -> PublishToUserAsync skips the XADD/buffer push and ReadSinceAsync returns [] (Realtime.cs:114-126,146-149,218-223). Tested (local). |
| Ring buffer over capacity | ✓ | LocalRealtimePublisher UserBuffer trims front when > MaxEvents (Realtime.cs:252-263); Redis uses approximate MAXLEN (Realtime.cs:119-123). Both tested (local) / asserted via the eviction test. |
| Cursor at the last event | ✓ | Returns empty — Redis IncrementStreamId bumps the seq for an exclusive XRANGE lower bound (Realtime.cs:154-156,181-197); local compares numerically > cursor (Realtime.cs:271-283). Tested for local. |
| Malformed Redis stream id in IncrementStreamId | ✓ | Falls back to the original id (Realtime.cs:191-196); XRANGE on an unknown id returns empty — safe. No-dash case appends '-1' (line 184-187). Proven by RealtimeReplayTests.Redis_stream_cursor_increment_handles_missing_malformed_and_overflow_sequence. |
| Approximate MAXLEN means Redis may retain MORE than MaxEvents | ◐ | useApproximateMaxLength:true trades exactness for performance — slightly more PII may linger than MaxEvents implies. The options contract now calls MaxEvents a soft event-count bound for Redis; TTL remains the hard time bound. |
| Local ring id overflow / cross-restart cursor | ◐ | LocalRealtimePublisher ids are a process-local Interlocked counter; on Api restart the counter resets to 1, so an old client cursor (e.g. '5000') would replay nothing or mismatch. The class contract now documents that local cursors are meaningful only within the current API process lifetime. |

**Testy:** RealtimeReplayTests.ReadSinceAsync_with_null_or_empty_lastEventId_returns_empty; RealtimeReplayTests.ReadSinceAsync_returns_only_events_newer_than_cursor; RealtimeReplayTests.ReadSinceAsync_at_last_event_returns_empty; RealtimeReplayTests.Ring_buffer_bounded_to_maxEvents_evicts_oldest_first; RealtimeReplayTests.ReadSinceAsync_for_unknown_userId_returns_empty; RealtimeReplayTests.Events_from_different_users_are_isolated; RealtimeReplayTests.Disabled_replay_buffer_returns_empty_from_ReadSince; RealtimeReplayTests.Redis_stream_cursor_increment_handles_missing_malformed_and_overflow_sequence
**Test gaps:** Zero live Redis impl tests for XADD MAXLEN, TTL refresh, and exclusive XRANGE behaviour — only the cursor helper and local ring are covered; no test for TTL expiry behavior.

_Local path is well covered; the Redis production replay path has solid code but no automated live-Redis tests, and approximate-MAXLEN intentionally makes the event-count PII bound soft._

### GDPR export / erasure (Notifications) — 🟢 minor-gaps
*Export the subject's in-app feed and anonymize PII in place on erasure, keeping structural rows.*

**Use cases:** GDPR data-portability fan-out includes the user's notifications; User erasure scrubs Title/Body across all the user's notification rows

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Erasure idempotency / re-run | ✓ | NotificationsPersonalDataEraser.cs:24-33 ExecuteUpdate blanks Title/Body to string.Empty (NOT NULL columns); re-running is a harmless no-op; test Notifications_gdpr_export_and_erasure_ports_return_feed_and_scrub_only_the_subject. |
| Erasure runs without a tenant/user context | ✓ | Runs in the Worker system context so the tenant filter doesn't restrict the match; filters by UserId explicitly (documented Realtime/eraser comment, line 16-18). |
| ExecuteUpdate bypasses audit + xmin + the live-column encryption interceptor | ✓ | Intentional for a set-based scrub (CLAUDE.md notes ExecuteUpdate bypasses the interceptor); the audit trail PII was already crypto-shredded via the subject DEK, and erasure shreds that DEK elsewhere, so audit PII is unrecoverable regardless. Live Title/Body are blanked here. |
| Export ordering / empty feed | ✓ | NotificationsPersonalDataExporter.cs:21-39 read-factory, OrderByDescending(CreatedAt), returns a dict (empty list if no rows); test Notifications_gdpr_export_and_erasure_ports_return_feed_and_scrub_only_the_subject. |
| Both ports registered for fan-out | ✓ | NotificationsModule.cs:47-48 registers both IExportPersonalData and IErasePersonalData (CLAUDE.md warns fan-out only works if both are registered). |

**Testy:** NotificationsIntegrationTests.Notification_pii_is_crypto_shredded_in_the_audit_trail; NotificationsIntegrationTests.Notifications_gdpr_export_and_erasure_ports_return_feed_and_scrub_only_the_subject
**Test gaps:** No remaining focused Notifications GDPR export/erasure port gap in this slice.

_Implementation is correct and matches platform conventions; exporter and eraser ports are now pinned through the same DI fan-out interfaces GDPR uses._

---

## Operations & Files

### Long-running operation accept (202 + outbox) — ✅ correct
*Accept slow work, persist a Pending operation + durable work message in one transaction, return 202 with a Location to the status endpoint.*

**Use cases:** Client kicks off a long job (export/bulk/external call) and gets an operation id to poll; Any module reuses IOperationStore.CreateAsync + publish to start its own long-running flow

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Owner must come from token, not body (IDOR) | ✓ | StartDemoOperationEndpoint.cs:25 takes userId from ITenantContext.UserId; StartDemoOperationCommand carries it; row stamped IUserOwned at StartDemoOperationHandler.cs:22 |
| Operation row + work message committed atomically (no orphan operation, no phantom message) | ✓ | StartDemoOperationHandler.cs:26-29 adds the Operation to outbox.DbContext and PublishAsync, then SaveChangesAndFlushMessagesAsync — single outbox commit |
| Duplicate client retry of the accept request | ✓ | `Idempotency-Key` header is stored on operations; UNIQUE(UserId, Type, IdempotencyKey) plus pre-check/catch race returns the original operation id and does not publish a second work item. Covered by OperationsTests.Demo_operation_accept_is_idempotent_for_the_same_user_type_and_key. |
| Malformed accept idempotency key | ✓ | StartDemoOperationValidator rejects keys longer than the DB column before persistence with operations.idempotency_key.too_long. Covered by OperationsTests.Demo_operation_rejects_an_oversized_idempotency_key_before_creating_an_operation. |
| Location stays correct under the /v1 group prefix | ✓ | StartDemoOperationEndpoint.cs:29 uses LinkGenerator.GetPathByName('GetOperationStatus') with a string fallback; proven by OperationsTests Location assertion |
| Unauthenticated request | ✓ | RequireAuthorization() at StartDemoOperationEndpoint.cs:33 + explicit UnauthorizedException at line 25 |
| Slow work accidentally done in the accept handler | ✓ | Handler only enqueues; the canonical comment + RunDemoOperationHandler does the work on the worker |

**Testy:** OperationsTests.Demo_operation_is_accepted_runs_on_the_worker_and_is_owner_scoped (202 + Location + worker drives to Succeeded); OperationsTests.Demo_operation_accept_is_idempotent_for_the_same_user_type_and_key; OperationsTests.Demo_operation_rejects_an_oversized_idempotency_key_before_creating_an_operation
**Test gaps:** No remaining focused accept/idempotency gap in this slice.

_Canonical 202 pattern; clean, token-sourced owner, atomic outbox, and retry-safe accept via Idempotency-Key._

### Operation state machine + terminal guard (OperationStore) — 🟢 minor-gaps
*Advance an operation Pending→Running→Succeeded/Failed with terminal states final and idempotent.*

**Use cases:** Worker marks running, completes with JSON result, or fails with errorCode/detail; Redelivered/duplicate worker message must not resurrect a terminal operation

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Terminal state must not be flipped/resurrected by a duplicate transition | ✓ | OperationStore.TransitionAsync reloads the row and early-returns (idempotent no-op) when Status is Succeeded/Failed; covered by OperationsTests.A_terminal_operation_is_not_resurrected_by_a_duplicate_worker_transition and Redelivered_worker_message_does_not_resurrect_a_failed_terminal_operation |
| Transition on a missing operation id | ✓ | OperationStore.TransitionAsync throws NotFoundException('operation.not_found') if the row is absent; proven by OperationsTests.Worker_transition_on_missing_operation_surfaces_not_found. |
| Worker work throws → operation must reach a terminal state (not stuck Running/Pending) | ✓ | RunDemoOperationHandler.cs:32 FailAsync in catch; MarkRunning is INSIDE the try (line 19) so a failed Pending→Running also terminalizes; covered by OperationWorkerFailureTests |
| Work message is permanently dead-lettered before terminalizing | ✓ | ReconcileStaleOperationsHandler ages out old Pending/Running rows to Failed with `operation.stale_reconciled`; scheduled by OperationsReconcileStaleOperationsJob every 15 minutes UTC; covered by OperationsTests.Reconcile_stale_operations_marks_only_old_non_terminal_operations_failed and JobsHostWiringTests.Operations_stale_reconcile_cron_uses_utc |
| Concurrent transitions racing (xmin conflict) outside the CQRS dispatcher pipeline | ✓ | OperationStore.TransitionAsync now has a local bounded DbUpdateConcurrencyException retry: clear tracker, reload, then either apply the transition or observe the terminal state as an idempotent no-op. Proven by OperationsTests.Concurrent_worker_transitions_retry_xmin_conflicts_and_leave_one_terminal_state. |
| FailAsync itself cannot write (DB down) | ✓ | RunDemoOperationHandler.cs:27-33 comment: the exception propagates and Wolverine retries the whole handler |

**Testy:** OperationsTests.A_terminal_operation_is_not_resurrected_by_a_duplicate_worker_transition; OperationsTests.Redelivered_worker_message_does_not_resurrect_a_failed_terminal_operation; OperationsTests.Concurrent_worker_transitions_retry_xmin_conflicts_and_leave_one_terminal_state; OperationsTests.Worker_transition_on_missing_operation_surfaces_not_found; OperationsTests.Reconcile_stale_operations_marks_only_old_non_terminal_operations_failed; OperationWorkerFailureTests.Worker_marks_the_operation_failed_when_the_work_throws; JobsHostWiringTests.Operations_stale_reconcile_cron_uses_utc
**Test gaps:** No remaining focused OperationStore state-machine/concurrency gap in this slice.

_State machine is solid and idempotent. Worker transitions now defend themselves locally against xmin races instead of relying only on Wolverine redelivery; dead-letter-before-terminalize is covered by an Operations-owned reconcile job._

### Operation status polling (owner-scoped read) — ✅ correct
*Return an operation's status/result to the user who owns it; foreign id is a 404.*

**Use cases:** Caller polls GET /operations/{id} until terminal; Defence-in-depth ownership even with RLS disabled

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Foreign user reads another's operation | ✓ | GetOperationStatusHandler.cs:22 explicit WHERE o.UserId == query.UserId AND RLS; 404 via NotFoundException. Tested by OperationsTests RLS check + the app-layer-bypass test |
| RLS disabled deployment still must not leak | ✓ | OperationsTests.Operation_status_is_owner_scoped_at_the_app_layer_even_when_rls_is_bypassed dispatches under system context with a foreign id and asserts NotFoundException |
| Read uses no-tracking read factory (no accidental write/tenant write context) | ✓ | GetOperationStatusHandler.cs:14,19 uses IReadDbContextFactory<OperationsDbContext> |
| Unauthenticated poll | ✓ | RequireAuthorization() + UnauthorizedException at GetOperationStatusEndpoint.cs:21-26 |
| Failed terminal state returns safe error payload to the owner | ✓ | OperationStore.FailAsync persists Status=Failed, ErrorCode, ErrorDetail, CompletedAt; GetOperationStatusHandler returns those fields; covered by OperationsTests.Operation_status_surfaces_failed_terminal_state_with_safe_error_details |

**Testy:** OperationsTests RLS-isolated 404 assertion; OperationsTests.Operation_status_is_owner_scoped_at_the_app_layer_even_when_rls_is_bypassed; OperationsTests.Operation_status_surfaces_failed_terminal_state_with_safe_error_details
**Test gaps:** No remaining focused status-polling gap; deeper worker-transition gaps are tracked in the operation state machine section above.

_Dual-gated ownership (app filter + RLS) is exemplary._

### File upload (server key, allowlist, size cap) — 🟢 minor-gaps
*Accept a multipart file from the owner, store bytes under a server-generated opaque key, persist metadata.*

**Use cases:** User uploads png/jpeg/pdf/txt and gets a file id + download Location; Reject disallowed content types and oversized files before persistence

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Client filename used as storage key (path traversal) | ✓ | UploadFileHandler.cs:20 storageKey = '{userId:N}/{id:N}' — server-generated, never the filename; FileName stored display-only (FileObject.cs:11-13) |
| Disallowed content type | ✓ | UploadFileValidator.cs:21-23 allowlist (deny by default) → file.content_type.not_allowed; tested by FilesUploadTests.Disallowed_content_type_is_rejected which also asserts no row persisted |
| Oversized upload | ✓ | UploadFileValidator.cs:18-19 cap (file.too_large) + RequestBodySizeLimit metadata at UploadFileEndpoint.cs:49 (Kestrel 413 before buffering); tested by Oversized_file_is_rejected (accepts 400 or 413) |
| Empty / zero-byte file | ✓ | UploadFileValidator.cs:18 GreaterThan(0) → file.empty |
| Owner from token not body | ✓ | UploadFileEndpoint.cs:34 userId from ITenantContext; UploadFileCommand.UserId set there |
| Missing/empty content-type on the form part | ✓ | UploadFileEndpoint.cs:39 passes file.ContentType ?? string.Empty; empty string is not on the allowlist so it is rejected with file.content_type.not_allowed. Proven by FilesUploadTests.Missing_content_type_is_rejected. |
| Bytes written to storage but metadata SaveChanges fails (orphan blob) | ✓ | UploadFileHandler.cs:35 catches SaveChanges failure, best-effort deletes the just-written storage key, logs cleanup failure, then rethrows the original DB error. Tested by FilesUploadTests.Upload_cleans_up_blob_when_metadata_persistence_fails. |
| Declared Size vs actual stream length mismatch | ◐ | UploadFileEndpoint.cs:39 uses file.Length (server-measured by Kestrel), not a client-claimed value, so Size is trustworthy; the validator caps Size but the stream itself is also body-size-limited. No independent re-count after copy, but file.Length is authoritative for IFormFile. |

**Testy:** FilesUploadTests.Upload_then_download_round_trips_the_same_bytes_and_content_type; FilesUploadTests.Disallowed_content_type_is_rejected; FilesUploadTests.Missing_content_type_is_rejected; FilesUploadTests.Oversized_file_is_rejected; FilesUploadTests.Upload_location_header_is_versioned_and_points_at_the_download_route; FilesUploadTests.Upload_uses_server_generated_storage_key_not_the_client_filename; FilesUploadTests.Upload_cleans_up_blob_when_metadata_persistence_fails
**Test gaps:** No remaining focused file-upload/security gap in this slice.

_Strong security posture: opaque server key, deny-by-default content type allowlist, size cap and orphan-blob compensation are covered._

### File download (stream, IDOR/404) — 🟢 minor-gaps
*Stream a file's bytes with stored content-type to its owner; foreign id is a 404.*

**Use cases:** Owner downloads their file; Intruder gets 404, not 403/leak

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Foreign user downloads another's file | ✓ | GetFileHandler.cs:22 WHERE Id && UserId + RLS; FilesUploadTests.A_different_user_cannot_download_another_users_file asserts 404 |
| RLS-disabled deployment | ✓ | FilesUploadTests.File_download_is_owner_scoped_at_the_app_layer_even_when_rls_is_bypassed dispatches under system context with a foreign id and asserts NotFoundException |
| Metadata row exists but the blob is missing (orphaned/expired/manually deleted) | ✓ | LocalFileStorage and S3FileStorage map missing blobs to NotFoundException file.not_found, so the HTTP path returns a clean 404; proven by FilesUploadTests.Download_returns_404_when_metadata_exists_but_blob_is_missing. |
| Response not wrapped in ApiResponse JSON envelope | ✓ | DownloadFileEndpoint.cs:29 returns Results.Stream directly with the stored content-type/filename |
| Unauthenticated download | ✓ | RequireAuthorization() + UnauthorizedException at DownloadFileEndpoint.cs:25-26 |

**Testy:** FilesUploadTests.Upload_then_download_round_trips_the_same_bytes_and_content_type; FilesUploadTests.A_different_user_cannot_download_another_users_file; FilesUploadTests.File_download_is_owner_scoped_at_the_app_layer_even_when_rls_is_bypassed; FilesUploadTests.Download_returns_404_when_metadata_exists_but_blob_is_missing; FilesUploadTests.Download_of_a_nonexistent_file_id_returns_404_not_500
**Test gaps:** No remaining focused file-download/404 gap in this slice.

_IDOR protection is dual-gated and well tested. Missing metadata and missing blob both return the same clean 404 shape._

### File list (paged, owner-scoped) — ✅ correct
*Return a paged, newest-first list of the caller's own file metadata.*

**Use cases:** User lists their files with page/pageSize; Other users see an empty list

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Owner scoping | ✓ | ListFilesHandler.cs:20 WHERE UserId + RLS; FilesUploadTests.List_is_paged_and_owner_scoped asserts other user totalCount==0 |
| Unbounded / negative / oversized page request | ✓ | PageRequest (Paging.cs:21-29) clamps page>=1 and pageSize 1..100 default 20 — a caller cannot request an unbounded page |
| Stable ordering for paging | ✓ | ListFilesHandler.cs:30-31 orders by CreatedAt desc, then Id desc as a deterministic tie-breaker; PagedQueryExtensions requires an ordered query |
| Only metadata returned, never bytes | ✓ | ListFilesHandler.cs:22 projects FileListItem (no StorageKey, no content) |
| Index supports the per-user ordered scan | ✓ | FileObjectConfiguration defines IX_file_objects_UserId_CreatedAt_Id with CreatedAt/Id descending, matching ListFilesHandler's UserId filter + CreatedAt/Id order; AddFileObjectListIndex migration replaces the old UserId-only index. |
| CreatedAt tie ordering is deterministic | ✓ | ThenByDescending(Id) prevents two same-timestamp files from swapping between pages; covered by FilesUploadTests.List_orders_created_at_ties_by_id_for_stable_paging. |

**Testy:** FilesUploadTests.List_is_paged_and_owner_scoped (pageSize, totalCount, items length, owner scoping); FilesUploadTests.List_clamps_page_parameters_and_orders_newest_first_across_pages (query-string clamping + newest-first across pages); FilesUploadTests.List_orders_created_at_ties_by_id_for_stable_paging
**Test gaps:** No remaining focused file-list paging/order/index gap.

_Paging is clamped and owner-scoped. Ordering is deterministic and the file_objects index now matches the per-user newest-first query._

### Blob storage providers (local + S3) & path-traversal guard — ✅ correct
*Persist/retrieve/delete bytes behind IFileStorage; local disk for dev, S3-compatible (AWS/MinIO/R2) for prod; reject path-traversal keys.*

**Use cases:** Dev round-trips bytes on local disk; Prod points at AWS S3, MinIO, or Cloudflare R2 via config only; Same opaque key works across providers

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Path-traversal / rooted / backslash / invalid-char key | ✓ | StorageKey.Validate (StorageKey.cs:17-25) rejects '..', leading slash/backslash, embedded backslash, rooted, and invalid chars; LocalFileStorage.ResolvePath re-checks the resolved path stays under root and rejects existing symlink/reparse-point parents (LocalFileStorage.cs:60-86); S3FileStorage validates on every call (lines 26,40,47). Tested by StorageUnitTests.Invalid_storage_keys_are_rejected and Local_provider_rejects_key_whose_existing_parent_symlink_escapes_root |
| Bucket/endpoint redirected by a request (SSRF/cross-bucket write) | ✓ | S3FileStorage.cs:21 bucket from options only; StorageOptions doc (StorageOptions.cs:4-7) — never from a request |
| Provider selection / default | ✓ | PlatformStorage.cs:20-32 selects s3 vs local, default local; TryAddSingleton idempotent across modules |
| Delete is idempotent (missing key) | ✓ | LocalFileStorage.cs:46-50 guards File.Exists; S3 DeleteObject is idempotent by S3 semantics; IFileStorage doc (Ports.cs:80) requires it |
| GetAsync on a missing key | ✓ | LocalFileStorage.cs:37-41 throws NotFoundException('file.not_found') instead of a provider-native exception; pinned by StorageUnitTests.Local_missing_key_throws_file_not_found_error_code. S3FileStorage mirrors the same code path around missing blobs (S3FileStorage.cs:48-53). |
| S3 stream lifecycle (AutoCloseStream) | ✓ | S3FileStorage.cs:33 AutoCloseStream=false so the caller-owned upload stream is not closed by the SDK; download returns response.ResponseStream which Results.Stream disposes |
| Live S3 round-trip | – | StorageUnitTests note: a live S3 round-trip needs a MinIO Testcontainer/real bucket and is intentionally not covered; only config wiring + key guard are unit-tested |

**Testy:** StorageUnitTests.Invalid_storage_keys_are_rejected; StorageUnitTests.Opaque_keys_are_accepted; StorageUnitTests.Local_missing_key_throws_file_not_found_error_code; StorageUnitTests.Local_provider_rejects_key_whose_existing_parent_symlink_escapes_root; StorageUnitTests.S3_config_for_minio_uses_service_url_and_path_style; StorageUnitTests.S3_config_for_real_s3_uses_region_not_service_url
**Test gaps:** No live S3/MinIO round-trip test (acknowledged)

_Defence-in-depth path guard (validate + resolved-path re-check) is excellent; S3 config is unit-tested across all three backends._

### Files GDPR export + erasure — 🟢 minor-gaps
*Export the subject's file inventory; on erasure delete the user's blobs and metadata outright (no retention).*

**Use cases:** GDPR /me/export includes file inventory; GDPR /me/erase removes blobs + metadata

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Erasure is idempotent / retryable (multi-transaction fan-out) | ✓ | FilesPersonalDataEraser.cs:24-34 re-run finds no rows and DeleteAsync is a no-op for already-removed blobs (documented at lines 13-16) |
| Runs under system context (no tenant) — must still match the user's rows | ✓ | FilesPersonalDataEraser doc lines 14-15: tenant filter does not restrict; the WHERE UserId == userId matches; FilesUploadTests.Gdpr_erasure_deletes_the_users_files_and_metadata verifies rows reach 0 after shred |
| Blob delete fails mid-loop (e.g. S3 transient) leaving metadata referencing a partially-deleted set | ✓ | FilesPersonalDataEraser.cs:29-34 deletes blobs in a loop THEN ExecuteDeleteAsync the rows; if a blob DeleteAsync throws partway, the exception propagates before metadata deletion, so a retry re-deletes already-gone blobs (no-op) and finishes. Proven by FilesUploadTests.Gdpr_erasure_keeps_metadata_when_blob_delete_fails_so_retry_can_finish. |
| Blob delete order changes after index/query-plan changes | ✓ | FilesPersonalDataEraser orders by FileName then StorageKey before deleting blobs, so retry behaviour is deterministic and not dependent on whichever DB index the planner chooses. |
| Export omits raw bytes (only metadata) | ✓ | FilesPersonalDataExporter.cs:8-12 doc + projection returns id/filename/contentType/size/createdAt only |
| Ports registered so the DI-driven fan-out actually runs | ✓ | FilesModule.cs:46-47 registers both IExportPersonalData and IErasePersonalData (doc warns omission makes files immortal) |
| ExecuteDeleteAsync bypasses audit/xmin | ✓ | FilesPersonalDataEraser.cs:34 uses ExecuteDeleteAsync — intentional for a GDPR scrub (consistent with the platform caveat that scrubs may bypass the interceptor); file_objects are not an append-only retained ledger |

**Testy:** FilesUploadTests.Gdpr_erasure_deletes_the_users_files_and_metadata (blobs+rows gone after subject-key shred); FilesUploadTests.Gdpr_erasure_keeps_metadata_when_blob_delete_fails_so_retry_can_finish; FilesUploadTests.Gdpr_export_contains_file_inventory_and_links_without_raw_bytes (DI-registered exporter returns files + fileLinks metadata, not storage keys/raw bytes)
**Test gaps:** No remaining focused Files GDPR export/erasure port gap in this slice.

_Erasure correctly deletes (not anonymizes), is idempotent, and keeps metadata until every blob delete succeeds so fan-out retry can finish._

**Nekonzistence v oblasti (6):**
- Operations.Tests contains RealtimeReplayTests.cs and RealtimeSseTests.cs which test the Realtime building block, not the Operations module — cross-cutting tests are co-located in the Operations test project (organizational drift, not a bug).


---

## Persistence & CQRS cross-cutting

### CQRS dispatcher + pipeline ordering — ✅ correct
*In-proc command/query dispatch through an ordered open-generic behavior chain to a single handler.*

**Use cases:** Send a write command (ICommand<T>); Run a read query (IQuery<T>); Apply cross-cutting behaviors (telemetry/logging/validation/concurrency-retry) uniformly

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Query must skip command-only behaviors (concurrency-retry, etc.) | ✓ | Dispatcher.cs:80-82 filters out ICommandOnlyBehavior for queries; ConcurrencyRetryBehavior marks ICommandOnlyBehavior (ConcurrencyRetryBehavior.cs:16) |
| Registration order must equal execution order (outer-most first) | ✓ | Dispatcher folds behaviors in reverse so registration order==execution order (Dispatcher.cs:51-58); host order is Telemetry (Telemetry/DI.cs:17) -> Logging+Validation (PlatformWebExtensions.cs:44-45) -> ConcurrencyRetry (Persistence/DI.cs:48) |
| Per-call reflection on hot path | ✓ | Wrapper type is cached per concrete request type in a ConcurrentDictionary (Dispatcher.cs:13-14,19-22) |
| Null command/query | ✓ | ArgumentNullException.ThrowIfNull at Dispatcher.cs:18,28 |
| Missing handler registration | ✓ | GetRequiredService throws a clear DI exception (Dispatcher.cs:47,75) |
| Behavior registered once per module would nest N-deep (5^N retry amplification) | ✓ | AddPipelineBehavior uses TryAddEnumerable dedup by (serviceType,implType) (DependencyInjection.cs:40-44); documented rationale lines 41-43 |

**Testy:** PipelineBehaviorRegistrationTests.ConcurrencyRetryBehavior_is_registered_once_regardless_of_module_count; DispatcherPipelineTests.Command_pipeline_runs_behaviors_in_registration_order; DispatcherPipelineTests.Query_pipeline_skips_command_only_behaviors_but_preserves_order
**Test gaps:** No remaining focused dispatcher/pipeline-order gap in this slice.

_Clean ~150 LOC mediator replacement. Runtime tests now pin both execution order and the query exclusion of command-only behaviors._

### Validation behavior — ✅ correct
*Run all FluentValidation validators for a request, aggregate failures into one RFC9457 400.*

**Use cases:** Validate command/query inputs before the handler; Surface per-field error codes for i18n

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| No validators registered | ✓ | Short-circuits to next() (ValidationBehavior.cs:16-19) |
| Validator with blank ErrorCode | ✓ | Falls back to 'validation.invalid' (ValidationBehavior.cs:29); covered by ValidationBehaviorTests.Multiple_validators_are_aggregated_and_blank_error_code_falls_back |
| Multiple validators / parallel run | ✓ | Task.WhenAll over all validators, each with its own ValidationContext so FluentValidation state cannot be shared across validators (ValidationBehavior.cs:22-23); covered by ValidationBehaviorTests.Multiple_validators_are_aggregated_and_blank_error_code_falls_back |
| Runs for queries too (read-safe) | ✓ | Does NOT implement ICommandOnlyBehavior, so it runs for both commands and queries (intended per docstring lines 8-9); pinned at runtime by DispatcherPipelineTests.Query_pipeline_runs_validation_behavior_before_the_handler |

**Testy:** ValidationBehaviorTests.No_validators_calls_next; ValidationBehaviorTests.Multiple_validators_are_aggregated_and_blank_error_code_falls_back; DispatcherPipelineTests.Query_pipeline_runs_validation_behavior_before_the_handler; ErrorCodeLocalizationTests (asserts error codes resolve to resx) — indirect; Module slice validators tested in module integration tests
**Test gaps:** No remaining focused validation-behavior gap in this slice.

_Thin and correct; the important invariant is one ValidationContext per validator, otherwise FluentValidation state can leak between parallel validators._

### Error types -> HTTP mapping — ✅ correct
*Typed business exceptions carrying a stable errorCode + HTTP status, translated to RFC9457 by the Web middleware.*

**Use cases:** throw ConflictException/NotFoundException/etc. from handlers; Map errorCode==resx key for localized detail; Carry per-field ValidationError list

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Cqrs must not depend on ASP.NET | ✓ | Status codes are local int constants (Errors.cs:62-70), not Microsoft.AspNetCore.Http.StatusCodes |
| Validation aggregate vs single business error | ✓ | ValidationException carries IReadOnlyList<ValidationError> (Errors.cs:17-23); others carry a single code |
| 422 business-rule vs 409 conflict separation | ✓ | BusinessRuleException=422 (Errors.cs:65-70), ConflictException=409 (Errors.cs:32-37). Proven by ModularPlatformExceptionTests.Exception_subtypes_map_to_the_documented_http_status_codes. |

**Testy:** ErrorCodeLocalizationTests (every errorCode used has en+cs resx entries); ModularPlatformExceptionTests.Exception_subtypes_map_to_the_documented_http_status_codes; ModularPlatformExceptionTests.Validation_exception_carries_validation_failed_code_and_field_errors

_errorCode==resx-key and status-code mapping are now both covered by focused tests._

### Logging behavior (PII-safe) — ✅ correct
*Structured request/response/error log with elapsed time, never logging the request body.*

**Use cases:** Observe command/query latency; Downgrade expected business errors to warning, real failures to error

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| PII in request body | ✓ | Logs only typeof(TRequest).Name, never the body (LoggingBehavior.cs:8,15) |
| Expected business error vs unexpected exception | ✓ | ModularPlatformException -> LogWarning with ErrorCode (LoggingBehavior.cs:24-29); other -> LogError with stack (31-36); pinned by LoggingBehaviorTests. |

**Testy:** LoggingBehaviorTests.Successful_request_logs_type_and_elapsed_time_without_request_body; LoggingBehaviorTests.Business_exception_is_logged_as_warning_with_error_code; LoggingBehaviorTests.Unexpected_exception_is_logged_as_error_with_exception
**Test gaps:** No remaining focused logging-behavior gap in this slice.

_The PII-safe logging contract is now covered directly: request values stay out of logs, business errors are warnings, unexpected failures are errors with exception details._

### xmin optimistic concurrency + ConcurrencyRetryBehavior — 🟢 minor-gaps
*Detect concurrent writes via Postgres xmin token and retry the whole command up to 5x with backoff.*

**Use cases:** Serialize confirm/release/expire/top-up on tracked entities; Recover transparently from lost-update conflicts

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Stale sibling entities after partial reload | ✓ | ChangeTracker.Clear() before each retry so the whole handler re-queries fresh (`Behaviors/ConcurrencyRetryBehavior.cs:37-38`; docstring 8-12); pinned by ConcurrencyRetryBehaviorTests.Retries_after_concurrency_conflict_and_clears_the_change_tracker_before_rerun |
| Retry exhaustion (>5 conflicts) | ✓ | when(attempt<MaxRetries) guard (`Behaviors/ConcurrencyRetryBehavior.cs:30`) lets the 6th DbUpdateConcurrencyException propagate as a 5xx — intentional give-up; pinned by ConcurrencyRetryBehaviorTests.Gives_up_after_max_retries_and_surfaces_the_concurrency_exception |
| Queries hitting the retry loop | ✓ | ICommandOnlyBehavior excludes it from the query pipeline (line 16) |
| Cooldown on conflict storm / backoff | ✓ | Exponential delay 50ms*2^(n-1) (line 40) |
| Idempotency (DbUpdateException, not concurrency) leaking into retry | ✓ | Only catches DbUpdateConcurrencyException; idempotency handled in handlers via catch DbUpdateException (per CLAUDE.md money rules) |

**Testy:** PipelineBehaviorRegistrationTests (single-registration); ConcurrencyRetryBehaviorTests.Retries_after_concurrency_conflict_and_clears_the_change_tracker_before_rerun; ConcurrencyRetryBehaviorTests.Gives_up_after_max_retries_and_surfaces_the_concurrency_exception; BillingConcurrencyTests.Concurrent_reservations_never_exceed_balance (money path, but that path uses the atomic ExecuteUpdate guard, not xmin retry)
**Test gaps:** No remaining focused ConcurrencyRetryBehavior gap in this slice.

_Logic is correct and now directly regression-tested at the retry behavior layer; the headline billing concurrency test separately covers the money ExecuteUpdate guard._

### Audit interceptor (changed-fields, stamps, converter values) — 🟢 minor-gaps
*On SaveChanges, stamp Created/Updated and write one per-module audit row capturing only changed columns as JSONB.*

**Use cases:** Forensic trail per entity; Capture provider-converted values (enum-as-string) not raw ints; Stamp CreatedBy/UpdatedBy from token

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Auditing the audit rows (recursion) | ✓ | Snapshots entries before adding audit rows and excludes AuditEntry (AuditInterceptor.cs:64-66) |
| Update records all columns instead of changed-only | ✓ | ChangedColumns/ValueMap filter on p.IsModified for Update (lines 107,139-146); covered by AuditInterceptorTests.Update_audit_row_records_changed_columns_only |
| HasConversion<string>() enum audited as its int (PL-2) | ✓ | ProviderValue reads GetValueConverter() ?? FindTypeMapping().Converter (lines 184-186); documented at 181-183 |
| Created/Updated stamps missing or wrong principal/time | ✓ | AuditInterceptor.cs:76-84 stamps AuditableEntity on Added/Modified from ITenantContext + IClock; covered by AuditInterceptorTests.Auditable_entities_are_stamped_on_create_and_update_from_the_current_context |
| ExecuteUpdate/ExecuteDelete bypass the interceptor | ◐ | Documented limitation (CLAUDE.md, EncryptedAttribute docstring) — by design used only where the change need not be audited; no compile-time guard prevents a careless ExecuteUpdate on an audited table |
| Composite / missing primary key | ✓ | PrimaryKey joins multi-prop keys; empty string when no key (lines 124-134) |
| PII in audit values | ✓ | See Audit-PII crypto-shred feature |

**Testy:** AuditPiiEncryptionTests (Create row exists, NewValues is enveloped) — proves rows are written; LedgerBackstopTests.PL2; AuditInterceptorTests.Auditable_entities_are_stamped_on_create_and_update_from_the_current_context; AuditInterceptorTests.Update_audit_row_records_changed_columns_only
**Test gaps:** No remaining focused audit-interceptor unit gap in this slice.

_Correct and carefully written: changed-only capture, converter resolution and stamps are now covered._

### Audit IP masking (data minimization) — ✅ correct
*Apply Full/Truncated/None policy to the client IP recorded on each audit row.*

**Use cases:** GDPR data-minimization of audit IPs; Coarse forensic attribution without storing full address

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| IPv4-mapped IPv6 from dual-stack Kestrel collapsing to '::' | ✓ | MapToIPv4 before /24 (AuditOptions.cs:57-60) |
| Unparseable address under Truncated | ✓ | Dropped to null rather than stored verbatim (lines 49-51) |
| Empty string vs null input | ✓ | Empty returns empty, null returns null under Full/empty branch (lines 43-44) |
| IPv6 /48 truncation | ✓ | Zero hextets from byte 6 (lines 69-72) |
| Rate-limit partition truncation collapsing 256 clients | ✓ | Documented that ONLY the audit IP is masked, rate-limit keeps full address (AuditOptions.cs:24-25) |

**Testy:** AuditIpMaskingTests: Full_keeps_verbatim, None_drops, Truncated_zeroes_ipv4_host_octet, Truncated_keeps_ipv6_routing_prefix, Truncated_normalizes_ipv4_mapped_ipv6, Truncated_drops_unparseable_or_empty; AuditInterceptorTests.Audit_ip_storage_config_changes_the_stored_audit_ip_address
**Test gaps:** No remaining focused audit-IP minimization gap in this slice.

_Best-tested unit in the area; the dual-stack mapping edge case and config-to-stored-audit-row path are explicitly covered._

### Tenant query filter + TenantStampingInterceptor — ✅ correct
*Defence-in-depth tenant scoping: stamp TenantId on insert, filter reads to the caller's tenant (or system).*

**Use cases:** Multi-tenant row isolation on ITenantScoped entities; System hosts (worker/jobs) bypass the filter

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Missing tenant claim = cross-tenant leak (old null short-circuit) | ✓ | Filter is IsSystemContext \|\| TenantId==CurrentTenantId with NO null escape (PlatformDbContext.cs:96-98); documented 93-95 |
| Stamping overwrites an explicit cross-tenant assignment (registration creating tenant+first user) | ✓ | Only fills TenantId when CurrentValue is null AND a tenant is in context (TenantStampingInterceptor.cs:42-45); pinned by TenantStampingInterceptorTests.SavingChanges_does_not_overwrite_explicit_tenant_id |
| Tenant + soft-delete filters overwriting each other | ✓ | EF Core 10 named query filters keyed 'Tenant'/'SoftDelete' coexist (PlatformDbContext.cs:60,65,88); pinned by PlatformDbContextQueryFilterTests.Query_filters_apply_tenant_and_soft_delete_together and System_context_bypasses_tenant_filter_but_keeps_soft_delete_filter |
| An in-Api background Wolverine handler assumed tenant-scoped | ◐ | Documented as SYSTEM (CLAUDE.md) but relies on the host wiring HttpTenantContext/SystemTenantContext correctly; not enforced in this layer |

**Testy:** TenantIsolationTests.Two_users_land_in_distinct_tenants_in_the_same_users_table; A_user_reading_through_the_tenant_filter_sees_only_their_own_tenant_data; Anonymous_caller_with_no_tenant_claim_is_rejected_not_granted_global_visibility; TenantStampingInterceptorTests.SavingChanges_does_not_overwrite_explicit_tenant_id; PlatformDbContextQueryFilterTests.Query_filters_apply_tenant_and_soft_delete_together; PlatformDbContextQueryFilterTests.System_context_bypasses_tenant_filter_but_keeps_soft_delete_filter
**Test gaps:** No remaining focused tenant-filter/stamping gap in this slice.

_The closed null-escape is the load-bearing security fix and is well covered; the non-overwrite stamping branch and tenant+soft-delete coexistence now have focused building-block tests._

### Postgres RLS (bootstrap, dual role, GUC stamping) — ✅ correct
*DB-level row isolation on IUserOwned tables keyed on app.principal_id, run by a least-privilege role.*

**Use cases:** Hard isolation even if a WHERE UserId is forgotten; System principals bypass via app.is_system; Auto-protect any table a module marks IUserOwned

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Superuser/table-owner bypasses RLS | ✓ | Runtime data connection uses least-privilege app_rls role (NOSUPERUSER NOBYPASSRLS, RlsBootstrapper.cs:106-109); admin only for DDL/Wolverine |
| Pooled connection reused across principals leaks GUCs | ✓ | PrincipalSessionConnectionInterceptor stamps on EVERY ConnectionOpened, is_local=false session scope (PrincipalSessionConnectionInterceptor.cs:32-39; docstring 8-13); proven on one open runtime-role connection by RlsTests.Principal_guc_is_restamped_when_a_runtime_connection_is_reused_for_another_user |
| Anonymous/empty principal matching rows | ✓ | NULLIF(current_setting(...,true),'')::uuid => unset principal matches nothing (RlsBootstrapper.cs:131-133) |
| Dev placeholder password in prod | ✓ | Fail-fast outside Development (RlsBootstrapper.cs:42-49) |
| SQL injection via role name | ✓ | SafeIdentifier regex validates RuntimeRole (RlsBootstrapper.cs:35-37); table identifiers come from EF model, password is ''-escaped (line 100) |
| FORCE RLS blocks a future DATA migration on an IUserOwned table run by admin | ✓ | Documented that admin should have BYPASSRLS; migrations run on admin conn via PlatformMigrator (CLAUDE.md §7) |
| Wolverine outbox tables created later not granted to runtime role | ✓ | Pre-creates wolverine schema + ALTER DEFAULT PRIVILEGES so later tables auto-grant (RlsBootstrapper.cs:112-124) |
| RLS disabled deployment (managed DB, no role creation) | ✓ | Enabled=false uses admin conn for data, no policies (RlsOptions.cs:15-16; RlsConnectionString.cs:18-21; RlsBootstrapper.cs:29-33) |
| System principal must bypass user-owned policy without admin role | ✓ | Policy checks app.is_system='on' (RlsBootstrapper.cs:131-133); proven through runtime role by RlsTests.System_principal_bypasses_user_owned_rls_without_using_the_admin_role |

**Testy:** RlsTests.A_user_cannot_see_another_users_credit_account_even_with_a_raw_query (owner sees own, other sees zero, admin sees both); RlsTests.System_principal_bypasses_user_owned_rls_without_using_the_admin_role; RlsTests.Principal_guc_is_restamped_when_a_runtime_connection_is_reused_for_another_user; RlsTests.Rls_disabled_host_can_read_user_owned_data_through_the_admin_connection
**Test gaps:** No remaining focused RLS bootstrap/runtime-role gap in this slice.

_Comprehensive, hardened implementation; cross-principal read denial, system bypass and same-connection principal restamping are proven end-to-end._

### Read DbContext factory — ✅ correct
*No-tracking module DbContext on the read replica (or write fallback), with RLS GUC stamping when enabled.*

**Use cases:** Scale read queries on a replica; Keep reads tenant/principal-isolated at the DB

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| No replica configured | ✓ | ModuleConnectionStrings.GetWriteAndRead treats missing/blank Read as Write; pinned by HostBootTests.Module_read_context_falls_back_to_write_connection_when_no_read_replica_is_configured |
| Reads bypassing RLS (no interceptor on read path) | ✓ | Adds PrincipalSessionConnectionInterceptor when rls.Enabled (ReadDbContextFactory.cs:29-32); runtime role derived via RlsConnectionString in AddModuleReadDbContext (DI.cs:64-66); pinned by ReadDbContextFactoryTests.AddModuleReadDbContext_uses_runtime_role_and_stamps_principal_when_rls_is_enabled |
| RLS-disabled deployment still uses admin connection and no GUC interceptor | ✓ | RlsConnectionString.ForRuntime returns the admin connection when disabled (RlsConnectionString.cs:15-18); pinned by ReadDbContextFactoryTests.AddModuleReadDbContext_keeps_admin_connection_and_skips_principal_interceptor_when_rls_is_disabled |
| Decryption on the interceptor-free read path | ✓ | PersonalDataDecryptingConverter is baked into the cached model, so reads decrypt without an interceptor (PlatformDbContext.cs:70-77; PersonalDataEncryption.cs docstring) |
| No audit/concurrency interceptors on reads | ✓ | Factory deliberately omits them — reads only (docstring line 10) |

**Testy:** ReadDbContextFactoryTests (runtime role + PrincipalSessionConnectionInterceptor when RLS enabled; admin connection + no principal interceptor when disabled); HostBootTests.Module_read_context_falls_back_to_write_connection_when_no_read_replica_is_configured; RlsTests/TenantIsolationTests exercise the read path implicitly via /me and credit queries
**Test gaps:** No remaining focused read-factory fallback/wiring gap in this slice.

_Read-side RLS isolation is the subtle correctness point; the factory registration and write fallback are now pinned directly and still exercised through module integration tests._

### PII at rest: encryption interceptor + decrypting converter — ✅ correct
*Seal [Encrypted] columns under the subject DEK on write; decrypt transparently on read; erasure shreds the key.*

**Use cases:** Encrypt users.Email at rest; Decrypt on both write context and read factory; Surface [erased] after DEK shred

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Save without a protector silently persisting plaintext | ✓ | Throws InvalidOperationException refusing to persist PII (PersonalDataEncryption.cs:172-175) |
| Interceptor ordering vs audit (DEK landing in audit) | ✓ | Encryption interceptor registered AFTER audit so audit sees+protects plaintext itself (DependencyInjection.cs:44-49; docstring 76-85) |
| Double-encrypting an already-protected value | ✓ | Skips values where LooksProtected (penc:v prefix) (PersonalDataEncryption.cs:166-167) |
| Unchanged property re-encrypted/re-written | ✓ | Skips !IsModified on Modified; restores plaintext + IsModified=false after save (lines 160-161,197-203) |
| Tracked instance left as ciphertext after save | ✓ | RestorePlaintext on success AND failure (SavedChanges/SaveChangesFailed overrides, lines 108-132) |
| Converter has no DI (cached model) | ✓ | Reads protector from static volatile accessor set by PersonalDataEncryptionBootstrap before seeders run (PersonalDataEncryption.cs:19-44) |
| Shredded subject on read | ✓ | TryReveal false -> ErasedMarker (PersonalDataEncryption.cs:70-72) |
| ExecuteUpdate/Delete bypass encryption | ◐ | Documented; used deliberately for erasure tombstones (EncryptedAttribute docstring); no guard against accidental misuse |

**Testy:** AuditPiiEncryptionTests (audit PII enveloped + erased); PersonalDataConventionTests.Every_Encrypted_property_is_PersonalData_on_an_IDataSubject_string; PersonalDataEncryptionInterceptorTests.Failed_save_restores_tracked_encrypted_property_to_plaintext; PersonalDataEncryptionInterceptorTests.Save_of_encrypted_plaintext_without_a_protector_is_rejected; Identity AuditPiiEncryption/PiiEncryption integration tests (login via blind index, penc:v2 envelopes)
**Test gaps:** No remaining focused PII encryption interceptor restore/refuse gap in this slice.

_Intricate but disciplined; restore-on-failure and refuse-without-protector are now pinned directly in building-block tests._

### Paging (PageRequest / PagedResponse / ToPagedResponseAsync) — 🟢 minor-gaps
*Clamp paging inputs to safe bounds and turn an ordered IQueryable into a counted page.*

**Use cases:** List endpoints return bounded pages; Prevent unbounded/negative page requests

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Negative/zero page or page size | ✓ | Page defaults to 1 if not >0; PageSize Math.Clamp(1..100) (Paging.cs:23-24) |
| Oversized page size (DoS) | ✓ | Clamped to MaxPageSize=100 (Paging.cs:19,24) |
| TotalPages with zero/negative page size | ✓ | Returns 0 when PageSize<=0 (Paging.cs:9) |
| Unordered query => unstable paging | ◐ | Documented requirement to OrderBy first (PagedQueryExtensions.cs:10-11) but not enforced in code |
| Count + page in two round-trips (consistency) | ✓ | LongCountAsync then Skip/Take (PagedQueryExtensions.cs:16-17); acceptable for paging and directly covered by PagingClampingTests.ToPagedResponseAsync_counts_total_and_preserves_ordered_page |

**Testy:** PagingClampingTests directly covers PageRequest clamping (negative/null page, null/below-one/oversized pageSize, Skip), PagedResponse.TotalPages math, and PagedQueryExtensions.ToPagedResponseAsync preserving ordered EF page items + total count; NotificationsIntegrationTests exercises the feed PagedResponse envelope (items/page/pageSize/totalCount) end-to-end
**Test gaps:** No remaining focused paging test gap; unordered IQueryable stability remains a documented caller contract, not enforced by code.

_Clamp/math and the EF extension wrapper are now covered by focused building-block tests._

### Entity base + conventions (xmin, soft-delete, IUserOwned/ITenantScoped) — 🟢 minor-gaps
*Boilerplate-free entities: Guid v7 ids, xmin concurrency, shadow TenantId, soft-delete filter, auto-config scan.*

**Use cases:** Derive Entity/AuditableEntity; Mark ITenantScoped/IUserOwned/ISoftDeletable to opt into platform behaviors

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| xmin token on every Entity without a RowVersion column | ✓ | Shadow uint 'xmin' xid concurrency token by convention (PlatformDbContext.cs:45-54) |
| Soft-deleted rows leaking into reads | ✓ | Global filter DeletedAt==null on ISoftDeletable (PlatformDbContext.cs:101-105) |
| IUserOwned entity without a UserId column | ✓ | `RlsConventionTests.Every_IUserOwned_entity_exposes_a_Guid_UserId` scans module assemblies and fails if an `IUserOwned` concrete type lacks a public `Guid UserId`, catching the RLS owner-column contract before startup/bootstrap. |

**Testy:** RlsTests (IUserOwned -> policy), TenantIsolationTests (ITenantScoped -> filter), module soft-delete usage, RlsConventionTests.Every_IUserOwned_entity_exposes_a_Guid_UserId
**Test gaps:** No remaining build-time gap for the `IUserOwned` owner-column convention.

_Conventions are solid; the `IUserOwned` -> `Guid UserId` contract is now enforced by an architecture test before RLS bootstrap._

---

## Messaging, hosts, jobs & Web

### Wolverine durable messaging configuration (PlatformMessaging.Configure) — ✅ correct
*Single central Wolverine setup: Postgres transport+persistence, outbox/inbox, sagas, retry→DLQ, service-location, PII retention, per-host listen/solo modes.*

**Use cases:** Atomic outbox publish of integration events from command handlers; Cross-module event consumption in the Worker (durable, deduped); EF-persisted sagas (CreditPurchaseSaga) via UseEntityFrameworkCoreTransactions; Single-node hosts (tests/dev) draining the durable queue via Solo mode; Bounded PII lifetime in durable envelopes (reaped/expired)

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Wolverine 6 ServiceLocationPolicy.NotAllowed silently skips handlers that resolve scoped IDispatcher | ✓ | Explicitly set AlwaysAllowed at PlatformMessaging.cs:55; documented as #1 gotcha |
| Single short-lived node never wins leadership → durable queue never drained | ✓ | Solo mode at PlatformMessaging.cs:57-60; Api sets soloMode for Testing/single-instance (Api Program.cs:46-48), Worker defaults true (WorkerHostBuilder.cs:57) |
| Transient handler failure must not lose the message | ✓ | RetryWithCooldown(100ms,500ms,3s).Then.MoveToErrorQueue() at PlatformMessaging.cs:84-86; inbox UNIQUE(MessageId) → ~exactly-once |
| PII in opaque durable JSON cannot be crypto-shredded in place | ✓ | KeepAfterMessageHandling=5m, DeadLetterQueueExpiration enabled=7d at PlatformMessaging.cs:76-78; asserted by HostBootTests.AssertPiiRetention |
| Slow default 5s poll lags event-driven work | ✓ | ScheduledJobPollingTime=1s at PlatformMessaging.cs:65 |
| Pure-publisher host (Jobs/Migration/Balanced-Api) should not consume | ✓ | listen param defaults false; Jobs/Migration call Configure without listen (JobsHostBuilder.cs:64, MigrationHostBuilder.cs:48); Api passes listen: soloMode so Balanced Api publishes only (Program.cs:55). Pinned by HostMessagingArchitectureTests.Api_host_listens_to_wolverine_queue_only_when_solo_mode_is_enabled |
| Module handlers not discovered → events publish but never consumed | ✓ | Discovery.IncludeAssembly per module + module.ConfigureMessaging at PlatformMessaging.cs:101-107 |
| Worker process dies while a durable event handler is in-flight | ✓ | `WorkerDurabilityTests.EV4_worker_killed_mid_message_restarts_and_processes_the_durable_event_once` runs API as publisher-only, starts a real `ModularPlatform.Worker` child process, blocks `credit_accounts`, kills the worker mid-handler, restarts it, then observes exactly one provisioned account. |
| DLQ expiration permanently deletes a genuinely lost grant | ✓ | Comment PlatformMessaging.cs:73-75: grant recovery is via ReconcileStripe from live Stripe state, not the dead-letter; acceptable by design |

**Testy:** HostBootTests.AssertPiiRetention (DLQ expiration + KeepAfterMessageHandling on Worker/Jobs/Migration); WorkerDurabilityTests.EV4_worker_killed_mid_message_restarts_and_processes_the_durable_event_once; PlatformMessagingPolicyTests.Multiple_subscribers_are_combined_until_we_make_an_explicit_separated_decision (also pins ServiceLocationPolicy=AlwaysAllowed); PlatformMessagingPolicyTests.Durable_queue_polling_is_fast_enough_for_event_driven_work; PlatformMessagingPolicyTests.Solo_mode_is_enabled_only_for_single_node_hosts; HostMessagingArchitectureTests.Api_host_listens_to_wolverine_queue_only_when_solo_mode_is_enabled; DeadLetterTests.EV3_throwing_handler_dead_letters_after_retries_instead_of_silently_handling; CrossModuleEventTests (referenced in CLAUDE.md — proves end-to-end event delivery, lives in module tests)
**Test gaps:** No remaining focused durable-messaging configuration gap in this slice.

_Well-reasoned and regression-guarded for retention, service-location, retry-to-DLQ, Solo mode, API listen-mode wiring and polling cadence._

### Host composition & DI graph (Api/Worker/Jobs/Migration builders) — ✅ correct
*Each host discovers the same module set, registers identical cross-cutting + module services, and wires Wolverine consistently so DI graphs stay uniform and validatable.*

**Use cases:** Api HTTP host: telemetry→web→realtime→modules→Wolverine→/v1 group→health; Worker consumer host: core+telemetry+logging/validation+realtime+modules, listen=true; Jobs cron host: same graph + Quartz scheduling, publisher-only; MigrationService: applies migrations + RLS then exits

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Command-dispatch host pipeline order must be Telemetry→Logging→Validation→ConcurrencyRetry | ✓ | Api: AddPlatformTelemetry before AddPlatformWeb (Api Program.cs:29-30) and pinned by TelemetryBehaviorTests.Platform_registration_keeps_telemetry_behavior_outer_most; Worker/Jobs add Logging+Validation after Telemetry, before module ConcurrencyRetry (WorkerHostBuilder.cs:31-36, JobsHostBuilder.cs:32-36), pinned by HostBootTests.Worker_and_jobs_hosts_register_command_pipeline_behaviors_in_the_expected_order. MigrationService only composes modules for migrations/RLS and exits, so it is covered as a DI graph host, not as a command-dispatch host. |
| Non-HTTP host's module graph needs IRealtimePublisher (Notifications) or graph is unfulfillable | ✓ | AddPlatformRealtime in Worker(:37), Jobs(:40), Migration(:30); comments explain ValidateOnBuild is off outside Dev so this is latent |
| Missing ConnectionStrings:Write | ✓ | throw InvalidOperationException in every host (Api:34-35, Worker:52-53, Jobs:57-58, Migration:45-46) |
| Module set drift between hosts | ✓ | All four hosts enumerate the identical 6-module Discover call incl. Files even though Files has no jobs (JobsHostBuilder.cs:49-51 comment) |
| DI graph regressions undetectable by Api-only integration harness | ✓ | HostBootTests builds Worker/Jobs/Migration in Development → ValidateOnBuild+ValidateScopes catches captive/unresolvable deps (HostBootTests.cs:11-19) |
| One-host-per-process PII protector invariant | ✓ | HostBootTests deliberately Build but never Start (HostBootTests.cs:16-18); separate test assembly |
| Migrations applied at Api startup AND a deploy that turns RunMigrationsAtStartup off | ✓ | RLS bootstrapped in BOTH Api startup (Program.cs:62) and dedicated MigrationService (Program.cs:20) — comment MigrationService Program.cs:12-14 |

**Testy:** TelemetryBehaviorTests.Platform_registration_keeps_telemetry_behavior_outer_most; HostBootTests.Api_host_composes_and_its_dependency_graph_is_valid; HostBootTests.Worker_host_composes_and_its_dependency_graph_is_valid; HostBootTests.Jobs_host_composes_and_its_dependency_graph_is_valid; HostBootTests.Worker_and_jobs_hosts_register_command_pipeline_behaviors_in_the_expected_order; HostBootTests.MigrationService_host_composes_and_its_dependency_graph_is_valid
**Test gaps:** No remaining focused host-composition / command-pipeline-order gap in this slice.

_Strong: the boot tests are an explicit regression guard for the A4 DI-graph concern, and command-dispatch pipeline ordering is now pinned directly for Api/Worker/Jobs._

### Jobs host: Quartz cron (UTC), idempotency, single-instance posture — ✅ correct
*Cron-only host scheduling module jobs + platform messaging-health, with UTC cron and a documented single-instance/idempotent deployment model.*

**Use cases:** Module cron jobs (credit expiry, Stripe reconcile, GDPR retention) via RegisterJobs; Platform messaging-health probe every 5 min; Centralized job-failure observability

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Quartz defaults cron to host local timezone (violates Law #7 UTC) | ✓ | Health trigger uses InTimeZone(TimeZoneInfo.Utc) at JobsHostBuilder.cs:88; pinned by JobsHostWiringTests.Platform_messaging_health_cron_uses_utc |
| In-memory job store does not coordinate across instances | ◐ | Documented: run replica=1; every job idempotent so duplicate run is safe-but-wasteful (JobsHostBuilder.cs:69-74). NOT enforced — a misconfigured replica=2 silently double-runs; relies on operator discipline + per-job idempotency |
| A throwing job is silent until next cron fire | ✓ | JobFailureListener registered for AnyGroup (Jobs Program.cs:9-11) → ERROR log + platform.jobs.failures counter (JobFailureListener.cs:38-43); direct listener path proven by JobFailureMetricsTests.JobWasExecuted_with_exception_logs_and_records_the_failed_job |
| Graceful shutdown mid-job | ✓ | AddQuartzHostedService WaitForJobsToComplete=true (JobsHostBuilder.cs:90); pinned by JobsHostWiringTests.Jobs_host_waits_for_running_jobs_on_shutdown |
| Concurrent execution of the health job within one scheduler | ✓ | [DisallowConcurrentExecution] on MessagingHealthJob (line 20); pinned by JobsHostWiringTests.Platform_messaging_health_job_disallows_concurrent_execution |

**Testy:** JobFailureMetricsTests.Recording_a_failure_increments_the_platform_jobs_failures_counter; JobFailureMetricsTests.JobWasExecuted_with_exception_logs_and_records_the_failed_job; JobsHostWiringTests.Platform_messaging_health_job_disallows_concurrent_execution; JobsHostWiringTests.Platform_messaging_health_cron_uses_utc; JobsHostWiringTests.Jobs_host_waits_for_running_jobs_on_shutdown; Jobs_host_composes (HostBootTests)
**Test gaps:** No remaining focused Jobs host wiring gap in this slice.

_Single-instance cross-instance coordination is a documented operational constraint, not a code-enforced one — acceptable given universal idempotency but worth noting._

### Messaging-health job + evaluation — ✅ correct
*Probes Wolverine's durable store via IMessageStore.Admin, exports OTel gauges, warns on dead-letters / stuck outbox.*

**Use cases:** Alert on dead-letters (any >0); Alert on incoming/outgoing backlog over StuckThreshold; Continuous gauges for dashboards

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Watching Scheduled (saga timeouts) instead of Outgoing hides a stuck outbox and false-alarms | ✓ | Evaluate uses counts.Outgoing for outbox backlog, never Scheduled (MessagingHealthEvaluation.cs:40-44); root-caused in comment lines 10-14 |
| Dead-letter threshold | ✓ | Any DeadLetter>0 warns (MessagingHealthEvaluation.cs:28-32) |
| Incoming backlog threshold | ✓ | Incoming-pending branch warns independently from outgoing backlog; proven by MessagingHealthEvaluationTests.Incoming_pending_above_threshold_warns_separately_from_outgoing_backlog |
| ObservableGauge pull model vs per-fire job instance | ✓ | Static gauges registered once at class load; job refreshes static backing fields via Interlocked.Exchange (MessagingHealthJob.cs:27-62); pinned by MessagingHealthEvaluationTests.Job_refreshes_all_platform_messaging_gauge_values_from_store_counts |
| Queries Wolverine internal tables directly | ✓ | Always via IMessageStore.Admin.FetchCountsAsync (MessagingHealthJob.cs:54); documented constraint line 18 |
| Warnings are LogWarning only — no paging/alert routing | ◐ | By design alerting belongs to infrastructure (CLAUDE.md); the WARN + gauge is the signal. A deployment with no OTel/log alerting wired would not be paged — operational gap, not a code gap |

**Testy:** MessagingHealthEvaluationTests.A_stuck_outbox_is_reported_via_outgoing_not_scheduled; MessagingHealthEvaluationTests.Scheduled_messages_alone_do_not_raise_a_false_outbox_alarm; MessagingHealthEvaluationTests.Dead_letters_always_warn; MessagingHealthEvaluationTests.Incoming_pending_above_threshold_warns_separately_from_outgoing_backlog; MessagingHealthEvaluationTests.Job_refreshes_all_platform_messaging_gauge_values_from_store_counts
**Test gaps:** No remaining focused messaging-health job/evaluation gap in this slice.

_Pure evaluation cleanly extracted and unit-tested; the Scheduled-vs-Outgoing bug fix is well-guarded._

### RFC 9457 error contract + i18n (GlobalExceptionMiddleware + resx) — 🟢 minor-gaps
*Translates every exception into application/problem+json with stable errorCode title/type and Accept-Language-localized detail.*

**Use cases:** Domain exceptions → correct HTTP status + stable code clients branch on; Validation failures → errors[] extension; Unhandled → 500 error.unexpected (exception detail only in Development); en/cs localized detail via resx keyed by errorCode

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Resx manifest probing broke localization (ResourcesPath bug, PL-3) | ✓ | AddLocalization with NO ResourcesPath (PlatformWebExtensions.cs:36-40); namespace mirrors folder |
| errorCode has no resx entry | ✓ | detail.ResourceNotFound → falls back to safe `error.unexpected`, never `ex.Message` (GlobalExceptionMiddleware.cs:50); ArchitectureTests.ErrorCodeLocalizationTests fails the build if any thrown code lacks en+cs entry |
| Leaking internals in production 500s | ✓ | exception extension only when IsDevelopment (GlobalExceptionMiddleware.cs:85-88) |
| traceId correlation | ✓ | problem.Extensions['traceId']=context.TraceIdentifier (GlobalExceptionMiddleware.cs:73) |
| Validation failure response shape | ✓ | `errors[]` extension is pinned as an array of `{field,errorCode,message}` with stable `validation.failed` title/type/errorCode in GlobalExceptionMiddlewareTests.Validation_exception_returns_problem_details_with_errors_extension_shape |
| Response already started when exception thrown (e.g. mid-SSE-stream) | ✓ | Catch blocks check `Response.HasStarted` before writing ProblemDetails; middleware rethrows the original exception and leaves the already-started stream untouched (GlobalExceptionMiddleware.cs:32-51) |

**Testy:** PlatformContractTests.PL3_domain_errors_are_rfc9457_with_stable_code_and_localized_detail; ArchitectureTests.ErrorCodeLocalizationTests.Every_thrown_error_code_is_localized_in_en_and_cs; GlobalExceptionMiddlewareTests.Unhandled_exception_returns_safe_problem_details; GlobalExceptionMiddlewareTests.Development_500_includes_exception_extension; GlobalExceptionMiddlewareTests.Validation_exception_returns_problem_details_with_errors_extension_shape; GlobalExceptionMiddlewareTests.Started_response_rethrows_original_exception_without_overwriting_stream
**Test gaps:** No remaining focused RFC9457 error-contract gap in this slice.

_Solid contract with build-time i18n parity guard. Validation errors, production-safe 500s, dev exception detail, and started-response rethrow are now directly tested._

### Rate limiting (global + auth policy) — ✅ correct
*Partitioned token/fixed-window limiting: per-user (or per-IP) global bucket + tight per-IP 'auth' policy for credential endpoints.*

**Use cases:** Throttle abusive traffic per IP partition (anon); Brute-force defence on /login,/refresh (per-IP fixed window); Config-tunable limits per deployment; Stripe webhook opts out

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Authenticated users must each get their OWN bucket (per-user partition) | ✓ | PlatformWebExtensions keys authenticated requests by `ClaimTypes.NameIdentifier`, which TokenIssuer emits; PL11 proves user A exhausting a tiny bucket does not throttle user B's first request. |
| Reject status code | ✓ | RejectionStatusCode=429 (PlatformWebExtensions.cs:169) |
| Stripe webhook bursts from many IPs must never 429 | ✓ | StripeWebhookEndpoint.cs:94 .DisableRateLimiting(); asserted by PL9_ST5 test |
| Multi-instance distributed limiting | ◐ | In-memory PartitionedRateLimiter only; comment notes Redis-backed swap is the seam (PlatformWebExtensions.cs:171-172) but not implemented — limits are per-instance |
| Retry-After header on 429 | ✓ | `OnRejected` copies limiter lease `RetryAfter` metadata to the `Retry-After` header and returns an RFC9457-shaped 429 body; PL12 asserts the header exists. |

**Testy:** PlatformContractTests.PL9_ST5_low_limit_host_throttles_normal_traffic_but_never_the_stripe_webhook; PlatformContractTests.Register_endpoint_uses_the_auth_rate_limit_policy; PlatformContractTests.Login_endpoint_uses_the_auth_rate_limit_policy; PlatformContractTests.Password_reset_endpoints_use_the_auth_rate_limit_policy; PlatformContractTests.PL11_rate_limit_bucket_is_per_user_not_shared; PlatformContractTests.PL12_throttled_response_carries_retry_after
**Test gaps:** No remaining focused rate-limit policy gap in this slice.

_The previously documented per-user partition, Retry-After and login auth-policy gaps are covered. Multi-instance distributed limiting remains a deployment-scale seam, not an in-process correctness bug._

### Forwarded headers (proxy trust) + audit IP — ✅ correct
*Resolves real client IP behind a proxy for audit + rate-limiting, with a fail-fast trust-list validator in Production.*

**Use cases:** Correct client IP feeding audit IpAddress and the per-IP auth limiter; Block X-Forwarded-For spoofing behind a proxy; Disable cleanly for a host with no reverse proxy

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Empty trust list in Production trusts forwarded headers from anyone (spoofing) | ✓ | ForwardedHeadersSettingsValidator fails fast when Enabled && !HasTrustList outside Dev (validator lines 38-45); ValidateOnStart |
| Malformed proxy IP / CIDR | ✓ | TryParse guards fail in ANY environment with a clear message (validator lines 21-31) |
| Enabled=false escape hatch must actually skip middleware | ✓ | UsePlatformWeb only calls UseForwardedHeaders when Enabled (PlatformWebExtensions.cs:109-112) |
| Loopback defaults vs explicit trust list | ✓ | No trust list → keep ASP.NET loopback defaults; trust list → Clear() then add (PlatformWebExtensions.cs:76-95) |
| Runtime ForwardedHeadersOptions mapping | ✓ | AddPlatformWeb maps KnownProxies/KnownNetworks/ForwardLimit into ForwardedHeadersOptions and clears loopback defaults; proven by ForwardedHeadersSettingsValidatorTests.AddPlatformWeb_maps_configured_trust_list_to_forwarded_headers_options |
| ForwardLimit for chained proxies | ✓ | Configurable, default 1 (ForwardedHeadersSettings.cs:24) |

**Testy:** ForwardedHeadersSettingsValidatorTests (8 cases: prod empty fail, known proxy/network succeed, disabled succeed, dev exempt, malformed proxy, malformed cidr, runtime options mapping); AuditIpMaskingTests (6 cases incl. ipv4-mapped-ipv6, truncation, drop); PlatformContractTests.PL8 satisfies the trust-list guard for a derived Production host
**Test gaps:** No remaining focused forwarded-headers/audit-IP gap in this slice.

_Best-covered security feature in the area. Validator, runtime options mapping, and IP masking are all directly tested._

### JWT bearer auth + options validation — 🟢 minor-gaps
*Validates HMAC-signed access tokens (issuer/audience/lifetime/key) and fail-fasts a weak/missing signing key outside Development.*

**Use cases:** Authenticate API requests via bearer token; RequireRole matches 'role' claim (RoleClaimType configured); Fail host startup on misconfigured JWT in prod

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Missing/weak signing key in production = auth-fails-open | ✓ | JwtOptionsValidator requires SigningKey>=32 bytes + Issuer + Audience outside Dev (validator lines 18-41), ValidateOnStart (PlatformWebExtensions.cs:47-49) |
| Dev runs without a configured key | ✓ | Placeholder 32-zero key so DI builds (PlatformWebExtensions.cs:147-148); validator exempts Development |
| Role claim type mismatch (RequireRole/IsInRole) | ✓ | RoleClaimType=AuthorizationClaims.Role='role' (PlatformWebExtensions.cs:152); TokenIssuer emits role claims (TokenIssuer.cs:50) |
| Clock skew | ✓ | ClockSkew=30s (PlatformWebExtensions.cs:150) |
| Permission-based authz | ✓ | RequirePermission policy checks 'permission' claim (EndpointAuthorization.cs:23-27); claims emitted in TokenIssuer.cs:51 |
| Identity.Name unavailable for downstream consumers (rate limiter) | ✓ | Rate limiter no longer depends on `Identity.Name`; it uses the emitted `ClaimTypes.NameIdentifier` subject id. `Identity.Name` remaining null is acceptable because no platform contract relies on it. |

**Testy:** JwtOptionsValidatorTests cover missing/short signing key, missing issuer/audience, valid Production config, Development exemption, and `Jwt_bearer_uses_platform_role_claim_type_for_require_role`; PlatformContractTests.PL8 indirectly exercises a Production host that passes JWT validation; Auth/permission flows covered in Identity module tests (RequirePermission/RequireRole gating)
**Test gaps:** No remaining focused JWT bearer/options gap in this slice.

_JwtOptionsValidator now has direct fail-fast coverage, and the JWT bearer options pin the platform `"role"` claim type used by RequireRole. The missing name claim does not affect rate limiting because the limiter keys on `NameIdentifier`._

### Security headers middleware — 🟢 minor-gaps
*Adds baseline hardening headers (nosniff, frame DENY, no-referrer, restrictive CSP) to every response.*

**Use cases:** Defence-in-depth on API responses against MIME-sniffing/clickjacking/referrer leak

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Headers set before next() so they apply even on error responses | ✓ | Headers written first, then await next() (SecurityHeadersMiddleware.cs:10-15); runs before GlobalExceptionMiddleware in the pipeline (PlatformWebExtensions.cs:114-123). Proven by PlatformContractTests.PL10_security_headers_present_on_every_response. |
| Response already started (header write after flush) | ◐ | Setting headers before next() is the correct ordering, but there is no guard if a later component already started the response on a re-entrant path; in practice safe given placement |
| CSP suitable for an API (default-src none) vs a browser app | ✓ | default-src 'none'; frame-ancestors 'none' — appropriate for a pure JSON/SSE API |

**Testy:** PlatformContractTests.PL10_security_headers_present_on_every_response

_Correct and minimal; the baseline header contract is covered by an integration test._

### SSE stream endpoint (native SSE endpoint) — 🟢 minor-gaps
*Per-connection bounded SSE buffer; the /v1/realtime/stream endpoint streams owner-scoped events with Last-Event-ID replay.*

**Use cases:** Browser realtime stream of a user's own events; Reconnect replay via Last-Event-ID; Back-pressure tolerance for slow/dead consumers

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Slow/dead consumer growing the buffer unbounded (memory leak) | ✓ | Channel.CreateBounded(256, DropOldest) in RealtimeStreamEndpoint.cs:57-62; TryWrite never blocks/fails; covered by RealtimeSseTests.Sse_live_buffer_drops_oldest_events_under_back_pressure |
| Client disconnect must dispose subscription | ✓ | Enumerator cancellation (CancellationToken) ends ReadAllAsync; using subscription disposes (RealtimeStreamEndpoint.cs:63-83) |
| Unauthenticated access to the stream | ✓ | .RequireAuthorization() + tenant.UserId ?? throw UnauthorizedException('auth.required') (RealtimeStreamEndpoint.cs:32-41) |
| Event ordering / duplicates across replay→live boundary | ◐ | Subscribe-before-replay means an event can be BOTH replayed and live-buffered (duplicate id), or buffered ones emitted after replayed ones; documented as acceptable best-effort UX (RealtimeStreamEndpoint.cs:54-56). Durable facts live in modules |
| Removed duplicate SseStream<T> abstraction stays removed | ✓ | No SseStream.cs exists under src; the active endpoint owns the one bounded channel implementation. |

**Testy:** Realtime replay covered in Realtime building-block/integration tests (outside this area's test set)
**Test gaps:** No authenticated HTTP streaming round-trip over TestServer (buffers infinite SSE).

_Endpoint is sound (bounded, owner-scoped, auth-gated). The old duplicate SseStream<T> abstraction has already been removed; remaining gaps are streaming-harness limitations._

### Telemetry (OTel pipeline + TelemetryBehavior + PlatformMetrics) — ✅ correct
*Outer-most CQRS span per request + OTel tracing/metrics export and a single shared Meter for platform/module instruments.*

**Use cases:** Per-command/query span tagged with type + outcome/error code; ASP.NET + runtime instrumentation export via OTLP; Single ModularPlatform meter for all custom counters/gauges

| Edge case | | Jak se k tomu stavíme |
|---|:--:|---|
| Behavior must be outer-most | ✓ | AddPlatformTelemetry registers TelemetryBehavior first; called before AddPlatformWeb in every host (Api Program.cs:29, comment DependencyInjection.cs:13-14); covered by TelemetryBehaviorTests.Platform_registration_keeps_telemetry_behavior_outer_most |
| Domain vs unexpected exception tagging | ✓ | ModularPlatformException tags cqrs.error_code; generic sets Error status with message (TelemetryBehavior.cs:22-32); error-code tag pinned by TelemetryBehaviorTests.ModularPlatformException_tags_activity_with_error_code |
| A second Meter never registered via AddMeter (silently unexported) | ✓ | Single PlatformMetrics.Meter (MeterName='ModularPlatform') registered via .AddMeter (DependencyInjection.cs:27); CLAUDE.md warns against a second meter |
| No OTLP collector configured | ◐ | AddOtlpExporter defaults to localhost:4317; with no collector the exporter logs/drops silently — deployment concern, not a code gap |

**Testy:** JobFailureMetricsTests uses a MeterListener on the 'ModularPlatform' meter — indirectly proves the shared meter name + instrument export path; TelemetryBehaviorTests.ModularPlatformException_tags_activity_with_error_code; TelemetryBehaviorTests.Platform_registration_keeps_telemetry_behavior_outer_most
**Test gaps:** No remaining focused telemetry behavior gap in this slice.

_Clean single-meter design; error-code tagging and outer-most behavior ordering are pinned by tests._

**Nekonzistence v oblasti (5):**
- Rate-limiter per-user partition is fixed: PlatformWebExtensions keys authenticated traffic on `ClaimTypes.NameIdentifier`; PL11 proves user-specific buckets.
- Retry-After drift is fixed: PlatformWebExtensions `OnRejected` emits `Retry-After`; PL12 proves the header on a throttled response.
- Historical note: the unused SseStream<T> abstraction mentioned in older audits has already been removed; the live browser endpoint now owns the single bounded channel implementation.
- GlobalExceptionMiddleware.WriteProblem (lines 72-73) writes status + JSON with no Response.HasStarted guard; for a streamed/SSE response that already flushed headers this throws instead of failing gracefully. Minor given current usage but an unhandled edge.
- JwtOptionsValidator now has a focused unit suite (`JwtOptionsValidatorTests`) covering the startup fail-fast guard; this closes the previous asymmetry with `ForwardedHeadersSettingsValidatorTests`.
