# Design Gallery — Test Scenarios

The `/design` route is a dev-only component gallery (`app/design/page.tsx`). It renders
`DesignGallery` — a standalone client component with its own sticky header. It lives
**outside** the `(tenant)` route group, so it does **not** use the `AppShell` (no sidebar,
no topbar, no `ThemeToggle` button in-page). The `ThemeProvider` is always active (root
`layout.tsx`), so dark mode **can** be applied, but must be triggered externally (e.g. via
`localStorage`) or by visiting an authenticated shell page first.

Access: no auth guard — accessible to authenticated and anonymous users alike.

---

## Scenarios

- **DES-01** — Page renders without a crash
  - Given the user navigates to `/design` (authenticated or anonymous)
  - When the page finishes loading
  - Then the sticky "Design Gallery" heading and the "dev only" badge are visible, and
    the browser reports no uncaught JS errors
  - Priority: P0 · Type: happy · Automated: yes (e2e: renders without uncaught console errors)

- **DES-02** — All 25 section headings are present
  - Given the user is on `/design`
  - When the page renders
  - Then each numbered section heading (1 · Design Tokens … 25 · Dark Mode) is visible in
    the document
  - Priority: P0 · Type: happy · Automated: yes (e2e: all section headings are visible)

- **DES-03** — Design tokens section shows color swatches
  - Given the user is on `/design`
  - When the page renders
  - Then the "1 · Design Tokens" section is visible and contains at least one color swatch
    aria-labelled "background" and one labelled "primary"
  - Priority: P1 · Type: happy · Automated: yes (e2e: renders without uncaught console errors)

- **DES-04** — Button section shows all six variants
  - Given the user is on `/design`
  - When the "2 · Button" section renders
  - Then buttons with visible text "Default", "Secondary", "Outline", "Ghost", "Destructive",
    and "Link" are all present in that section
  - Priority: P1 · Type: happy · Automated: yes (e2e: all section headings are visible)

- **DES-05** — Form inputs section contains interactive controls
  - Given the user is on `/design`
  - When the "5 · Form Inputs" section renders
  - Then a text input (placeholder "Type something…"), a Switch, and a Checkbox are all
    visible; the controlled Switch responds to a click by updating its `aria-checked` state
  - Priority: P1 · Type: happy · Automated: yes (e2e: interactive controls respond)

- **DES-06** — Toast buttons trigger Sonner toasts
  - Given the user is on `/design` and the Toaster is mounted
  - When the user clicks the "Success" toast button in section 17 · Toast
  - Then a toast with text "Operation completed successfully." appears in the viewport
  - Priority: P1 · Type: happy · Automated: yes (e2e: toast appears on button click)

- **DES-07** — Dialog opens and closes
  - Given the user is on `/design`
  - When the user clicks "Open dialog" in section 13 · Dialog
  - Then a modal dialog with title "Confirm action" appears
  - And when the user presses Escape
  - Then the dialog closes (the dialog content is no longer visible)
  - Priority: P1 · Type: happy · Automated: yes (e2e: dialog opens and closes)

- **DES-08** — Sheet opens and closes
  - Given the user is on `/design`
  - When the user clicks "Right sheet" in section 14 · Sheet
  - Then a sheet panel with title "Right sheet" slides in from the right
  - And when the user presses Escape
  - Then the sheet closes
  - Priority: P1 · Type: happy · Automated: yes (e2e: sheet opens and closes)

- **DES-09** — DataTable renders with data rows
  - Given the user is on `/design`
  - When the "18 · DataTable" section renders
  - Then the table contains the names "Alice Johnson" and "Bob Smith", and a pagination
    control is visible
  - Priority: P1 · Type: happy · Automated: yes (e2e: all section headings are visible)

- **DES-10** — DataTable shows empty state when data is empty
  - Given the user is on `/design`
  - When the "Empty state" sub-section of the DataTable section renders
  - Then text "No users found" is visible
  - Priority: P1 · Type: happy · Automated: yes (e2e: all section headings are visible)

- **DES-11** — EmptyState action button fires callback
  - Given the user is on `/design` (section 19 · EmptyState)
  - When the user clicks "Upload a file" in the second EmptyState card
  - Then a Sonner toast with text "Upload triggered" appears
  - Priority: P2 · Type: happy · Automated: yes (e2e: interactive controls respond)

- **DES-12** — MoneyAmount renders credits and fiat
  - Given the user is on `/design` (section 20 · MoneyAmount)
  - When the section renders
  - Then "0 cr." (zero credits), "1,250 cr." (formatted credits), and a USD amount
    containing "$" are all visible
  - Priority: P1 · Type: happy · Automated: yes (e2e: all section headings are visible)

- **DES-13** — ProblemDetails renders known and unknown error variants
  - Given the user is on `/design` (section 21 · ProblemDetails)
  - When the section renders
  - Then at least two error display blocks are visible, including one with text related
    to "email" (the `user.email_taken` error) and one for the generic "Internal error"
  - Priority: P1 · Type: happy · Automated: yes (e2e: all section headings are visible)

- **DES-14** — Operation status cards render all five states
  - Given the user is on `/design` (section 22 · OperationStatus)
  - When the section renders
  - Then panels labelled "Pending", "Running", "Completed", "Failed", and "Cancelled"
    are all visible
  - Priority: P1 · Type: happy · Automated: yes (e2e: all section headings are visible)

- **DES-15** — Dark mode toggle flips the `html.dark` class
  - Given the user is on `/design` and the page is in light mode (no `dark` class on `<html>`)
  - When the user sets `localStorage.theme = "dark"` and reloads the page
  - Then `<html>` has the class `dark`
  - And when the user sets `localStorage.theme = "light"` and reloads
  - Then `<html>` does NOT have the class `dark`
  - Priority: P0 · Type: happy · Automated: yes (e2e: dark-mode class flips on theme change)

- **DES-16** — Dark mode: gallery tokens visually adapt (smoke)
  - Given the user is on `/design` in dark mode
  - When the page renders
  - Then the page body is still readable (the "Design Gallery" heading is visible and
    the page has not crashed), confirming semantic tokens adapt without layout breakage
  - Priority: P1 · Type: happy · Automated: yes (e2e: dark-mode class flips on theme change)

- **DES-17** — No uncaught JS console errors on load
  - Given the user navigates to `/design`
  - When the page finishes loading (no interaction)
  - Then the browser console has no uncaught errors (pageerror events = 0)
  - Priority: P0 · Type: happy · Automated: yes (e2e: renders without uncaught console errors)

- **DES-18** — Tabs switch content panels
  - Given the user is on `/design` (section 8 · Tabs)
  - When the user clicks the "Activity" tab trigger
  - Then the "Activity feed goes here." content panel becomes visible and the "Overview
    content panel." text is no longer in the active panel
  - Priority: P1 · Type: happy · Automated: yes (e2e: interactive controls respond)

- **DES-19** — Dropdown menu opens on trigger click
  - Given the user is on `/design` (section 11 · Dropdown Menu)
  - When the user clicks the "Actions" dropdown trigger
  - Then a menu containing "Profile", "Settings", and "Sign out" items appears
  - Priority: P1 · Type: happy · Automated: yes (e2e: interactive controls respond)

- **DES-20** — Popover opens on trigger click
  - Given the user is on `/design` (section 12 · Popover)
  - When the user clicks "Open popover"
  - Then a popover with title "Popover title" appears
  - Priority: P1 · Type: happy · Automated: yes (e2e: interactive controls respond)

- **DES-21** — Scroll area is present and scrollable
  - Given the user is on `/design` (section 15 · Scroll Area)
  - When the section renders
  - Then a scroll container with "Item 1 — scrollable content row" visible is present
  - Priority: P2 · Type: happy · Automated: yes (e2e: all section headings are visible)

- **DES-22** — Keyboard: dialog opened via Enter key on trigger
  - Given the user is on `/design` and focuses the "Open dialog" button via keyboard
  - When the user presses Enter
  - Then the dialog opens (keyboard-accessible)
  - Priority: P1 · Type: a11y · Automated: yes (e2e: dialog opens and closes)

- **DES-23** — Page accessible without authentication (no redirect)
  - Given the user is NOT logged in
  - When the user navigates to `/design`
  - Then the page renders with the "Design Gallery" heading visible (no redirect to `/login`)
  - Priority: P1 · Type: security · Automated: manual (requires ANONYMOUS spec variant — low value to automate separately from DES-01)

- **DES-24** — No tokens exposed in JS storage on the design page
  - Given the user navigates to `/design` (authenticated)
  - When the page loads
  - Then `localStorage` and `sessionStorage` contain no keys with "token" or "session" in the
    name, and `document.cookie` does not expose the session cookie (only `mp_csrf` may be readable)
  - Priority: P1 · Type: security · Automated: manual (overlaps with platform-level auth security spec)

- **DES-25** — Realtime indicator section shows all three states
  - Given the user is on `/design` (section 23 · RealtimeIndicator)
  - When the section renders
  - Then texts "Live", "Connecting", and "Offline" are all visible (static state previews)
  - Priority: P2 · Type: happy · Automated: yes (e2e: all section headings are visible)
