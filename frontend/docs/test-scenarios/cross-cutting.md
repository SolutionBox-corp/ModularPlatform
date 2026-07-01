# Cross-Cutting Test Scenarios

Covers: **Realtime indicator**, **error surfaces**, **i18n / locale toggle**, **dark-mode persistence**,
**entitlement-gated nav**, and **accessibility (axe)**. These concerns cut across every page of the
authenticated app shell and the auth flows.

---

## Realtime

- **RT-01** — Realtime indicator reaches "Live" on the dashboard
  - Given: authenticated user on the dashboard
  - When: the SSE stream connects successfully (normal conditions)
  - Then: the indicator shows the text "Live" (or the status dot is `bg-success`) within the default
    timeout; the `aria-label` on the indicator span includes "Live"
  - Priority: P0 · Type: happy · Automated: yes (e2e: "realtime indicator shows Live on dashboard")

- **RT-02** — Realtime indicator shows "Connecting…" before the stream settles
  - Given: the SSE endpoint is unreachable (network blocked)
  - When: the provider attempts its first connection
  - Then: the indicator label contains "Connecting…" before falling back
  - Priority: P1 · Type: edge · Automated: manual (requires network interception that would interfere
    with the serial suite; verify via DevTools throttling)

- **RT-03** — Realtime indicator shows "Offline" when the tab becomes hidden and is then restored
  - Given: authenticated user, stream is open
  - When: the page visibility changes to "hidden" (provider aborts) then back to "visible"
  - Then: indicator transitions Offline → Connecting… → Live
  - Priority: P1 · Type: edge · Automated: manual (requires `document.visibilityState` manipulation in
    isolation; tab-hide tests are flaky in headless mode)

- **RT-04** — A 401 from the SSE endpoint redirects to /login?reason=expired
  - Given: the BFF SSE endpoint returns 401 (token cannot be refreshed)
  - When: the provider receives the error response
  - Then: the browser navigates to `/login?reason=expired`
  - Priority: P0 · Type: security · Automated: manual (requires mocking the BFF /api/bff/realtime/stream
    to return 401; not feasible without a test intercept fixture)

---

## Errors

- **ER-01** — An RFC 9457 API error surfaces as a sonner toast, not a crash
  - Given: authenticated user triggers a backend error (e.g. submitting an invalid form)
  - When: the request returns a known error code
  - Then: a sonner toast appears in the top-right corner with the mapped message; no unhandled JS error;
    the page remains interactive
  - Priority: P0 · Type: error · Automated: partial (covered implicitly by form-level error scenarios in
    billing/files/auth specs; the toast infrastructure is verified by those; isolated trigger is manual)

- **ER-02** — Unknown error codes fall back to the generic message
  - Given: the backend returns an unrecognised error code
  - When: `toDisplayMessage` is called
  - Then: "Something went wrong. Please try again." is shown (en locale)
  - Priority: P1 · Type: edge · Automated: manual (pure unit test candidate, not E2E)

- **ER-03** — Rate-limit (429) response shows the rate-limit message with retry-after hint
  - Given: the backend returns 429 with a `Retry-After` header
  - When: the toast is rendered
  - Then: the message contains "Too many requests" and the retry-after value in seconds
  - Priority: P1 · Type: error · Automated: manual (requires inducing rate-limit; destructive to shared
    rate-limit bucket)

- **ER-04** — Error boundary renders ProblemDetails + Try Again button (not a blank crash)
  - Given: a page-level component throws during render
  - When: the Next error.tsx boundary catches it
  - Then: an alert with role="alert" appears with a safe message and a "Try again" button
  - Priority: P0 · Type: error · Automated: manual (triggering a boundary requires forcing a render error;
    the component itself is verified by snapshot/unit tests)

---

## 404 / Not Found

- **NF-01** — Navigating to a non-existent route shows the 404 page
  - Given: user (authenticated or anonymous) navigates to `/this-route-does-not-exist-xyz`
  - When: Next.js renders the not-found.tsx
  - Then: a heading "Page not found" is visible; the large "404" text is present; a "Go to dashboard"
    link is present
  - Priority: P0 · Type: happy · Automated: yes (e2e: "unknown route shows 404 not-found page")

- **NF-02** — "Go to dashboard" link navigates to "/"
  - Given: the 404 page is displayed
  - When: the user clicks "Go to dashboard"
  - Then: the browser navigates to "/" (authenticated users land on the dashboard; anonymous users are
    redirected to /login by the layout)
  - Priority: P1 · Type: happy · Automated: yes (e2e: "404 Go to dashboard link navigates home")

---

## i18n / Locale Toggle

- **I18-01** — Locale toggle switches the app to Czech and persists via NEXT_LOCALE cookie
  - Given: authenticated user on the dashboard with locale "en" (default)
  - When: the user opens the locale dropdown and selects "Čeština"
  - Then: the page reloads; the `NEXT_LOCALE` cookie is set to `cs`; the nav item previously labelled
    "Dashboard" now shows "Přehled"; the `<html lang>` attribute is "cs"
  - Priority: P0 · Type: happy · Automated: yes (e2e: "locale toggle switches en to cs and persists")

- **I18-02** — Switching back to English restores English labels
  - Given: locale is currently "cs" (set from previous switch)
  - When: the user opens the locale dropdown and selects "English"
  - Then: the page reloads; `NEXT_LOCALE` cookie is set to "en"; nav shows "Dashboard" again
  - Priority: P1 · Type: happy · Automated: yes (e2e: "locale toggle switches cs back to en")

- **I18-03** — Czech locale does NOT translate auth form strings on the login page
  - Given: NEXT_LOCALE cookie is set to "cs"
  - When: the user navigates to /login
  - Then: the heading still reads "Sign in" and the submit button still says "Sign in" — auth forms
    use inline EN strings and are not wired to next-intl translations; only nav labels (e.g.
    "Dashboard" → "Přehled") switch locale
  - Priority: P1 · Type: happy · Automated: yes (e2e: "Czech locale does not translate the login form copy")

- **I18-04** — Locale toggle is keyboard accessible
  - Given: authenticated user on the dashboard
  - When: the user tabs to the locale toggle button and presses Enter/Space
  - Then: the dropdown opens and locale items are keyboard-navigable
  - Priority: P1 · Type: a11y · Automated: manual (keyboard-only navigation requires careful focus
    management that is hard to assert reliably in headless mode)

---

## Dark Mode / Theme

- **TH-01** — Dark mode toggle switches `<html>` class to "dark" and persists across reload
  - Given: authenticated user on the dashboard with light mode active
  - When: the user clicks the theme toggle button
  - Then: the `<html>` element gains the class "dark"; after a full page reload the class is still
    "dark" (persisted via `localStorage["theme"]`)
  - Priority: P0 · Type: happy · Automated: yes (e2e: "dark mode toggle persists across reload")

- **TH-02** — Switching back to light mode removes the "dark" class
  - Given: theme is "dark"
  - When: the user clicks the theme toggle again
  - Then: the `<html>` class "dark" is removed; persists after reload
  - Priority: P1 · Type: happy · Automated: yes (e2e: part of "dark mode toggle persists across reload"
    — the same test re-toggles to verify reversibility)

- **TH-03** — System theme is respected as the default (no stored preference)
  - Given: no `localStorage["theme"]` is set; system prefers dark
  - When: the dashboard loads
  - Then: `<html>` class is "dark" (system preference reflected)
  - Priority: P2 · Type: edge · Automated: manual (requires emulating prefers-color-scheme in the
    browser; possible via Playwright but adds fragility and is low risk)

- **TH-04** — Theme toggle button has an accessible aria-label
  - Given: theme is light (mounted)
  - When: the toggle button is inspected
  - Then: `aria-label` is "Switch to dark mode" (light) or "Switch to light mode" (dark)
  - Priority: P1 · Type: a11y · Automated: yes (e2e: verified as part of "dark mode toggle persists
    across reload" via accessible name assertion)

---

## Entitlement-Gated Navigation

- **EG-01** — Entitled primary user sees Billing, Files, Notifications in the nav
  - Given: authenticated primary user (self-registered → default 5 entitlements including billing,
    files, notifications)
  - When: the dashboard loads
  - Then: nav links for "Dashboard", "Billing", "Files", "Notifications", "Profile", "Privacy" are all
    visible in the sidebar
  - Priority: P0 · Type: happy · Automated: yes (e2e: "entitled user sees all gated nav items")

- **EG-02** — Dashboard, Profile, and Privacy are always present regardless of entitlements
  - Given: any authenticated user
  - When: the dashboard loads
  - Then: "Dashboard" (href="/"), "Profile" (href="/account/profile"), and "Privacy"
    (href="/account/privacy") are always in the nav (they have no moduleKey in NAV_ITEMS)
  - Priority: P0 · Type: happy · Automated: yes (e2e: "always-present nav items visible for primary
    user")

- **EG-03** — A module's nav item is hidden when its entitlement is disabled
  - Given: a user whose tenant does NOT have (e.g.) the "billing" module entitled
  - When: the dashboard loads
  - Then: the "Billing" nav link is absent from the sidebar
  - Priority: P0 · Type: edge · Automated: partial (cannot disable entitlements via self-serve UI;
    would require a backend admin API call or direct DB change; scenario verified by reading the
    AppNav filtering logic + marked for manual/API-level verification)

- **EG-04** — Navigating directly to a module's route when not entitled shows an appropriate response
  - Given: a user without the "billing" entitlement
  - When: navigating to /billing
  - Then: the route shows a 403/not-found response or redirects appropriately (depends on server-side
    guard implementation — verify when route guards are confirmed server-side)
  - Priority: P1 · Type: security · Automated: manual (same constraint as EG-03)

- **EG-05** — Permission-gated admin nav items are absent for non-admin users
  - Given: the primary self-registered user (no `identity.manage_roles` or `audit.read` permission)
  - When: the platform admin nav would be evaluated
  - Then: platform admin nav items ("Tenants", "Users") are not visible in the main sidebar
  - Priority: P1 · Type: security · Automated: yes (e2e: implicitly covered — primary user does not
    see platform items; the sidebar nav is verified in EG-01)

---

## Accessibility (axe)

- **A11Y-01** — Dashboard has no axe serious/critical violations
  - Given: authenticated user on the dashboard (app shell + cards rendered)
  - When: `AxeBuilder.analyze()` is run
  - Then: no violations with impact "critical" or "serious"
  - Priority: P0 · Type: a11y · Automated: yes (e2e: "dashboard has no critical axe violations")

- **A11Y-02** — Login page has no axe serious/critical violations
  - Given: unauthenticated user on /login
  - When: `AxeBuilder.analyze()` is run
  - Then: no violations with impact "critical" or "serious"
  - Priority: P0 · Type: a11y · Automated: yes (e2e: "login page has no critical axe violations")

- **A11Y-03** — 404 page has no axe serious/critical violations
  - Given: user navigates to an unknown route
  - When: `AxeBuilder.analyze()` is run
  - Then: no violations with impact "critical" or "serious"
  - Priority: P1 · Type: a11y · Automated: yes (e2e: "404 page has no critical axe violations")

- **A11Y-04** — Sidebar nav links have correct aria-current="page" on the active route
  - Given: authenticated user on the Dashboard route "/"
  - When: the sidebar is rendered
  - Then: the "Dashboard" SidebarMenuButton link has `aria-current="page"` and no other nav link does
  - Priority: P1 · Type: a11y · Automated: yes (e2e: "active dashboard nav link exposes aria-current=page")

- **A11Y-05** — RealtimeIndicator uses aria-live="polite" for status announcements
  - Given: the realtime indicator is rendered
  - When: the status changes (connecting → open)
  - Then: the status text is inside an `aria-live="polite"` region so screen readers announce it
  - Priority: P1 · Type: a11y · Automated: yes (e2e: "status is announced through a polite live region")
