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
| ID-2 | Duplicate email → `409 user.email_taken`; only one user row | I | ✓ `AuthRobustnessTests` |
| ID-3 | Register validator: empty/invalid email, password < 8 → field `errorCode`s | U | ▢ |
| ID-4 | Login wrong password → `401 auth.invalid_credentials`; no user enumeration (same error for unknown email) | I | ◐ (lockout test covers wrong pw) |
| ID-5 | Login → refresh rotation → new tokens; old refresh replay → `401 auth.refresh_token_reused` | I | ✓ `IdentityE2ETests` |
| ID-6 | **Reuse revoke is audited**: after reuse, ALL family tokens `RevokedAt` set AND ≥1 `identity_audit_entries` row for the revoke | I | ✓ `AuthRobustnessTests` |
| ID-7 | **Lockout**: 5 wrong passwords → account locked 15 min; correct password while locked → still rejected `auth.locked_out`; success resets counters | I | ✓ `AccountLockoutTests` |
| ID-8 | Two parallel `/refresh` with the same valid token → exactly one `200`, family ends revoked, no unhandled 500 | C | ✓ `AuthRobustnessTests` |
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
| BL-6 | Reserve → ReleaseHold restores availability (`available = posted − pending`); double-release idempotent | I | ✓ `LedgerLifecycleTests` |
| BL-7 | Reserve → ConfirmSpend draws buckets soonest-to-expire (FIFO); posted reduced; bucket remaining drawn once | I | ✓ `LedgerLifecycleTests` |
| BL-8 | Reserve insufficient balance → `422 credit.insufficient_balance`; no hold/entry created | I | ✓ `LedgerLifecycleTests` |
| BL-9 | Expire sweep: lapsed hold restores availability; expired bucket destroys credits (posted − lost); idempotent (UNIQUE expire key) | I | ✓ `LedgerLifecycleTests` (sweep dispatched via DI; no HTTP trigger) |
| BL-10 | `GetCreditBalance` returns stored `available` (== what reserve allows), not a live recompute | I | ◐ |
| BL-11 | Overflow guard: posted near `long.MaxValue`, top-up → rejected, balance unchanged | I/U | ▢ |
| BL-12 | Ledger invariant after any mixed reserve/confirm/release/expire run: `posted == Σ bucket.Remaining`; `available == posted − pending` | I | ✓ `LedgerLifecycleTests` (corrected: a Reservation debit only moves available→pending, so `posted == Σ bucket.Remaining`, not `Σcredit − Σdebit`) |

## 3. Billing — Stripe webhook

| # | Scenario | Type | Status |
|---|---|---|---|
| ST-1 | Valid signed top-up event → `200` fast; `StripeEvent` row AND the worker message persisted in ONE transaction (outbox) | I | ◐ `StripeWebhookTests` (atomic INGEST proven via `stripe_events`; the actual ledger top-up is unreachable in-test — the worker handler does a LIVE `EventService().GetAsync` with no API key) |
| ST-2 | **Redelivery exactly-once**: same `StripeEventId` delivered twice → exactly one Topup entry, one `CreditsToppedUp` envelope, `ProcessedAt` set | I/C | ◐ `StripeWebhookTests` (exactly-once proven at the INGEST layer: one `stripe_events` row; topup-applied/`ProcessedAt` need a Stripe test seam) |
| ST-3 | Bad signature → `400`, nothing persisted | I | ✓ `StripeWebhookTests` |
| ST-4 | Out-of-order events reconcile against object state (not sequence) | F | ▢ |
| ST-5 | Webhook is exempt from the rate limiter: 150 rapid signed deliveries from one IP → none `429` | F | ▢ |

## 4. Cross-module events (the durable spine)

| # | Scenario | Type | Status |
|---|---|---|---|
| EV-1 | Register a user → `UserRegisteredIntegrationEvent` → Billing auto-provisions a credit account | I | ✓ `CrossModuleEventTests` |
| EV-2 | Register → Notifications welcome path runs; missing template is non-fatal (no dead-letter, warning logged) | I/F | ✓ `NotificationsIntegrationTests` (no `welcome` template is seeded anywhere → asserts the missing-template-is-non-fatal path; the spine still provisions the Billing account) |
| EV-3 | A thrown message handler dead-letters after retries (does not silently mark Handled) | F | ▢ |
| EV-4 | Kill the worker mid-message; restart → the durable message is processed once (inbox dedup) | F | ▢ |
| EV-5 | `EnsureCreditAccount` dispatched twice/concurrently for one user → exactly one account (UNIQUE userId) | C | ▢ |

## 5. Notifications

| # | Scenario | Type | Status |
|---|---|---|---|
| NT-1 | `SendNotification` persists an in-app row + enqueues channel deliveries via the outbox (never inline) | I | ✓ `NotificationsIntegrationTests` (caller sends to its OWN id — RLS WITH CHECK on `IUserOwned` blocks cross-user HTTP sends; cross-user happens via the Worker/system) |
| NT-2 | In-app realtime push happens AFTER commit (force first save to fail once → exactly one delivery, one row) | C/F | ▢ |
| NT-3 | Email delivery handler runs in the Worker; per-user locale resolved | I | ▢ |
| NT-4 | `GetMyNotifications(unreadOnly)` + `MarkNotificationRead` round-trip | I | ✓ `NotificationsIntegrationTests` |
| NT-5 | Channel validation: unknown channel → `notification.channel.invalid` | U | ▢ |

## 6. GDPR

| # | Scenario | Type | Status |
|---|---|---|---|
| GD-1 | Crypto-shredder: encrypt → decrypt round-trip; deleting the DEK makes ciphertext unrecoverable | U | ✓ `Gdpr.Tests` |
| GD-2 | `ShredSubjectKey` nulls `WrappedDek` + stamps `DeletedAt`; idempotent when already shredded | U | ✓ `SubjectKeyShredTests` |
| GD-3 | **Erasure pipeline e2e**: seed Billing account + a Notification with PII + a SubjectKey → publish `UserErasureRequested` → Worker drains → Notification Title/Body blanked, SubjectKey shredded, **ledger UNCHANGED** | I | ✓ `GdprIntegrationTests` (POST `/v1/gdpr/me/erase`; no slice creates a SubjectKey so the test seeds one) |
| GD-4 | `ExportUserDataQuery` fans out `IExportPersonalData`, assembles one document keyed by module; resilient if one exporter throws | I/F | ◐ `GdprIntegrationTests` (assembly-by-module proven; the **resilient-if-one-throws** half is NOT asserted — `ExportUserDataHandler` has no per-exporter try/catch, so a thrower fails the whole export — see Findings) |
| GD-5 | Consent grant/withdraw/get round-trip (append-only) | I | ✓ `GdprIntegrationTests` |

## 7. Cross-cutting platform

| # | Scenario | Type | Status |
|---|---|---|---|
| PL-1 | Module boundaries: `*.Contracts` depend on no infra; a module Core depends on no other Core | A | ✓ `ArchitectureTests` |
| PL-2 | Audit interceptor: update records ONLY changed columns; value-converted enum serialized as string, not int | I | ▢ |
| PL-3 | Error contract: a domain exception → RFC 9457 `application/problem+json`, stable `errorCode`, localized `detail` (Accept-Language en/cs) | I | ▢ |
| PL-4 | `ApiResponse<T>` wraps success only; errors are always Problem Details | I | ◐ |
| PL-5 | **Tenant isolation**: tenant A & B rows; an authenticated non-system user with tenant A → sees only A's rows; a missing claim → NOT everyone's | I | ◐ `TenantIsolationTests` (distinct tenants + self-only filtered read + anonymous 401 proven; the "authenticated principal with NO tenant claim" case needs a token-minting seam — the no-null-escape filter is a source invariant at `PlatformDbContext.cs:84`) |
| PL-6 | xmin concurrency: two updates to one row → second conflicts → `ConcurrencyRetryBehavior` retries (tracker cleared) → succeeds, no 500 | C | ◐ (BL-2 exercises it) |
| PL-7 | Health: `/health/live` always `200`; `/health/ready` `200` when Postgres up, `503` when down | I/F | ✓ live+ready up; ▢ down case |
| PL-8 | OpenAPI gating: in Production anonymous `/openapi/v1.json` is not `200`; Development `200` | I | ▢ |
| PL-9 | Rate limiter: >100 req/min from one principal → `429` with the right headers | F | ▢ |
| PL-10 | Migration race: two contexts → same fresh DB, parallel `ApplyMigrationsAsync` → exactly one applies, no throw | C | ▢ |
| PL-11 | Worker/Jobs/Migration run under `SystemTenantContext` (tenant filter bypassed for system work) | I | ◐ (erasure relies on it) |

---

## Priority gaps to fill next

The first robustness wave shipped (2026-06-09): **20 new tests, suite 50 → 70 green**. ID-2/6/8, PL-5, BL-6/7/8/9/12,
ST-1/2/3, GD-3/4/5, EV-2, NT-1/4 are now covered (some as ◐ — see the per-row notes for what is and isn't asserted).

**Remaining, in priority order:**
1. **Stripe test seam (ST-1/ST-2 full)** — the worker handler does a live `EventService().GetAsync`; inject `IStripeClient`/`EventService` (or a configurable base URL) so the ledger top-up + `ProcessedAt` are assertable in-test.
2. **EV-3 / EV-4** event resilience — a thrown handler dead-letters (not silently `Handled`); kill-worker-mid-message → processed once on restart (needs an out-of-process worker harness, not TestServer).
3. **GD-4 export resilience** — once `ExportUserDataHandler` isolates a throwing exporter (see Findings), assert it.
4. **EV-5 / ST-4 / ST-5 / NT-2 / NT-3** — concurrent `EnsureCreditAccount` dedup; out-of-order Stripe; webhook rate-limit-exempt; realtime-after-commit; email locale.
5. **PL-2/3/7-down/8/9/10** cross-cutting — audit changed-columns, RFC9457 contract, `/health/ready` down, OpenAPI prod-gating, rate-limit 429 (now reachable via the config-driven limits + a low-limit host), migration race.
6. **BL-5/BL-10/BL-11** — DB CHECK backstop, stored-`available` read, overflow guard.
