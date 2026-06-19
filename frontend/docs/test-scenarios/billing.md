# Billing Page — Test Scenarios

Route: `/billing` (requires auth + `billing` module entitlement)

The billing page renders four sections:
1. **Balance card** (`CreditBalanceCard`) — shows Available and Posted credits via `GET /v1/billing/credits/balance`.
2. **Subscription card** (`SubscriptionCard`) — shows active plan / empty state via `GET /v1/billing/subscriptions/me`.
3. **Buy credits** (`PackagesGrid`) — lists packages from `GET /v1/billing/packages`; empty state when none.
4. **Promo code** (`PromoCodeInput`) — validates a code via `GET /v1/billing/promo-codes/{code}/validate`.
5. **Credit balance table** (`CreditSummaryTable`) — three rows: Posted / Available / Held, derived from the balance endpoint.

Credit values render as `"N cr."` with tabular-nums via `MoneyAmount`. Checkout redirects are guarded by
`safeExternalRedirect` (must be `https:` + `*.stripe.com`).

---

## Scenarios

### BILL-01 — Balance card renders Available and Posted values

- **Given** an authenticated user with a provisioned credit account
- **When** the user navigates to `/billing`
- **Then** the **Credits** card is visible, showing `"Available balance"` subtitle, a credit amount in `"N cr."` format, and a `"of N cr. posted"` sub-line
- Priority: P0 · Type: happy · Automated: yes (e2e: `balance card renders credit values`)

---

### BILL-02 — Balance card empty state (no account yet)

- **Given** a brand-new user whose credit account has not been provisioned
- **When** the API returns no data for `GET /v1/billing/credits/balance`
- **Then** the Credits card shows `"No account yet."` instead of a credit amount
- Priority: P1 · Type: edge · Automated: manual (requires unfunded account; typical fresh tenants get provisioned by the `UserRegistered` handler via Billing — the default registered user will already have an account)

---

### BILL-03 — Credit summary table has three rows

- **Given** an authenticated user with a credit account
- **When** the user navigates to `/billing` and the balance loads
- **Then** the **Credit balance** section shows a table with exactly three rows: `Posted`, `Available`, and `Held / pending`, each displaying a credit amount with tabular-nums styling
- Priority: P0 · Type: happy · Automated: yes (e2e: `credit summary table shows Posted Available Held rows`)

---

### BILL-04 — Credit table row math: Held = Posted − Available

- **Given** a user with a non-zero balance where `posted != available`
- **When** the balance loads in the credit summary table
- **Then** the `Held / pending` row value equals `posted − available` (zero or positive; never negative under normal ledger conditions)
- Priority: P1 · Type: edge · Automated: manual (requires seeded non-zero hold state on the backend)

---

### BILL-05 — Packages grid empty state

- **Given** the billing module is configured with no credit packages
- **When** the user navigates to `/billing`
- **Then** the **Buy credits** section displays `"No packages available"` with the description `"Credit packages will appear here once configured."` and no package cards
- Priority: P0 · Type: edge · Automated: yes (e2e: `packages grid shows empty state when no packages`)

---

### BILL-06 — Packages grid renders package cards

- **Given** the billing module has ≥1 credit package configured
- **When** the user navigates to `/billing`
- **Then** the **Buy credits** section renders one card per package, each with a name, credit amount, price with currency, and a **Buy now** button
- Priority: P0 · Type: happy · Automated: manual (requires seeded packages; the default dev/test environment may have none)

---

### BILL-07 — Subscription card empty state (no active subscription)

- **Given** an authenticated user who has never subscribed (`GET /v1/billing/subscriptions/me` returns 404)
- **When** the user navigates to `/billing`
- **Then** the **Subscription** card shows `"No active subscription."` with a `"View plans"` link
- Priority: P0 · Type: happy · Automated: yes (e2e: `subscription card shows no subscription empty state`)

---

### BILL-08 — Subscription card renders plan details

- **Given** a user with an active subscription (status `Active` or `Trialing`)
- **When** the user navigates to `/billing`
- **Then** the Subscription card shows the plan key, a badge with the status (e.g. `"Active"`), and the renewal date; a **Cancel plan** button is visible if `cancelAtPeriodEnd` is false
- Priority: P1 · Type: happy · Automated: manual (requires active Stripe subscription in dev environment)

---

### BILL-09 — Subscription card — cancel-at-period-end state

- **Given** a user with `cancelAtPeriodEnd = true`
- **When** the Subscription card renders
- **Then** the card shows `"Cancels at period end"` warning text, the date line reads `"Cancels …"` instead of `"Renews …"`, and the **Cancel plan** button is hidden
- Priority: P1 · Type: edge · Automated: manual

---

### BILL-10 — Promo code — empty-submit client validation

- **Given** the user is on `/billing` with the promo code input empty
- **When** the user clicks **Apply** without entering a code
- **Then** a client-side error `"Enter a promo code."` is shown inline; no API request is made
- Priority: P1 · Type: error · Automated: yes (e2e: `promo code empty submit shows client validation error`)

---

### BILL-11 — Promo code — code too long client validation

- **Given** the user enters a code longer than 64 characters
- **When** the user clicks **Apply**
- **Then** `"Code is too long."` is shown inline; no API request is made
- Priority: P2 · Type: error · Automated: yes (e2e: `promo code too-long input shows client validation error`)

---

### BILL-12 — Promo code — invalid code shows error feedback

- **Given** the user enters a code that does not match any Stripe promotion (API returns 404 or error)
- **When** the user clicks **Apply**
- **Then** the error banner `"Invalid or expired promo code."` is visible (with XCircleIcon styling); no success panel is shown
- Priority: P0 · Type: error · Automated: yes (e2e: `promo code invalid code shows error feedback`)

---

### BILL-13 — Promo code — valid code shows discount details

- **Given** the user enters a known valid promo code
- **When** the user clicks **Apply** and the API returns `{ percentOff: 20, amountOff: null, currency: null }`
- **Then** a success panel is visible containing the code and `"20% off"` text; no error banner is shown
- Priority: P0 · Type: happy · Automated: manual (requires a live Stripe promo code in the test environment)

---

### BILL-14 — Promo code — clear resets the form

- **Given** the user has submitted a (valid or invalid) promo code and the result is displayed
- **When** the user clicks **Clear**
- **Then** the input is empty, the result panel (success or error) disappears, and the **Clear** button itself is gone
- Priority: P1 · Type: happy · Automated: manual (depends on a submittable code; error path could be automated but pairs with BILL-12)

---

### BILL-15 — Checkout redirect is Stripe-only (security guard)

- **Given** the backend returns a `checkoutUrl` of `https://checkout.stripe.com/pay/…`
- **When** the user clicks **Buy now** on a package card
- **Then** `window.location.href` is set to that URL; if the URL has a non-`stripe.com` host or is not `https:`, a sonner toast error is shown and no navigation occurs
- Priority: P0 · Type: security · Automated: partial (guard logic unit-testable in hooks.ts; full checkout redirect is manual — requires live Stripe session)

---

### BILL-16 — Unauthenticated redirect to /login

- **Given** a user who is NOT authenticated
- **When** the user navigates to `/billing`
- **Then** the browser is redirected to `/login` (middleware or layout guard); the billing page content is never rendered
- Priority: P0 · Type: security · Automated: yes (e2e: `unauthenticated user is redirected from billing to login`)

---

### BILL-17 — Session token NOT in JS-accessible storage

- **Given** an authenticated user on `/billing`
- **When** the page has fully loaded
- **Then** `localStorage`, `sessionStorage`, and the JS-readable `document.cookie` string do NOT contain the session access/refresh token (only the CSRF token `mp_csrf` may appear in `document.cookie`)
- Priority: P0 · Type: security · Automated: yes (e2e: `session token is not accessible from JavaScript`)

---

### BILL-18 — Billing nav link is visible only when entitled

- **Given** an authenticated user whose tenant has the `billing` module enabled
- **When** the user looks at the sidebar navigation
- **Then** a **Billing** nav link is present and leads to `/billing`
- Priority: P1 · Type: happy · Automated: yes (covered by smoke.spec.ts `authenticated dashboard renders with nav`)

---

### BILL-19 — Credits card renders with tabular-nums class

- **Given** a user with a credit account
- **When** the credits card is rendered
- **Then** the numeric value element has the `tabular-nums` class (ensures monospaced digit alignment)
- Priority: P2 · Type: a11y · Automated: yes (incidental — the element carries `tabular-nums` via `MoneyAmount`; verified in BILL-01 by asserting the rendered text format)

---

### BILL-20 — Keyboard navigation through the billing form

- **Given** an authenticated user on `/billing`
- **When** the user tabs to the promo code input and presses Enter
- **Then** the form submits (or client validation fires); the Apply button is keyboard-reachable and activatable without a mouse
- Priority: P2 · Type: a11y · Automated: manual

---

### BILL-21 — Page heading and section headings are present

- **Given** an authenticated user on `/billing`
- **When** the page renders
- **Then** an `<h1>` with `"Billing"` and `<h2>` headings `"Buy credits"`, `"Promo code"`, and `"Credit balance"` are all visible
- Priority: P2 · Type: a11y · Automated: yes (incidental, asserted in BILL-01 / page navigation)
