# Billing revenue features — design (packages, subscriptions, coupons, Stripe Tax, saga)

> Decision date 2026-06-10 (user-approved scope: ALL of plan §5, fully config-driven). This doc is the
> spec for the package-purchase flow, subscriptions, promo codes, Stripe Tax, the `IStripeGateway` test
> seam and the canonical **CreditPurchaseSaga** (the platform's first Wolverine saga — plan §4).

## 1. The Stripe seam — `IStripeGateway` (anti-corruption port)

All Stripe SDK calls go through one **internal** port in Billing Core (`Stripe/IStripeGateway.cs`):

| Method | Used by |
|---|---|
| `GetEventAsync(eventId)` | `ProcessStripeEventCommand` (replaces the inline `new EventService()`) |
| `CreateCheckoutSessionAsync(spec)` | package purchase + subscription checkout |
| `GetSubscriptionAsync(id)` | webhook reconcile (**object state**, never event order) + reconciliation job |
| `CancelSubscriptionAsync(id, atPeriodEnd)` | CancelSubscription |
| `FindActivePromotionCodeAsync(code)` | ValidatePromoCode |

- Wraps `Stripe.net` (`StripeClient` built from `Billing:Stripe:ApiKey`). Returns our own records
  (`CheckoutSessionRef`, `StripeSubscriptionState`, `PromotionCodeState`) — Stripe model quirks stay in the
  adapter. `GetEventAsync` returns the raw `Stripe.Event` (the webhook layer already speaks it).
- No ApiKey configured + a gateway call → `BusinessRuleException("billing.stripe.not_configured")`.
- **Test seam:** `Billing:Stripe:UseFakeGateway=true` (set by `PlatformApiFactory`, never in prod) registers
  the in-memory `FakeStripeGateway` singleton — tests seed events/subscriptions through it and the full
  worker path (ledger top-up + `ProcessedAt`) becomes assertable (closes ST-1/ST-2).

## 2. Config (all knobs, `StripeOptions` / `SubscriptionOptions`)

```
Billing:Stripe:ApiKey                    secret, required for live Stripe calls
Billing:Stripe:WebhookSecret             existing
Billing:Stripe:AutomaticTax              bool, default false → Stripe Tax (MoR) on every checkout session
Billing:Stripe:AllowPromotionCodes       bool, default true  → promo-code box on Stripe Checkout
Billing:Stripe:SuccessUrl / CancelUrl    required to create a checkout session
Billing:Stripe:UseFakeGateway            bool, default false — TESTS ONLY
Billing:Stripe:CheckoutTimeoutMinutes    int, default 120 — saga abandon timeout
Billing:Subscriptions:CancelAtPeriodEnd  bool, default true (proration-safe cancel)
Billing:Subscriptions:Plans:N:{PlanKey,StripePriceId,CreditsPerPeriod,BucketExpiryDays}
```
Plans are **config, not DB** (per-deployment product shaping, consistent with `Modules:{Name}:Enabled`).
Packages are **DB catalogue** (`credit_packages` already existed) managed via admin endpoints.

## 3. Package purchase flow (+ the canonical saga)

```
POST /v1/billing/packages/{id}/checkout   (auth)            GET /v1/billing/packages (auth, active only)
  → load active package → purchaseId = v7 guid
  → gateway.CreateCheckoutSession(mode=payment, price=StripePriceId,
       metadata: purchase_type=package, purchase_id, user_id, package_id, credit_amount, bucket_expiry_days,
       automatic_tax, allow_promotion_codes, client_reference_id=purchaseId)
  → outbox.Publish(CreditPurchaseStarted{Id=purchaseId, …})  → 200 { purchaseId, checkoutUrl }

Worker: CreditPurchaseSaga.Start(CreditPurchaseStarted)      ← saga row in billing.credit_purchase_sagas
  + schedules CreditPurchaseTimeout (TimeoutMessage, Billing:Stripe:CheckoutTimeoutMinutes)

Stripe webhook checkout.session.completed (signed, UNIQUE event id, outboxed — existing ingest)
  → ProcessStripeEventCommand → metadata purchase_type=package
  → publish CreditPurchaseConfirmed{Id=purchase_id, …} (outbox)

Saga.Handle(CreditPurchaseConfirmed) → dispatch CreditTopUpCommand(idempotency key = "purchase:{purchaseId}")
  → Status=Completed, MarkCompleted, cascade CreditPurchaseCompletedIntegrationEvent (Contracts)
Saga.Handle(CreditPurchaseTimeout)  → still Pending → Status=Abandoned, MarkCompleted (compensation: nothing
  was granted; an after-timeout confirm is handled by the static NotFound + direct idempotent top-up, so a
  late Stripe payment is NEVER lost)
GET /v1/billing/purchases/{id} (auth, RLS) → saga row status (404 until the worker materializes it)
```
- Saga state = Wolverine **EF Core saga persistence** in `BillingDbContext` (`credit_purchase_sagas`,
  `IUserOwned` → RLS). The saga class is PUBLIC (Wolverine codegen — same precedent as handler shells) and
  registered via `Discovery.IncludeType<CreditPurchaseSaga>()`.
- Money stays in the LEDGER: the saga never mutates balances itself — it dispatches the existing idempotent
  `CreditTopUpCommand`. Purchase-scoped idempotency key (`purchase:{id}`) dedups even distinct Stripe events
  for the same session.

## 4. Subscriptions (Stripe Billing, lifecycle via object-state reconcile)

- Entity `Subscription` (AuditableEntity + IUserOwned, table `subscriptions`): UserId, PlanKey,
  StripeSubscriptionId (UNIQUE), StripeCustomerId?, Status (Pending/Active/PastDue/Canceled, string),
  CurrentPeriodEnd?, CancelAtPeriodEnd. **Local rows are written ONLY from Stripe object state** (webhook +
  reconcile job) — Stripe is the source of truth; checkout does not pre-create a row.
- Slices: `GET /billing/subscriptions/plans` (config), `POST /billing/subscriptions/checkout` (PlanKey;
  rejects when a non-canceled sub exists — `billing.subscription.already_active`), `GET /billing/subscriptions/me`,
  `POST /billing/subscriptions/cancel` (gateway cancel → webhook finalizes; local CancelAtPeriodEnd set eagerly).
- Webhooks (all through the existing ingest): `customer.subscription.created|updated|deleted` →
  `UpsertSubscriptionFromStripeCommand(stripeSubscriptionId)` → `gateway.GetSubscriptionAsync` → upsert by
  UNIQUE StripeSubscriptionId (+`catch DbUpdateException`) → status transitions publish
  `SubscriptionActivated/CanceledIntegrationEvent`. Out-of-order events converge on object state (ST-4).
- `invoice.paid` → `GrantSubscriptionCreditsCommand`: plan.CreditsPerPeriod > 0 → idempotent
  `CreditTopUpCommand` with key `sub-invoice:{invoiceId}` (per-period grant, exactly once per invoice).
- Proration = Stripe default behavior on plan changes; we do not mutate subscription items locally.

## 5. Coupons / promo codes

Stripe-owned. `AllowPromotionCodes` flag flows into every checkout session.
`GET /billing/promo-codes/{code}/validate` → `ValidatePromoCodeQuery` → gateway lookup →
`{ valid, percentOff?, amountOff?, currency? }`; unknown/inactive → `billing.coupon.invalid` (404).

## 6. Stripe Tax / EU VAT

`Billing:Stripe:AutomaticTax=true` → `automatic_tax.enabled` on every checkout session (payment +
subscription). Requires Stripe Tax activated in the Stripe dashboard — config-gated so deployments without
it keep working.

## 7. Admin package management

`POST /billing/admin/packages` + `PUT /billing/admin/packages/{id}` gated by NEW permission
`PlatformPermissions.BillingManage = "billing.manage"` (auto-seeded, auto-granted to admin).

## 8. Error codes (resx en+cs)

`billing.stripe.not_configured`, `billing.package_not_found`, `billing.package_inactive`,
`billing.package.price_not_configured`, `billing.purchase_not_found`, `billing.coupon.invalid`,
`billing.subscription.plan_not_found`, `billing.subscription.already_active`, `billing.subscription.not_found`
(+ validator codes `billing.*.required` style as needed).

## 9. What this deliberately does NOT do

- No LemonSqueezy adapter (not selected; `IStripeGateway` is the seam a future provider port would mirror).
- No Stripe Customer Portal/self-serve plan switch (future slice — webhook reconcile already absorbs it).
- No DB-stored coupon state — Stripe owns discounts end-to-end.
