# ModularPlatform — ROADMAP ("everything that must be")

> The full intended scope, reconstructed from the original architecture plan
> (`~/.claude/plans/ultracode-tohle-budem-musel-transient-papert.md`, the binding "what must be" doc) and reconciled
> against the **actual build status** as of **2026-06-10**. This is the durable, in-repo successor to that
> session-local plan file. `CLAUDE.md` is the law (how things are built); this is the map (what's done vs left).
>
> **Status:** ✅ done & tested · ◐ partial / data-model-only / different shape · ❌ not started

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
| **PII-at-rest column encryption** (live columns via `ValueConverter`+per-subject DEK, `[Encrypted]`, blind-index/HMAC lookup) | ❌ | plan §8; only AUDIT PII is encrypted today, not the live `users`/etc. columns |

## 3. Messaging, Worker & Jobs

| Item | Status | Notes |
|---|---|---|
| Wolverine durable outbox/inbox, retry → dead-letter | ✅ | `PlatformMessaging`; the 3 gotchas in `CLAUDE.md` §9b |
| Cross-module integration events (the spine) | ✅ | `UserRegistered` → Billing/Notifications |
| Worker host (scale 2+) | ✅ | |
| Jobs host + `IModule.RegisterJobs` hook + Quartz | ✅ | canonical `BillingExpireCreditsJob` (cron) |
| **Reconciliation job** (replay Stripe events, drift detection — Stripe = source of truth) | ❌ | plan §4/§5; **0 reconcile code in repo** |
| **Stuck-outbox / dead-letter alert job** | ❌ | §10 open |
| **Retention / erasure sweep job** | ❌ | erasure-on-request exists; periodic sweep not built |
| **Sagas / self-healing step workflows** (Postgres-backed, compensation) | ❌ | plan §4; §10 "ASK before inventing" |

## 4. Billing & Credits (money)

| Item | Status | Notes |
|---|---|---|
| Append-only credit ledger; invariant `available = posted − pending` | ✅ | |
| Commands: `CreditTopUp, ReserveCredits, ConfirmSpend, ReleaseHold, ExpireCredits` | ✅ | atomic EF debit guard (no double-spend, 20-way tested) |
| `GetCreditBalance` (stored `available`) | ✅ | |
| Stripe webhook: raw-body signature, UNIQUE `event.id`, 200-then-outbox, idempotent | ✅ | `Features/Stripe/StripeWebhook` + `ProcessStripeEvent` |
| `CreditPackage` (one-time Stripe Price) | ◐ | **entity/table only — no purchase or list slice/endpoint** |
| **Subscriptions** (Stripe, proration, lifecycle) | ❌ | plan §5; not built |
| **Coupons / promo codes** (percent/amount-off) | ❌ | plan §5; not built |
| **EU VAT via Stripe Tax (MoR)** | ❌ | plan §5; not built |
| **Stripe reconciliation job** (out-of-order events reconciled vs object state) | ❌ | plan §5; not built |
| **LemonSqueezy adapter** behind `IPaymentProvider` (opt-in) | ❌ | plan §5; not built |
| Stripe test seam (assert the real top-up applies, not just ingest) | ◐ | worker does a live `EventService().GetAsync`; tests cover ingest only — see `docs/test-scenarios.md` ST-1/2 |

## 5. Real-time, i18n & errors

| Item | Status | Notes |
|---|---|---|
| SSE-first (`GET /v1/realtime/stream`, .NET 10 native) | ✅ | owner-scoped via token |
| Multi-instance fan-out via Redis Pub/Sub (`IRealtimePublisher`) | ✅ | `ModularPlatform.Realtime` |
| **Replay buffer (Redis Stream, `Last-Event-ID`, last-N/TTL)** | ◐ | verify — partial references; not confirmed end-to-end |
| RFC 9457 Problem Details keyed by stable `errorCode` | ✅ | `GlobalExceptionMiddleware` |
| i18n `IStringLocalizer<SharedResource>` + `.resx` (en/cs), key == errorCode | ✅ | `ApiResponse<T>` success-only |

## 6. Notifications

| Item | Status | Notes |
|---|---|---|
| `SendNotification` → in-app row + channel deliveries via outbox (never inline) | ✅ | email/push handled in Worker |
| Templates + in-app feed + `MarkNotificationRead` | ✅ | |
| **`welcome` template seeded** | ❌ | finding: no env seeds it → welcome never produces an in-app row |
| Per-user email locale resolution | ◐ | resolves; not asserted (NT-3) |

## 7. GDPR & Security

| Item | Status | Notes |
|---|---|---|
| Export fan-out (`IExportPersonalData` per module → one document) | ✅ | resilience-if-one-throws not yet (GD-4) |
| Erasure event + per-module `IErasePersonalData` fan-out | ✅ | |
| Consent grant/withdraw/get (append-only) | ✅ | |
| Crypto-shredder (AES-256-GCM) + `SubjectKey` DEK envelope | ✅ | |
| Audit-PII crypto-shred (see §2) | ✅ | |
| Auth hardening: per-account lockout + per-IP `auth` rate-limit + refresh-reuse revoke (audited) | ✅ | |
| Rate limiter (config-driven limits) | ✅ | |
| Secrets fail-fast at startup (JWT signing key, RLS runtime password) | ✅ | |
| **PII-at-rest column encryption + blind-index** | ❌ | the larger crypto-shred vision (plan §8); only audit PII done |
| Follow-ups: AES-GCM AAD binding of `subjectId`; blank `PasswordHash` on erasure | ❌ | from the audit-PII security review (not leaks) |
| KEK/KMS envelope-wrapping of the DEK | ❌ | dev stores raw DEK; out of scope until GA |

## 8. Modules delivered

| Module | Status | Owns |
|---|---|---|
| **Identity** | ✅ | users, JWT, refresh rotation+reuse, profile, roles+permissions, lockout, audit decrypt read |
| **Billing** | ◐ | credit ledger ✅; subscriptions/coupons/packages/reconcile ❌ (see §4) |
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
| Robustness **wave 2** (EV-3/4, PL-2/3/7-down/8/9/10, BL-5/10/11, ST-4/5, NT-2/3, GD-4 resilience) | ❌ | `docs/test-scenarios.md` "Priority gaps" |

---

## What's left — prioritized

1. **Billing revenue features** (biggest product-value gap vs the vision): subscriptions (Stripe, proration,
   lifecycle), coupons/promo codes, a **package purchase/list slice** (entity exists, no flow), Stripe Tax/EU VAT.
2. **Operational robustness jobs**: Stripe **reconciliation**, **stuck-outbox/dead-letter alert**, retention/erasure
   sweep (§10 + plan §4/§5).
3. **PII-at-rest column encryption + blind-index** — completes the crypto-shred vision (plan §8); audit-PII was one slice.
4. **Sagas / self-healing step workflows** (§10 — ASK first: saga vs orchestrating command-chain).
5. **Robustness test wave 2** + the Stripe test seam (full ST-1/2) + GD-4 export resilience.
6. **Smaller**: realtime replay buffer (`Last-Event-ID`); seed the `welcome` template; AES-GCM AAD; `PasswordHash`
   erasure; per-user email locale assertion.
7. **Per-need (no pattern yet — ASK):** search, feature flags, bulk ops (§10).
8. **Optional:** Aspire dev inner-loop.

> Each non-trivial item gets its own spec (`docs/*-design.md`) before implementation, per the spec-driven flow.
> Decisions with "no canonical example" (sagas, search, flags, bulk ops, the encryption-at-rest column model) must
> be brought to the user first — Law 11.
