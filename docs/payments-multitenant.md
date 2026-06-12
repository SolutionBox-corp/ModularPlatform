# Payments — provider-agnostic, per-tenant, two planes

> **Status (2026-06-12):** the `ModularPlatform.Payments` building-block is **BUILT** (port + Stripe + GoPay +
> Fake + resolver + config-store port), and the Billing module owns per-tenant gateway config + secret storage
> (`PaymentConfiguration` + `TenantSecret`) with the self-service `ConfigureGateway` slice. The per-tenant **package
> purchase** now resolves the tenant's gateway via `IPaymentGatewayResolver`, and the per-tenant webhook **endpoint**
> `POST /billing/webhooks/{provider}/{tenantId}/{token?}` (`TenantWebhookEndpoint`) is **wired** — it re-fetches
> authoritative state and outboxes `CreditPurchaseConfirmed` into the existing idempotent saga grant. Subscription
> checkout + the `payment_events` table generalisation remain the next step. Where a thing is not yet wired
> end-to-end, this doc says so explicitly.
>
> This doc is the single map of how money moves. `CLAUDE.md` (root) stays the law; when they disagree, the law wins.

---

## 1. Two planes (never conflate them)

There are two completely independent money flows. They share the *same* `IPaymentGateway` port and the *same*
resolver, but they resolve to **different** `(tenant, plane)` configurations and the money lands in different
accounts.

| Plane | Who pays whom | Whose gateway / credentials | Owning module |
|---|---|---|---|
| **`PaymentPlane.Platform`** | a **tenant → the SaaS operator** (us) — pays for the platform itself, tied to the entitlement tier | the **platform's own** gateway (one operator account) | **Tenancy** (tenant lifecycle owns platform billing) |
| **`PaymentPlane.Tenant`** | an **end-user → the tenant** — pays the tenant for the tenant's own products | the **tenant's own** gateway/credentials; **the money never touches the platform** | **Billing** (end-user commerce) |

Canonical enum: `src/building-blocks/ModularPlatform.Payments/PaymentResolution.cs` (`PaymentPlane`).

This is the application of the module-placement rule (CLAUDE.md §3 / program §0): platform-plane billing is the
**Tenancy** module's responsibility (it owns tenant lifecycle), reusing the shared `Payments` building-block —
there is deliberately **no** separate `PlatformBilling` module. Tenant-plane commerce stays in **Billing**.

**Platform-plane checkout is price-server-authoritative.** `POST /v1/tenant/me/platform-checkout`
(`CreatePlatformCheckout`, denies machine principals) takes a **plan KEY**, never a free-form amount — the
amount/currency/description come from `Platform:Payments:Plans:{key}` config (an unknown key → `tenancy.platform_plan_unknown`).
A client-supplied amount would let a tenant pay €0.01 for a plan. The platform gateway itself is read from
`Platform:Payments:*` by `TenancyPlatformPaymentConfigStore` (the platform's own account, not a tenant's).

---

## 2. The one Stripe/GoPay seam — `IPaymentGateway`

All providers sit behind a single neutral port so no business code references Stripe.net or GoPay HTTP shapes.

- **Port:** `IPaymentGateway` — `src/building-blocks/ModularPlatform.Payments/IPaymentGateway.cs`
  (`CreateCheckout` / `GetPaymentState` / `Refund` / `VerifyNotification` / `ValidateCredentials` / `Capabilities`).
- **Neutral types:** `src/building-blocks/ModularPlatform.Payments/PaymentTypes.cs` — `PaymentState` enum,
  `CheckoutRequest`/`CheckoutResult`, `PaymentSnapshot`, `RefundResult`, `NotificationContext`, `GatewayCapabilities`.
  **Amounts are always minor units** (haléře/cents) to avoid float drift.
- **Adapters:**
  - `StripePaymentGateway` — `…/StripePaymentGateway.cs` (constructed per-tenant with `(apiKey, webhookSecret)`,
    **never** a DI singleton — credentials are per-tenant).
  - `GoPayPaymentGateway` — `…/GoPayPaymentGateway.cs`.
  - `FakePaymentGateway` — `…/FakePaymentGateway.cs` (test harness only; re-fetch model with `SetState`).
- **State mapping:** `…/PaymentStateMapping.cs` (`FromGoPay` / Stripe → neutral `PaymentState`).

**Leaky provider differences are pushed into the type, not forced into a common shape:**

| Concern | Stripe | GoPay |
|---|---|---|
| Webhook trust | signed HMAC — `NotificationContext.RawBody` + signature header (`EventUtility.ConstructEvent`) | **no signature** — `NotificationContext.Query` carries only the payment id |
| Authoritative state | re-fetch the object after the event (never trust payload order) | **always re-fetch** `GET /payments/payment/{id}` — the notification is a *hint* only |
| Idempotency | app-side UNIQUE key | app-side UNIQUE key (GoPay has no idempotency header) |
| Auth | API key | OAuth2 `client_credentials` (Basic → `/oauth2/token`), bearer cached in a process-wide singleton `GoPayTokenCache` keyed by client id (the resolver builds a throwaway gateway per request, so a per-instance cache would re-auth every call; 60 s safety margin) |
| Payee | account on the key | `target.goid` per create |

### The GoPay re-fetch model (critical)

GoPay does **not** sign its notifications. The inbound `GET` to the notification URL is only a wake-up. The
adapter's `VerifyNotification` reads the payment id from the query and **re-fetches the payment object from GoPay**
to get the authoritative state. **Never** treat the GoPay notification body as authoritative; **never** branch on
it without a re-fetch. The notification URL itself carries a high-entropy unguessable token (`WebhookToken`); the
`ProcessTenantWebhook` handler **validates** it against the stored token for the GoPay (unsigned) path and
acknowledges-and-ignores a mismatch — but authoritative trust still comes only from the re-fetch.

---

## 3. Resolving the right gateway — `(tenant, plane)`

```
IPaymentGatewayResolver.ResolveAsync(tenantId, plane, ct)  → IPaymentGateway bound to that tenant's credentials
```

- Port + impl: `…/PaymentResolution.cs` (`IPaymentGatewayResolver` / `PaymentGatewayResolver`).
- The resolver calls `IPaymentConfigStore.GetAsync(tenantId, plane)` to get a **fully-decrypted**
  `ResolvedPaymentConfig` (provider, currency, active flag, plaintext credentials), then picks the adapter.
- A **missing** config → `PaymentGatewayUnavailableException("payment.gateway_not_configured")`; an **inactive**
  config → `…("payment.gateway_inactive")`. The resolver **never** silently falls back to another tenant's gateway.
- The plaintext credentials live for the lifetime of one resolve only — never persisted, logged, or put on an
  event/outbox envelope.

### Per-tenant config + secrets (who owns the data)

The building-block stays free of any module entity; the **owning module** implements `IPaymentConfigStore` and does
the secret decryption:

- **Billing** (tenant plane): `BillingPaymentConfigStore` — `src/modules/Billing/…/Payments/BillingPaymentConfigStore.cs`.
- **Entities** — `src/modules/Billing/…/Entities/PaymentGatewayEntities.cs`:
  - `PaymentConfiguration` (table `payment_configurations`) — metadata only: `TenantId`, `Plane`, `Provider`,
    `Currency`, `Status`, `WebhookToken`, `GoPayGoid`, `Sandbox`; UNIQUE `(TenantId, Plane)`.
  - `TenantSecret` (table `tenant_secrets`) — ciphertext only (`KeyVersion`, `Ciphertext`, `WrappedDek?`); UNIQUE
    `(TenantId, Purpose)`. Purposes: `stripe.api_key`, `stripe.webhook_secret`, `gopay.client_id`,
    `gopay.client_secret`. (See `tenant-secrets.md` for the secret model.)
- **Self-service onboarding:** the `ConfigureGateway` slice — `PUT /v1/billing/payment-gateway` — gated
  `.RequirePermission(billing.manage)` **and** `.RequireModule("billing")` (the live entitlement guard). It seals
  each credential via `ISecretProtector.ProtectAsync(tenantId, purpose, plaintext)` and mints the per-tenant
  `WebhookToken`. Endpoint: `…/Features/PaymentGateway/ConfigureGateway/ConfigureGatewayEndpoint.cs`.

### The explicit-`TenantId` landmine (PaymentConfiguration is NOT `ITenantScoped`)

`PaymentConfiguration` and `TenantSecret` carry an **explicit** `Guid TenantId` and are **not** `ITenantScoped`.
The reason is the resolver runs in the **SYSTEM Worker** context (processing an inbound webhook for an *arbitrary*
tenant), where the ambient EF tenant filter is bypassed (`IsSystem`). An automatic per-tenant query filter would
**hide** the very row the webhook needs to find. So the config store filters by the **explicit** tenant id and uses
`IgnoreQueryFilters()` — never the ambient tenant.

This generalizes to **all** Worker-side money handlers: the Worker is SYSTEM, so it neither auto-stamps nor
auto-filters `TenantId`. Every webhook/saga/grant handler that writes a tenant-scoped row must carry the
`TenantId` through the message/metadata and **stamp it explicitly** — the program's **#1 correctness landmine**
(`UserRegisteredIntegrationEvent` already demonstrates this with an explicit `TenantId` field that
`RegisterUserHandler` fills and `ProvisionCreditAccountHandler` re-stamps; see `CrossModuleEventTests`).

---

## 4. Per-tenant webhook routing

GoPay notifications and the future per-tenant Stripe webhooks are addressed **per tenant** so the SYSTEM Worker
knows which tenant's config to resolve (it can't derive a tenant from an HTTP context it doesn't have):

```
POST /v1/billing/webhooks/{provider}/{tenantId:guid}/{token?}
```

- **Endpoint:** `…/Features/Stripe/TenantWebhook/TenantWebhookEndpoint.cs` — anonymous, rate-limit-disabled
  (providers retry on non-200), returns 200 immediately and dispatches `ProcessTenantWebhookCommand`.
- The notification URL is constructed in `BillingPaymentConfigStore.BuildNotificationUrl` from
  `Billing:Payments:PublicBaseUrl` + the tenant id + the per-tenant `WebhookToken`. The `{token?}` segment is
  optional so a signed-Stripe callback (no token) and an unsigned-GoPay callback (token) both match the one route.
- The `tenantId` segment routes the inbound notification to the right `(tenant, plane)` config.
- The `token` segment is the unguessable guard for providers with **no** signed payload (GoPay); the handler
  validates it against the stored `WebhookToken` for the GoPay path and ignores a mismatch (Stripe ignores it — its
  HMAC signature is the binding).
- The handler resolves the gateway via `IPaymentGatewayResolver.ResolveAsync(tenantId, plane)`, calls
  `VerifyNotification` (which **re-fetches** the authoritative state), and on a PAID package checkout outboxes
  `CreditPurchaseConfirmed` — the existing saga grants exactly once via the `purchase:{id}` idempotency key.

> **Still to generalise:** the legacy single Stripe webhook (`…/Stripe/StripeWebhook/StripeWebhookEndpoint.cs`)
> remains for the platform-plane Stripe ingest; broadening the `payment_events` table (`StripeEvent` →
> `Provider` + `TenantId`, UNIQUE `(Provider, TenantId, EventId)`) to every provider is the next step.

---

## 5. Reconciliation (per provider, per tenant)

External-system drift is corrected by a per-module reconciliation **job** (CLAUDE.md §4 "External-system
reconciliation"), not a generic cross-module reconciler:

- `ReconcileStripeCommand` + `BillingStripeReconcileJob` (cron `Modules:Billing:Jobs:ReconcileStripeCron`) —
  `src/modules/Billing/…/Features/Stripe/ReconcileStripe/` + `…/Jobs/BillingStripeReconcileJob.cs`.
- LIVE provider state wins; drift → WARN + `platform.billing.stripe_drift` counter.
- Under multi-tenant the sweep runs **per `(tenant, provider)`** (each tenant has its own credentials/config), and
  each corrected row is stamped with its explicit `TenantId` (the Worker-SYSTEM rule again).

This is also device-module seam #8 (per-module reconciliation pattern available for desired-vs-reported drift).

---

## 6. Error codes (resx en + cs both required)

| Code | Meaning |
|---|---|
| `payment.gateway_not_configured` | no usable `(tenant, plane)` gateway config |
| `payment.gateway_inactive` | config exists but `Status != Active` |
| `billing.gateway.stripe_webhook_secret_required` | Stripe config REQUIRES a webhook signing secret (an absent one would 500-loop every retry) |
| `tenancy.platform_plan_unknown` | platform-plane checkout got a plan key not in `Platform:Payments:Plans` |
| `tenancy.platform_plan_misconfigured` | the named platform plan has no positive `AmountMinorUnits` |

Every payment error code has an entry in **both** `SharedResource.resx` (en) and `SharedResource.cs.resx` (cs) —
the ArchUnitNET resx-parity test enforces it.

---

## 7. Do / don't (money correctness)

- **DO** call any provider only through `IPaymentGateway` via `IPaymentGatewayResolver.ResolveAsync(tenant, plane)`.
- **DO** re-fetch authoritative state after any notification (mandatory for GoPay, safe for Stripe).
- **DO** stamp `TenantId` explicitly on every Worker-side row (the Worker is SYSTEM — no auto-stamp/filter).
- **DO** keep amounts in minor units; keep the append-only ledger + UNIQUE idempotency key for grants.
- **DON'T** construct `StripePaymentGateway` / `GoPayPaymentGateway` as a DI singleton — credentials are per-tenant.
- **DON'T** put plaintext credentials on an event, outbox envelope, or log — carry only `(TenantId, Purpose)`.
- **DON'T** trust a GoPay notification body or Stripe webhook payload order — re-fetch.
- **DON'T** make `PaymentConfiguration`/`TenantSecret` `ITenantScoped` — the SYSTEM resolver must read any tenant.
