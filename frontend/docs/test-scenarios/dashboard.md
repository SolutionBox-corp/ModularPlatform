# Dashboard ("/") — Test Scenarios

The dashboard is the authenticated entry point. It shows a welcome heading personalised with
the user's display name, a Credits card (credit balance), a Subscription card (current plan or
"No active subscription" empty state), a Recent Notifications card (showing the welcome
notification for a brand-new user), and a Quick actions list linking to /files,
/billing/packages, and /account/privacy. All card data is server-prefetched and handed to
client components via TanStack Query dehydrated state, so the first paint is content-rich with
no loading skeleton flash for the primary user. A 404 from `GET /v1/billing/subscriptions/me`
is treated as a valid "no subscription" empty state and MUST NOT produce an error toast.

---

## Scenarios

- **DASH-01** — Welcome heading with display name
  - Given: an authenticated user with `displayName = "E2E Primary"` visits `/`
  - When: the page finishes loading
  - Then: an `<h1>` with text "Welcome back, E2E Primary" is visible
  - Priority: P0 · Type: happy · Automated: yes (e2e: "shows welcome heading with display name")

- **DASH-02** — Welcome heading without display name (email fallback)
  - Given: an authenticated user whose account has no display name set visits `/`
  - When: the page finishes loading
  - Then: the heading reads "Welcome back" (no trailing comma or name fragment)
  - Priority: P1 · Type: edge · Automated: manual (requires a user registered without a display name; registerFreshUser always sets one, so this would need a dedicated registration with displayName omitted — out-of-scope for the shared primary session spec)

- **DASH-03** — Credits card shows zero balance for a new user
  - Given: a brand-new authenticated user (just registered) visits `/`
  - When: the Credits card renders
  - Then: the credit balance is displayed as "0 cr." (or similar "0 cr." formatted string), the card title "Credits" is visible, and no error toast appears
  - Priority: P0 · Type: happy · Automated: yes (e2e: "Credits card shows 0 cr. for new user")

- **DASH-04** — Credits card "Manage billing" link navigates to /billing
  - Given: the authenticated user is on the dashboard
  - When: the user clicks "Manage billing" in the Credits card
  - Then: the browser navigates to `/billing`
  - Priority: P1 · Type: happy · Automated: yes (e2e: "Manage billing link goes to /billing")

- **DASH-05** — Subscription card shows "No active subscription" for a new user
  - Given: a brand-new user (no Stripe subscription) visits `/`
  - When: the Subscription card renders
  - Then: the text "No active subscription." is visible, no error toast is shown, and the "View plans" link is present
  - Priority: P0 · Type: happy · Automated: yes (e2e: "Subscription card shows empty state — no error toast")

- **DASH-06** — No error toast fires for empty subscription (404 treated as valid empty state)
  - Given: a new user with no subscription visits `/`
  - When: the page fully loads (SSE connects, all queries resolve)
  - Then: no sonner toast element with role="alert" or a destructive/error variant is visible
  - Priority: P0 · Type: error · Automated: yes (e2e: "no error toast for missing subscription")

- **DASH-07** — Subscription card shows plan details when active
  - Given: a user with an active subscription visits `/`
  - When: the Subscription card renders
  - Then: the plan key (e.g. "starter") and a status badge (e.g. "Active") are visible
  - Priority: P1 · Type: happy · Automated: manual (requires an existing Stripe-activated subscription; cannot be provisioned cheaply in E2E without a real Stripe checkout or backend seeding)

- **DASH-08** — Recent Notifications card shows the welcome notification with a "New" badge
  - Given: a brand-new user has just registered (the backend sends a welcome notification via the Notifications module)
  - When: the Recent Notifications card on the dashboard renders
  - Then: at least one notification item is listed, a "New" badge is visible on an unread item, and the notification title is non-empty
  - Priority: P0 · Type: happy · Automated: yes (e2e: "welcome notification appears with New badge")

- **DASH-09** — Recent Notifications card shows empty state when there are no notifications
  - Given: a user whose notification feed is empty visits `/`
  - When: the Recent Notifications card renders
  - Then: the text "No notifications" and "You're all caught up." are visible (the EmptyState component); no error is shown
  - Priority: P1 · Type: edge · Automated: manual (requires a user with a cleared feed; cannot guarantee in E2E without backend seeding)

- **DASH-10** — Quick action: "Upload a file" links to /files
  - Given: the authenticated user is on the dashboard
  - When: they inspect or click "Upload a file" in the Quick actions list
  - Then: the link `href` is `/files` and navigating it lands on the Files page
  - Priority: P1 · Type: happy · Automated: yes (e2e: "Quick actions — Upload a file links to /files")

- **DASH-11** — Quick action: "Top up credits" links to /billing/packages
  - Given: the authenticated user is on the dashboard
  - When: they inspect or click "Top up credits" in the Quick actions list
  - Then: the link `href` is `/billing/packages` and navigating it lands on the Billing packages section
  - Priority: P1 · Type: happy · Automated: yes (e2e: "Quick actions — Top up credits links to /billing/packages")

- **DASH-12** — Quick action: "View audit trail" links to /account/privacy
  - Given: the authenticated user is on the dashboard
  - When: they inspect or click "View audit trail" in the Quick actions list
  - Then: the link `href` is `/account/privacy`
  - Priority: P1 · Type: happy · Automated: yes (e2e: "Quick actions — View audit trail links to /account/privacy")

- **DASH-13** — All three quick action links are present simultaneously
  - Given: the authenticated user is on the dashboard
  - When: the page loads
  - Then: all three quick actions ("Upload a file", "Top up credits", "View audit trail") are visible and each has the correct href
  - Priority: P0 · Type: happy · Automated: yes (e2e: "all three Quick actions are present")

- **DASH-14** — Unauthenticated user is redirected to /login
  - Given: an unauthenticated browser (no session cookie) navigates to `/`
  - When: the server-side session check runs
  - Then: the browser lands on `/login` (Next.js `redirect()`)
  - Priority: P0 · Type: security · Automated: manual (covered conceptually by auth.setup guard; a dedicated logged-out spec could assert `toHaveURL("/login")`)

- **DASH-15** — All empty states render without JS errors for a brand-new user
  - Given: a brand-new user (zero credits, no subscription, only the welcome notification) visits `/`
  - When: the full page loads including client-side hydration
  - Then: the Credits card shows "0 cr.", the Subscription card shows the empty state, the Notifications card shows the welcome notification — and the browser console has no unhandled errors / React hydration warnings
  - Priority: P1 · Type: edge · Automated: partial (e2e assertions cover visible content; console error monitoring is manual or requires a dedicated console listener)

- **DASH-16** — "View all" link in Recent Notifications navigates to /notifications
  - Given: the authenticated user is on the dashboard
  - When: they click "View all" in the Notifications card header
  - Then: the browser navigates to `/notifications`
  - Priority: P1 · Type: happy · Automated: yes (e2e: "View all notifications link goes to /notifications")

- **DASH-17** — Mark-as-read button on a notification removes the "New" badge
  - Given: the welcome notification is visible on the dashboard with a "New" badge
  - When: the user clicks the checkmark ("Mark as read") button on that notification
  - Then: the "New" badge disappears from that item (query invalidated and re-rendered)
  - Priority: P1 · Type: happy · Automated: manual (stateful mutation that affects the shared primary user's feed; better covered in the Notifications spec area)

- **DASH-18** — Page title is "Dashboard — ModularPlatform"
  - Given: the authenticated user navigates to `/`
  - When: the page fully loads
  - Then: `document.title` equals "Dashboard — ModularPlatform"
  - Priority: P2 · Type: a11y · Automated: yes (e2e: "page title is set correctly")
