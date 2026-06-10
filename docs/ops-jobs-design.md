# Operational jobs — design (Stripe reconciliation, messaging health alert, retention sweep)

> Decision 2026-06-10: alert target = **structured log + OpenTelemetry metric** (infrastructure alerting —
> Grafana/New Relic — owns thresholds/paging). No e-mail/in-app dependency from jobs.

## 0. Platform metrics plumbing (new, Telemetry building-block)

`PlatformMetrics` static holder (mirrors `PlatformTelemetry`): one `Meter("ModularPlatform")` + factory
helpers. `AddPlatformTelemetry` adds `.AddMeter(PlatformMetrics.MeterName)` to `WithMetrics` so instruments
actually export. Naming: `platform.{area}.{thing}` (`platform.messaging.dead_letters`,
`platform.billing.stripe_drift`, `platform.gdpr.retention_swept`).

## 1. Stripe reconciliation job (Billing)

- `BillingStripeReconcileJob` (thin `[DisallowConcurrentExecution]` IJob → `ReconcileStripeCommand`),
  cron `Modules:Billing:Jobs:ReconcileStripeCron`, default every 6 h (`0 0 */6 * * ?`).
- `ReconcileStripeHandler` (slice `Features/Stripe/ReconcileStripe/`):
  1. **Stuck events:** `stripe_events` with `ProcessedAt IS NULL AND ReceivedAt < now − 30 min` →
     re-publish `ProcessStripeEventMessage` (the router is idempotent), log warn + counter.
  2. **Subscription drift:** local non-Canceled subscriptions → `IStripeGateway.GetSubscriptionAsync` →
     status/periodEnd/cancelAtPeriodEnd differ ⇒ dispatch `UpsertSubscriptionFromStripeCommand` (Stripe wins),
     log warn + `platform.billing.stripe_drift` counter (tag `kind=subscription`).
  Caps per run (e.g. 200 events / 500 subs) to bound Stripe API usage; log when capped.

## 2. Messaging health / stuck-outbox / dead-letter alert (Jobs HOST — platform concern, not a module)

- `MessagingHealthJob` registered by the Jobs host itself (Program.cs), cron `Messaging:HealthCheckCron`,
  default every 5 min.
- Reads Wolverine's own admin API — **never SQL over wolverine tables** (repo law): resolve `IMessageStore`
  from DI, `Admin.FetchCountsAsync()` → `PersistedCounts` (incoming/outgoing/scheduled/dead-letter/handled).
- Emits `platform.messaging.dead_letters`, `platform.messaging.incoming_pending`,
  `platform.messaging.outgoing_pending` gauges (ObservableGauge or per-run record) + a structured WARN log
  when dead-letter > 0 or pending exceeds `Messaging:StuckThreshold` (default 100).

## 3. Retention / erasure sweep (Gdpr)

- `GdprRetentionSweepJob` via `GdprModule.RegisterJobs`, cron `Modules:Gdpr:Jobs:RetentionSweepCron`,
  default daily (`0 0 3 * * ?`).
- `RetentionSweepCommand` handler: hard-DELETE `subject_keys` rows where `DeletedAt < now − RetentionDays`
  (`Gdpr:Retention:ShreddedKeyRetentionDays`, default 30) — the DEK is already shredded (null), the row is
  just a tombstone. Count → `platform.gdpr.retention_swept` counter + info log.
- Cross-module retention stays per-module by law (no generic reaper over other modules' tables); this job
  is the canonical example a module copies for its own retention.

## Shared rules

- Jobs = thin IJob → command; all logic in slice handlers under system context (`SystemTenantContext`).
- Quartz cron = 6-field; defaults hardcoded next to the config key read.
- Sweep handlers idempotent — Jobs host may double-fire when scaled (no clustering configured).
