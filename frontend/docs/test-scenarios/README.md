# Frontend test scenarios — ModularPlatform

The complete Given/When/Then test catalog for the Next.js frontend, plus a runnable Playwright
E2E suite (`frontend/e2e/`). Each area below has a catalog file here and a matching `*.spec.ts`.

Each scenario carries: **Given / When / Then**, a **Priority** (P0–P2), a **Type**
(happy · edge · error · security · a11y), and an **Automated** marker
(`yes (e2e: …)` · `partial` · `manual`). Scenarios that need infrastructure the local stack can't
provide (live Stripe checkout, an admin-role user, the `admin.` host, the InviteOnly flow) are
catalogued and marked **manual** with their preconditions.

## Areas

| Area | Catalog | Spec |
|---|---|---|
| Authentication (register / login / logout / guards) | [auth.md](auth.md) | `e2e/auth.spec.ts` |
| Dashboard | [dashboard.md](dashboard.md) | `e2e/dashboard.spec.ts` |
| Billing (credits, packages, promo, subscription) | [billing.md](billing.md) | `e2e/billing.spec.ts` |
| Files (upload / list / download) | [files.md](files.md) | `e2e/files.spec.ts` |
| Notifications (feed / mark-read) | [notifications.md](notifications.md) | `e2e/notifications.spec.ts` |
| Operations (long-running task history) | [operations.md](operations.md) | `e2e/operations.spec.ts` |
| Account profile | [account-profile.md](account-profile.md) | `e2e/account-profile.spec.ts` |
| Privacy / GDPR (consents / export / erase) | [privacy.md](privacy.md) | `e2e/privacy.spec.ts` |
| Identity admin (roles / audit) | [identity-admin.md](identity-admin.md) | `e2e/identity-admin.spec.ts` |
| Platform admin (`admin.` host) | [platform-admin.md](platform-admin.md) | `e2e/platform-admin.spec.ts` |
| `/design` component gallery | [design-gallery.md](design-gallery.md) | `e2e/design-gallery.spec.ts` |
| Cross-cutting: realtime · errors · i18n · theme · a11y · entitlement gating | [cross-cutting.md](cross-cutting.md) | `e2e/cross-cutting.spec.ts` |
| Cross-cutting: security (BFF / CSRF / no-token-in-JS / headers) | [security.md](security.md) | `e2e/security.spec.ts` |

## Running the E2E suite

The suite runs against a **live stack**: the .NET API on `:5271` (+ migrated Postgres on `:5432`)
and the Next dev server on `:3000` (Playwright auto-starts the dev server; the backend must be
started separately).

```bash
# 1. Start the backend (separate terminal):
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5271 \
  dotnet run --project src/hosts/ModularPlatform.Api --no-launch-profile

# 2. Run the suite (from frontend/):
pnpm e2e            # headless
pnpm e2e:ui         # interactive UI mode
pnpm e2e:report     # open the last HTML report
```

### How the suite is wired
- **Serial** (`workers: 1`): registration shares one per-IP `auth` rate-limit and the backend state
  is shared, so parallel runs flake.
- A **`setup` project** registers one primary user and saves its authenticated session to
  `e2e/.auth/user.json`; the **`chromium` project** reuses it, so most specs start logged-in.
- Specs that must be logged-out use `test.use(ANONYMOUS)` (from `e2e/helpers.ts`); destructive or
  isolation-sensitive specs (erase account, consent writes) register a **fresh** user via
  `registerFreshUser`.
- Self-serve registration provisions a tenant with the default module entitlements, so a fresh user
  immediately has billing / files / notifications / operations / gdpr.

See `e2e/helpers.ts` for the shared fixtures.
