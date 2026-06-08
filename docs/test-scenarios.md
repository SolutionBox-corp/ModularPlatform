# ModularPlatform — Test Scenarios

Coverage map + test plan. Each scenario is **Given / When / Then**, tagged by type and status.
Types: **U** unit (no host) · **I** integration (shared `PlatformApiFactory` + Testcontainers-Postgres) ·
**C** concurrency · **F** failure/resilience · **A** architecture (ArchUnitNET).
Status: **✓** implemented · **▢** gap (planned) · **◐** partially covered.

> Reuse the shared harness (`tests/ModularPlatform.IntegrationTesting`). One behaviour per test. Assert through
> HTTP/queries, never private state. See the `writing-modularplatform-tests` skill.

---

## 1. Identity & Auth

| # | Scenario (Given / When / Then) | Type | Status |
|---|---|---|---|
| ID-1 | New email / register → `201` + userId; profile readable with the access token | I | ✓ `IdentityE2ETests` |
| ID-2 | Duplicate email → `409 user.email_taken`; only one user row | I | ▢ |
| ID-3 | Register validator: empty/invalid email, password < 8 → field `errorCode`s | U | ▢ |
| ID-4 | Login wrong password → `401 auth.invalid_credentials`; no user enumeration (same error for unknown email) | I | ◐ (lockout test covers wrong pw) |
| ID-5 | Login → refresh rotation → new tokens; old refresh replay → `401 auth.refresh_token_reused` | I | ✓ `IdentityE2ETests` |
| ID-6 | **Reuse revoke is audited**: after reuse, ALL family tokens `RevokedAt` set AND ≥1 `identity_audit_entries` row for the revoke | I | ▢ (A4 shipped, assert audit) |
| ID-7 | **Lockout**: 5 wrong passwords → account locked 15 min; correct password while locked → still rejected `auth.locked_out`; success resets counters | I | ✓ `AccountLockoutTests` |
| ID-8 | Two parallel `/refresh` with the same valid token → exactly one `200`, family ends revoked, no unhandled 500 | C | ▢ |
| ID-9 | Brute-force: >10 `/login` from one IP in a minute → `429`; per-account lockout independent of the IP budget | F | ▢ (A13 shipped) |
| ID-10 | JWT issued validates under the configured key; a token signed with a wrong key is rejected | U | ▢ |
| ID-11 | `JwtOptionsValidator`: empty/<32-byte key in non-Development → host fails to start; Development → allowed | U | ▢ |
| ID-12 | Audit after profile update records ONLY changed columns (JSONB) | I | ◐ (e2e checks Create row) |

## 2. Billing — credit ledger (money-critical)

| # | Scenario | Type | Status |
|---|---|---|---|
| BL-1 | **No double-spend**: balance 1000, 20 parallel reserves of 100 → exactly 10 succeed, 10 `422`, available never < 0, holds sum == reserved | C | ✓ `BillingConcurrencyTests` |
| BL-2 | **Confirm exactly-once**: 10 parallel confirms of one reservation → all `200`, exactly one Spend entry, posted decremented once | C | ✓ `BillingLedgerTests` |
| BL-3 | **Idempotent top-up**: 2 parallel top-ups, same key → one credit, posted == amount, one Topup entry | C | ✓ `BillingLedgerTests` |
| BL-4 | Amount bounds: top-up/reserve with `≤ 0` → `credit.amount.must_be_positive`; above max → `credit.amount.too_large` | U | ✓ `CreditAmountBoundsTests` |
| BL-5 | DB CHECK backstop: a raw negative write to posted/available/pending → constraint violation | I | ▢ |
| BL-6 | Reserve → ReleaseHold restores availability (`available = posted − pending`); double-release idempotent | I | ▢ |
| BL-7 | Reserve → ConfirmSpend draws buckets soonest-to-expire (FIFO); posted reduced; bucket remaining drawn once | I | ▢ |
| BL-8 | Reserve insufficient balance → `422 credit.insufficient_balance`; no hold/entry created | I | ▢ |
| BL-9 | Expire sweep: lapsed hold restores availability; expired bucket destroys credits (posted − lost); idempotent (UNIQUE expire key) | I | ▢ |
| BL-10 | `GetCreditBalance` returns stored `available` (== what reserve allows), not a live recompute | I | ◐ |
| BL-11 | Overflow guard: posted near `long.MaxValue`, top-up → rejected, balance unchanged | I/U | ▢ |
| BL-12 | Ledger invariant after any mixed reserve/confirm/release/expire run: `posted == Σcredit − Σdebit`; `available == posted − pending` | I | ▢ |

## 3. Billing — Stripe webhook

| # | Scenario | Type | Status |
|---|---|---|---|
| ST-1 | Valid signed top-up event → `200` fast; `StripeEvent` row AND the worker message persisted in ONE transaction (outbox) | I | ▢ |
| ST-2 | **Redelivery exactly-once**: same `StripeEventId` delivered twice → exactly one Topup entry, one `CreditsToppedUp` envelope, `ProcessedAt` set | I/C | ▢ |
| ST-3 | Bad signature → `400`, nothing persisted | I | ▢ |
| ST-4 | Out-of-order events reconcile against object state (not sequence) | F | ▢ |
| ST-5 | Webhook is exempt from the rate limiter: 150 rapid signed deliveries from one IP → none `429` | F | ▢ |

## 4. Cross-module events (the durable spine)

| # | Scenario | Type | Status |
|---|---|---|---|
| EV-1 | Register a user → `UserRegisteredIntegrationEvent` → Billing auto-provisions a credit account | I | ✓ `CrossModuleEventTests` |
| EV-2 | Register → Notifications welcome path runs; missing template is non-fatal (no dead-letter, warning logged) | I/F | ▢ |
| EV-3 | A thrown message handler dead-letters after retries (does not silently mark Handled) | F | ▢ |
| EV-4 | Kill the worker mid-message; restart → the durable message is processed once (inbox dedup) | F | ▢ |
| EV-5 | `EnsureCreditAccount` dispatched twice/concurrently for one user → exactly one account (UNIQUE userId) | C | ▢ |

## 5. Notifications

| # | Scenario | Type | Status |
|---|---|---|---|
| NT-1 | `SendNotification` persists an in-app row + enqueues channel deliveries via the outbox (never inline) | I | ▢ |
| NT-2 | In-app realtime push happens AFTER commit (force first save to fail once → exactly one delivery, one row) | C/F | ▢ |
| NT-3 | Email delivery handler runs in the Worker; per-user locale resolved | I | ▢ |
| NT-4 | `GetMyNotifications(unreadOnly)` + `MarkNotificationRead` round-trip | I | ▢ |
| NT-5 | Channel validation: unknown channel → `notification.channel.invalid` | U | ▢ |

## 6. GDPR

| # | Scenario | Type | Status |
|---|---|---|---|
| GD-1 | Crypto-shredder: encrypt → decrypt round-trip; deleting the DEK makes ciphertext unrecoverable | U | ✓ `Gdpr.Tests` |
| GD-2 | `ShredSubjectKey` nulls `WrappedDek` + stamps `DeletedAt`; idempotent when already shredded | U | ✓ `SubjectKeyShredTests` |
| GD-3 | **Erasure pipeline e2e**: seed Billing account + a Notification with PII + a SubjectKey → publish `UserErasureRequested` → Worker drains → Notification Title/Body blanked, SubjectKey shredded, **ledger UNCHANGED** | I | ▢ (highest-value gap) |
| GD-4 | `ExportUserDataQuery` fans out `IExportPersonalData`, assembles one document keyed by module; resilient if one exporter throws | I/F | ▢ |
| GD-5 | Consent grant/withdraw/get round-trip (append-only) | I | ▢ |

## 7. Cross-cutting platform

| # | Scenario | Type | Status |
|---|---|---|---|
| PL-1 | Module boundaries: `*.Contracts` depend on no infra; a module Core depends on no other Core | A | ✓ `ArchitectureTests` |
| PL-2 | Audit interceptor: update records ONLY changed columns; value-converted enum serialized as string, not int | I | ▢ |
| PL-3 | Error contract: a domain exception → RFC 9457 `application/problem+json`, stable `errorCode`, localized `detail` (Accept-Language en/cs) | I | ▢ |
| PL-4 | `ApiResponse<T>` wraps success only; errors are always Problem Details | I | ◐ |
| PL-5 | **Tenant isolation**: tenant A & B rows; an authenticated non-system user with tenant A → sees only A's rows; a missing claim → NOT everyone's | I | ▢ |
| PL-6 | xmin concurrency: two updates to one row → second conflicts → `ConcurrencyRetryBehavior` retries (tracker cleared) → succeeds, no 500 | C | ◐ (BL-2 exercises it) |
| PL-7 | Health: `/health/live` always `200`; `/health/ready` `200` when Postgres up, `503` when down | I/F | ✓ live+ready up; ▢ down case |
| PL-8 | OpenAPI gating: in Production anonymous `/openapi/v1.json` is not `200`; Development `200` | I | ▢ |
| PL-9 | Rate limiter: >100 req/min from one principal → `429` with the right headers | F | ▢ |
| PL-10 | Migration race: two contexts → same fresh DB, parallel `ApplyMigrationsAsync` → exactly one applies, no throw | C | ▢ |
| PL-11 | Worker/Jobs/Migration run under `SystemTenantContext` (tenant filter bypassed for system work) | I | ◐ (erasure relies on it) |

---

## Priority gaps to fill next
1. **GD-3** erasure pipeline e2e — proves the whole GDPR spine + ledger retention.
2. **ST-1/ST-2** Stripe atomic + redelivery exactly-once — money + external integration.
3. **PL-5** tenant isolation — security; assert the null-escape is closed.
4. **ID-6** refresh-reuse-is-audited — security forensics.
5. **BL-6/BL-7/BL-9/BL-12** ledger lifecycle + invariant — money completeness.
6. **EV-2/EV-3/EV-4** event resilience (welcome non-fatal, dead-letter, crash-recovery).
