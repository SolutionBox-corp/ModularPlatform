# ModularPlatform — ROADMAP ("everything that must be")

> The full intended scope, reconstructed from the original architecture plan
> (`~/.claude/plans/ultracode-tohle-budem-musel-transient-papert.md`, the binding "what must be" doc) and reconciled
> against the **actual build status** as of **2026-06-10**. This is the durable, in-repo successor to that
> session-local plan file. `CLAUDE.md` is the law (how things are built); this is the map (what's done vs left).
>
> **Status:** ✅ done & tested · ◐ partial / data-model-only / different shape · ❌ not started
>
> **⚠️ UPDATE 2026-06-11:** this table maps the original BACKEND scope as of 2026-06-10. A stabilization pass since then
> took the suite **108 → 181 tests** (build 0/0) and added the `BuildingBlocks.Tests` + `Hosts.Tests` projects — for the
> **authoritative current coverage see `feature-coverage.md`**, fix log in `stability-audit-2026-06-10.md`. The next big
> scope (**B2B subdomain multi-tenancy + per-tenant module entitlements + infra**) is DESIGNED, not built — see
> `multitenancy-and-infra.md`. This ROADMAP otherwise stays the durable backend map.

**Vision:** a production-grade .NET 10 modular-monolith SaaS **base** — the cross-cutting plumbing every SaaS needs
(users, pay-per-credit + subscriptions + coupons + packages, audit, GDPR, i18n, security, notifications) — onto
which **modules** add product "flavour" (toggled per deployment via `Modules:{Name}:Enabled`, so one base ships as
many products). Must scale: API 2+ instances, write DB + read replica, separate Jobs + Worker hosts, durable
event-based flow with idempotency/concurrency/self-healing. Only free, battle-tested libraries. Everything is a
`Command`/`Query`; modules talk only through `*.Contracts`.

---

## 1. Foundation — building blocks & CQRS pipeline

| Item | Status | Notes / pointer |
|---|---|---|
| Building blocks: Cqrs, Abstractions, Persistence, Messaging, Web, Telemetry | ✅ | `src/building-blocks/*` |
| Building blocks: Realtime (Redis fan-out), Storage (local/S3) | ✅ | emerged during build; `ModularPlatform.Realtime`, `.Storage` |
| Thin custom dispatcher (`ICommand`/`IQuery`/`IDispatcher`) — no MediatR | ✅ | `Cqrs/Dispatcher.cs` |
| Pipeline: Telemetry → Logging → Validation → ConcurrencyRetry | ✅ | command-only retry; queries skip it |
| Idempotency + transaction/outbox commit | ✅ (different shape) | **deviation from the plan:** moved OUT of pipeline behaviors INTO the handler — idempotency = UNIQUE key + `catch DbUpdateException`; commit = `SaveChangesAndFlushMessagesAsync`. See `CLAUDE.md` Law 2 |
| ArchUnitNET module-boundary tests (build fails on violation) | ✅ | `tests/ModularPlatform.ArchitectureTests` |
| Hosts: Api, Worker, Jobs, MigrationService | ✅ | `src/hosts/*` |
| Aspire AppHost / ServiceDefaults (dev inner-loop) | ❌ | optional; never built |

## 2. Data layer — write/read, tenancy, RLS, audit

| Item | Status | Notes |
|---|---|---|
| Write context (outbox) + read-replica factory (no-tracking) | ✅ | `AddModuleDbContext` / `AddModuleReadDbContext` |
| Optimistic concurrency | ✅ | Postgres **xmin** (plan said RowVersion; xmin chosen) + `ConcurrencyRetryBehavior` |
| Flat entities, reference by Id, no `Include`/navigation | ✅ | enforced by convention |
| Multi-tenancy: tenant filter `IsSystem ‖ TenantId == claim` (no null-escape) | ✅ | B2C per-user tenancy; `TenantStampingInterceptor` |
| Postgres **RLS** on `IUserOwned` tables (`app_rls` role + GUCs) | ✅ | `RlsBootstrapper`, `PrincipalSessionConnectionInterceptor` |
| Audit interceptor (changed fields → per-module JSONB) | ✅ | `AuditInterceptor` |
| **Audit-PII crypto-shred** (`[PersonalData]`+`IDataSubject` → encrypted under subject DEK; erasure → `[erased]`) | ✅ | `docs/audit-pii-encryption-design.md`; admin read `GET /v1/identity/admin/users/{id}/audit` |
| **PII-at-rest column encryption** (live columns, `[Encrypted]`, blind-index/HMAC lookup) | ✅ | `docs/pii-column-encryption-design.md`: encryption interceptor + decrypting converter, `users.EmailHash` blind index, `penc:v2` + AAD, backfill |

## 3. Messaging, Worker & Jobs

| Item | Status | Notes |
|---|---|---|
| Wolverine durable outbox/inbox, retry → dead-letter | ✅ | `PlatformMessaging`; the 3 gotchas in `CLAUDE.md` §9b |
| Cross-module integration events (the spine) | ✅ | `UserRegistered` → Billing/Notifications |
| Worker host (scale 2+) | ✅ | |
| Jobs host + `IModule.RegisterJobs` hook + Quartz | ✅ | canonical `BillingExpireCreditsJob` (cron) |
| **Reconciliation job** (replay Stripe events, drift detection — Stripe = source of truth) | ✅ | `ReconcileStripeCommand` + `BillingStripeReconcileJob` (stuck-event requeue + subscription drift, capped, OTel counter) |
| **Stuck-outbox / dead-letter alert job** | ✅ | `MessagingHealthJob` (Jobs host) over Wolverine `IMessageStore.Admin` → OTel gauges + WARN threshold |
| **Retention / erasure sweep job** | ✅ | `GdprRetentionSweepJob` purges shredded `subject_keys` tombstones past retention |
| **Sagas / self-healing step workflows** (Postgres-backed, compensation) | ✅ | **CreditPurchaseSaga** (Wolverine, EF-persisted): checkout → confirm → grant → completion event; abandon timeout; late payment honored |

## 4. Billing & Credits (money)

| Item | Status | Notes |
|---|---|---|
| Append-only credit ledger; invariant `available = posted − pending` | ✅ | |
| Commands: `CreditTopUp, ReserveCredits, ConfirmSpend, ReleaseHold, ExpireCredits` | ✅ | atomic EF debit guard (no double-spend, 20-way tested) |
| `GetCreditBalance` (stored `available`) | ✅ | |
| Stripe webhook: raw-body signature, UNIQUE `event.id`, 200-then-outbox, idempotent | ✅ | `Features/Stripe/StripeWebhook` + `ProcessStripeEvent` |
| `CreditPackage` (one-time Stripe Price) | ✅ | list + admin create/update (`billing.manage`) + Checkout purchase via the saga |
| **Subscriptions** (Stripe, proration, lifecycle) | ✅ | config plans, checkout/cancel/me/plans slices, lifecycle mirrored from Stripe OBJECT state, per-invoice credit grants |
| **Coupons / promo codes** (percent/amount-off) | ✅ | Stripe-owned; `AllowPromotionCodes` on every checkout + validate endpoint |
| **EU VAT via Stripe Tax (MoR)** | ✅ | `Billing:Stripe:AutomaticTax` flag on every checkout session (requires Stripe Tax active in the dashboard) |
| **Stripe reconciliation job** (out-of-order events reconciled vs object state) | ✅ | see §3 — plus every subscription webhook upserts from object state (order-proof) |
| **LemonSqueezy adapter** behind `IPaymentProvider` (opt-in) | ❌ | plan §5; not built |
| Stripe test seam (assert the real top-up applies, not just ingest) | ✅ | `IStripeGateway` + `FakeStripeGateway` — ST-1/ST-2 asserted end-to-end |

## 5. Real-time, i18n & errors

| Item | Status | Notes |
|---|---|---|
| SSE-first (`GET /v1/realtime/stream`, .NET 10 native) | ✅ | owner-scoped via token |
| Multi-instance fan-out via Redis Pub/Sub (`IRealtimePublisher`) | ✅ | `ModularPlatform.Realtime` |
| **Replay buffer (Redis Stream, `Last-Event-ID`, last-N/TTL)** | ✅ | `IRealtimeReplay`, stream id = SSE event id, in-memory fallback without Redis |
| RFC 9457 Problem Details keyed by stable `errorCode` | ✅ | `GlobalExceptionMiddleware` |
| i18n `IStringLocalizer<SharedResource>` + `.resx` (en/cs), key == errorCode | ✅ | `ApiResponse<T>` success-only |

## 6. Notifications

| Item | Status | Notes |
|---|---|---|
| `SendNotification` → in-app row + channel deliveries via outbox (never inline) | ✅ | email/push handled in Worker |
| Templates + in-app feed + `MarkNotificationRead` | ✅ | |
| **`welcome` template seeded** | ✅ | `NotificationsSeeder` (welcome + purchase_completed, en+cs) |
| Per-user email locale resolution | ◐ | resolves via command data; Worker-side assertion still open (NT-3) |

## 7. GDPR & Security

| Item | Status | Notes |
|---|---|---|
| Export fan-out (`IExportPersonalData` per module → one document) | ✅ | per-exporter isolation (GD-4 full) |
| Erasure event + per-module `IErasePersonalData` fan-out | ✅ | |
| Consent grant/withdraw/get (append-only) | ✅ | |
| Crypto-shredder (AES-256-GCM) + `SubjectKey` DEK envelope | ✅ | |
| Audit-PII crypto-shred (see §2) | ✅ | |
| Auth hardening: per-account lockout + per-IP `auth` rate-limit + refresh-reuse revoke (audited) | ✅ | |
| Rate limiter (config-driven limits) | ✅ | |
| Secrets fail-fast at startup (JWT signing key, RLS runtime password) | ✅ | |
| **PII-at-rest column encryption + blind-index** | ✅ | see §2 |
| Follow-ups: AES-GCM AAD binding of `subjectId`; blank `PasswordHash` on erasure | ✅ | `penc:v2` envelopes carry AAD; the eraser blanks `PasswordHash` |
| KEK/KMS envelope-wrapping of the DEK | ❌ | dev stores raw DEK; out of scope until GA |

## 8. Modules delivered

| Module | Status | Owns |
|---|---|---|
| **Identity** | ✅ | users, JWT, refresh rotation+reuse, profile, roles+permissions, lockout, audit decrypt read |
| **Billing** | ✅ | ledger + full Stripe commerce: packages/saga/subscriptions/coupons/Tax/reconcile (see §4) |
| **Notifications** | ✅ | email/push/in-app via outbox, templates, feed |
| **Gdpr** | ✅ | export, erasure, consent, crypto-shred keys |
| **Operations** | ✅ | long-running 202 + status polling |
| **Files** | ✅ | upload/download metadata + `IFileStorage` (local/S3) |
| **Audit (separate module)** | ✅ (different shape) | **deliberately NOT a module** (would break boundaries) — per-module audit queries + `GetUserAuditTrail` instead |

## 9. Testing

| Item | Status | Notes |
|---|---|---|
| Shared Testcontainers-Postgres harness (`PlatformApiFactory`) | ✅ | |
| Suite | ✅ | **75/75 green, build 0/0** (2026-06-10) |
| Robustness backlog **wave 1** (ID-2/6/8, PL-5, BL-6/7/8/9/12, ST-1/2/3, GD-3/4/5, EV-2, NT-1/4) | ✅ | `docs/test-scenarios.md` |
| Audit-PII tests (envelope + decrypt + erased + arch + DEK-never-audited) | ✅ | |
| Robustness **wave 2** | ✅ (mostly) | shipped 2026-06-10 — EV-3/5, ST-4/5 full, BL-5/10/11, PL-2/3/7-down/8/9, GD-4; still open: EV-4 (needs out-of-process worker), PL-10, NT-2/3 (see test-scenarios.md) |

---

## What’s left — prioritized

The full intended scope from the architecture plan is **BUILT** as of 2026-06-10 (suite green; see
`docs/test-scenarios.md` for the per-scenario map). Remaining items are deliberate deferrals:

1. **Per-need (no pattern yet — ASK, Law 11):** search, feature flags, bulk ops.
2. **Pre-GA hardening:** KEK/KMS envelope-wrapping of DEKs (dev stores the raw DEK); legal sign-off that
   crypto-shredding satisfies erasure in target jurisdictions.
3. **Test tail:** EV-4 (kill-worker durability — needs an out-of-process worker harness), PL-10 (migration
   race), NT-2/3 fault-injection/locale asserts + smaller ◐ from wave 1.
4. **Optional:** Aspire dev inner-loop; LemonSqueezy adapter behind a provider port mirroring `IStripeGateway`.

> Design docs: `billing-revenue-design.md`, `pii-column-encryption-design.md`, `ops-jobs-design.md`,
> `realtime-replay-design.md`, `audit-pii-encryption-design.md`. CLAUDE.md §4 holds the canonical examples.
