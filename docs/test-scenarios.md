# ModularPlatform — Test Scenarios

Coverage map + test plan. Each scenario is **Given / When / Then**, tagged by type and status.
Types: **U** unit (no host) · **I** integration (shared `PlatformApiFactory` + Testcontainers-Postgres) ·
**C** concurrency · **F** failure/resilience · **A** architecture (ArchUnitNET).
Status: **✓** implemented · **▢** gap (planned) · **◐** partially covered.

> Reuse the shared harness (`tests/ModularPlatform.IntegrationTesting`). One behaviour per test. Assert through
> HTTP/queries, never private state. See the `writing-modularplatform-tests` skill.

> **⚠️ 2026-06-11:** this is the original Given/When/Then PLAN (as of ~108 tests). The suite is now **181/181** and many
> rows once marked `▢ gap` are implemented (no-enumeration parity, expired-token, paging, security-headers, per-user
> rate-limit, GDPR consent export/erase, host-boot DI, …). **Authoritative CURRENT coverage = `feature-coverage.md`.**
> Use this doc for the scenario intent; trust `feature-coverage.md` for what's actually covered today.

---

## 1. Identity & Auth

| # | Scenario (Given / When / Then) | Type | Status |
|---|---|---|---|
| ID-1 | New email / register → `201` + userId; profile readable with the access token | I | ✓ `IdentityE2ETests` |
| ID-2 | Duplicate email → `409 user.email_taken`; only one user row | I | ✓ `AuthRobustnessTests` |
| ID-3 | Register validator: empty/invalid email, password < 8 → field `errorCode`s | U/I | ✓ `RegisterUserValidatorTests` |
| ID-4 | Login wrong password → `401 auth.invalid_credentials`; no user enumeration (same error for unknown email) | I | ✓ `Unknown_email_and_wrong_password_return_identical_401_invalid_credentials` |
| ID-5 | Login → refresh rotation → new tokens; old refresh replay → `401 auth.refresh_token_reused` | I | ✓ `IdentityE2ETests` |
| ID-6 | **Reuse revoke is audited**: after reuse, ALL family tokens `RevokedAt` set AND ≥1 `identity_audit_entries` row for the revoke | I | ✓ `AuthRobustnessTests` |
| ID-7 | **Lockout**: 5 wrong passwords → account locked 15 min; correct password while locked → still rejected `auth.locked_out`; success resets counters | I | ✓ `AccountLockoutTests` |
| ID-8 | Two parallel `/refresh` with the same valid token → exactly one `200`, family ends revoked, no unhandled 500 | C | ✓ `AuthRobustnessTests` |
| ID-9 | Brute-force: >10 `/login` from one IP in a minute → `429`; per-account lockout independent of the IP budget | F | ✓ `Login_endpoint_uses_the_auth_rate_limit_policy` + `AccountLockoutTests` |
| ID-10 | JWT issued validates under the configured key; a token signed with a wrong key is rejected | U | ✓ `TokenIssuerTests.Access_token_validates_with_configured_key_and_rejects_wrong_key` |
| ID-11 | `JwtOptionsValidator`: empty/<32-byte key in non-Development → host fails to start; Development → allowed | U | ✓ `JwtOptionsValidatorTests` |
| ID-12 | Audit after profile update records ONLY changed columns (JSONB) | I | ✓ `Update_profile_audit_row_records_changed_columns_only` |
| ID-13 | Accept current terms from `/identity/users/me/terms-acceptance`: subject comes from token, version/timestamp persist, profile returns the accepted version, invalid versions reject, machine principals are forbidden | I/S | ✓ `AcceptTermsTests` + `MachineTokenTests.Admin_mints_a_tenant_scoped_machine_token` |

## 2. Billing — credit ledger (money-critical)

| # | Scenario | Type | Status |
|---|---|---|---|
| BL-1 | **No double-spend**: balance 1000, 20 parallel reserves of 100 → exactly 10 succeed, 10 `422`, available never < 0, holds sum == reserved | C | ✓ `BillingConcurrencyTests` |
| BL-2 | **Confirm exactly-once**: 10 parallel confirms of one reservation → all `200`, exactly one Spend entry, posted decremented once | C | ✓ `BillingLedgerTests` |
| BL-3 | **Idempotent top-up**: 2 parallel top-ups, same key → one credit, posted == amount, one Topup entry | C | ✓ `BillingLedgerTests` |
| BL-4 | Amount bounds: top-up/reserve with `≤ 0` → `credit.amount.must_be_positive`; above max → `credit.amount.too_large` | U | ✓ `CreditAmountBoundsTests` |
| BL-5 | DB CHECK backstop: a raw negative write to posted/available/pending → constraint violation | I | ✓ `LedgerBackstopTests` (23514 check_violation) |
| BL-6 | Reserve → ReleaseHold restores availability (`available = posted − pending`); double-release idempotent | I | ✓ `LedgerLifecycleTests` |
| BL-7 | Reserve → ConfirmSpend draws buckets soonest-to-expire (FIFO); posted reduced; bucket remaining drawn once | I | ✓ `LedgerLifecycleTests` |
| BL-8 | Reserve insufficient balance → `422 credit.insufficient_balance`; no hold/entry created | I | ✓ `LedgerLifecycleTests` |
| BL-9 | Expire sweep: lapsed hold restores availability; expired bucket destroys credits (posted − lost); idempotent (UNIQUE expire key) | I | ✓ `LedgerLifecycleTests` (sweep dispatched via DI; no HTTP trigger) |
| BL-10 | `GetCreditBalance` returns stored `available` (== what reserve allows), not a live recompute | I | ✓ `LedgerBackstopTests` (skewed stored projection is what the read returns) |
| BL-11 | Overflow guard: posted near `long.MaxValue`, top-up → rejected, balance unchanged | I/U | ✓ `LedgerBackstopTests` (`credit.amount.too_large` 422 pre-check in `CreditTopUpHandler`; DB CHECK remains the backstop) |
| BL-12 | Ledger invariant after any mixed reserve/confirm/release/expire run: `posted == Σ bucket.Remaining`; `available == posted − pending` | I | ✓ `LedgerLifecycleTests` (corrected: a Reservation debit only moves available→pending, so `posted == Σ bucket.Remaining`, not `Σcredit − Σdebit`) |

## 3. Billing — Stripe webhook

| # | Scenario | Type | Status |
|---|---|---|---|
| ST-1 | Valid signed top-up event → `200` fast; `StripeEvent` row AND the worker message persisted in ONE transaction (outbox) | I | ✓ FULL — `StripeWebhookTests` (ingest) + `BillingCommerceTests` (ledger top-up + `ProcessedAt` through the `IStripeGateway` fake) |
| ST-2 | **Redelivery exactly-once**: same `StripeEventId` delivered twice → exactly one Topup entry, one `CreditsToppedUp` envelope, `ProcessedAt` set | I/C | ✓ FULL — `BillingCommerceTests` (one ledger entry + `ProcessedAt` across redelivery, via the fake gateway) |
| ST-3 | Bad signature → `400`, nothing persisted | I | ✓ `StripeWebhookTests` |
| ST-4 | Out-of-order events reconcile against object state (not sequence) | F | ✓ `BillingCommerceTests` (subscription updated-before-created + invoice-before-subscription converge on Stripe object state) |
| ST-5 | Webhook is exempt from the rate limiter: rapid deliveries from one IP → none `429` | F | ✓ `PlatformContractTests` (low-limit derived host: normal traffic 429s, webhook never) |

## 4. Cross-module events (the durable spine)

| # | Scenario | Type | Status |
|---|---|---|---|
| EV-1 | Register a user → `UserRegisteredIntegrationEvent` → Billing auto-provisions a credit account | I | ✓ `CrossModuleEventTests` |
| EV-2 | Register → Notifications welcome path runs (template now SEEDED by `NotificationsSeeder`); missing template stays non-fatal for unseeded keys | I/F | ✓ `NotificationsIntegrationTests` (rewritten: the welcome in-app row IS created) |
| EV-3 | A thrown message handler dead-letters after retries (does not silently mark Handled) | F | ✓ `DeadLetterTests` (unknown event id → gateway throws every retry → durable dead-letter row, ProcessedAt stays NULL) |
| EV-4 | Kill the worker mid-message; restart → the durable message is processed once (inbox dedup) | F | ✓ `WorkerDurabilityTests` (API runs publisher-only, real `ModularPlatform.Worker` child process blocks on `credit_accounts`, is killed, restarted, then provisions exactly one account) |
| EV-5 | `EnsureCreditAccount` dispatched twice/concurrently for one user → exactly one account (UNIQUE userId) | C | ✓ `LedgerBackstopTests` (8-way concurrent) |

## 5. Notifications

| # | Scenario | Type | Status |
|---|---|---|---|
| NT-1 | `SendNotification` persists an in-app row + enqueues channel deliveries via the outbox (never inline) | I | ✓ `NotificationsIntegrationTests` (caller sends to its OWN id — RLS WITH CHECK on `IUserOwned` blocks cross-user HTTP sends; cross-user happens via the Worker/system) |
| NT-2 | In-app realtime push happens AFTER commit (force first save to fail once → exactly one delivery, one row) | C/F | ✓ `SendNotification_realtime_push_happens_only_after_successful_commit` |
| NT-3 | Email delivery handler runs in the Worker; per-user locale resolved | I | ✓ `WorkerEmailDeliveryTests` + `SendNotification_email_delivery_message_uses_requested_locale_template` + `ChannelDeliveryHandlersTests` |
| NT-4 | `GetMyNotifications(unreadOnly)` + `MarkNotificationRead` round-trip | I | ✓ `NotificationsIntegrationTests` |
| NT-5 | Channel validation: unknown channel → `notification.channel.invalid` | U | ✓ `SendNotification_with_unknown_channel_returns_validation_problem` |

## 6. GDPR

| # | Scenario | Type | Status |
|---|---|---|---|
| GD-1 | Crypto-shredder: encrypt → decrypt round-trip; deleting the DEK makes ciphertext unrecoverable | U | ✓ `Gdpr.Tests` |
| GD-2 | `ShredSubjectKey` nulls `WrappedDek` + stamps `DeletedAt`; idempotent when already shredded | U | ✓ `SubjectKeyShredTests` |
| GD-3 | **Erasure pipeline e2e**: seed Billing account + a Notification with PII + a SubjectKey → publish `UserErasureRequested` → Worker drains → Notification Title/Body blanked, SubjectKey shredded, **ledger UNCHANGED** | I | ✓ `GdprIntegrationTests` (POST `/v1/gdpr/me/erase`; no slice creates a SubjectKey so the test seeds one) |
| GD-4 | `ExportUserDataQuery` fans out `IExportPersonalData`, assembles one document keyed by module; resilient if one exporter throws | I/F | ✓ FULL — per-exporter try/catch in the handler + `ExportResilienceTests`/`ExportResilienceUnitTests` (a thrower yields `{"error":"export_failed"}`, others export) |
| GD-5 | Consent grant/withdraw/get round-trip (append-only) | I | ✓ `GdprIntegrationTests` |

## 7. Cross-cutting platform

| # | Scenario | Type | Status |
|---|---|---|---|
| PL-1 | Module boundaries: `*.Contracts` depend on no infra; a module Core depends on no other Core | A | ✓ `ArchitectureTests` |
| PL-2 | Audit interceptor: update records ONLY changed columns; value-converted enum serialized as string, not int | I | ✓ `LedgerBackstopTests` (hold release: `"Released"` string present, immutable Amount absent) |
| PL-3 | Error contract: a domain exception → RFC 9457 `application/problem+json`, stable `errorCode`, localized `detail` (Accept-Language en/cs) | I | ✓ `PlatformContractTests` |
| PL-4 | `ApiResponse<T>` wraps success only; errors are always Problem Details | I | ✓ `PlatformContractTests.PL4_success_is_api_response_and_errors_are_problem_details_not_wrapped` |
| PL-5 | **Tenant isolation**: tenant A & B rows; an authenticated non-system user with tenant A → sees only A's rows; a missing claim → NOT everyone's | I | ✓ `TenantIsolationTests` (`Two_users_land_in_distinct_tenants_in_the_same_users_table`, self-only filtered read, signed token with no `tenant_id` claim returns no row, anonymous 401) |
| PL-6 | xmin concurrency: two updates to one row → second conflicts → `ConcurrencyRetryBehavior` retries (tracker cleared) → succeeds, no 500 | C | ✓ `ConcurrencyRetryBehaviorTests.Retries_after_concurrency_conflict_and_clears_the_change_tracker_before_rerun` + `Gives_up_after_max_retries_and_surfaces_the_concurrency_exception` |
| PL-7 | Health: `/health/live` always `200`; `/health/ready` `200` when Postgres up, `503` when down | I/F | ✓ live+ready up (`HealthCheckTests`) + DB-down readiness 503 (`PlatformContractTests.PL7_liveness_stays_up_but_readiness_fails_when_postgres_is_unreachable`) |
| PL-8 | OpenAPI gating: in Production anonymous `/openapi/v1.json` is not `200`; Development `200` | I | ✓ `PlatformContractTests` (Production derived host vs the Development shared host) |
| PL-9 | Rate limiter: low-limit host, one IP partition → `429` | F | ✓ `PlatformContractTests` (5-permit derived host) |
| PL-10 | Migration race: two contexts → same fresh DB, parallel `ApplyMigrationsAsync` → exactly one applies, no throw | C | ✓ `MigrationRaceTests.Parallel_identity_migrations_on_one_fresh_database_are_idempotent` |
| PL-11 | Worker/Jobs/Migration run under `SystemTenantContext` (tenant filter bypassed for system work) | I | ✓ `HostBootTests.Non_http_hosts_run_with_system_tenant_context` |
| PL-12 | **Audit-PII crypto-shred**: a `[PersonalData]` value is stored in the audit trail ONLY as a `penc:v1:` envelope (never plaintext); an admin reveals it via `GET /v1/identity/admin/users/{id}/audit` (`audit.read`); after the subject erases themselves the DEK is shredded and the same value surfaces as `[erased]`, raw row still plaintext-free | I | ✓ `AuditPiiEncryptionTests` (Identity) + `Notification_pii_is_crypto_shredded_in_the_audit_trail` (Notifications) + `PersonalDataConventionTests` (Arch: `[PersonalData]` ⇒ `IDataSubject`) |

---

## Priority gaps to fill next

Wave 1 (2026-06-09, 20 tests) and **wave 2 (2026-06-10)** have shipped. Covered since wave 2: full ST-1/ST-2
(via the `IStripeGateway` fake), ST-4, ST-5, EV-3, EV-5, BL-5/10/11, PL-2/3/7-down/8/9, GD-4 resilience —
plus the new commerce suite (package purchase saga e2e, subscription lifecycle, reconcile sweep, retention
sweep, PII column encryption, dead-letter, replay buffer).

**Remaining, in priority order:**
No explicit `▢ gap` scenario remains in this map as of the EV-4 out-of-process worker harness.
