# Account Profile — Test Scenario Catalog

**Route:** `/account/profile`
**Backend:** `GET /v1/identity/users/me` → `UserProfileResponse { id, email, displayName, locale }`
**Update endpoint:** **None as of this build.** All fields are read-only. `PATCH /v1/identity/users/me`
is a planned future endpoint; the frontend form already has a placeholder schema (`features/account/schema.ts`)
and a TODO comment in `features/account/api.ts` and `components/profile-form.tsx`.

---

## Scenarios

- **PROF-01** — Page renders with authenticated user's email (read-only)
  - Given the primary user is authenticated
  - When they navigate to `/account/profile`
  - Then the page heading "Profile" is visible, the Email field contains their email address, the field is disabled/read-only, and no form submission is possible
  - Priority: P0 · Type: happy · Automated: yes (e2e: "profile page shows email in read-only field")

- **PROF-02** — Display name field renders (read-only, may be empty)
  - Given the primary user has a display name (set at registration as "E2E Primary")
  - When they view `/account/profile`
  - Then the "Display name" field is visible, contains the user's display name, and is disabled/read-only
  - Priority: P0 · Type: happy · Automated: yes (e2e: "profile page shows display name in read-only field")

- **PROF-03** — Locale field renders (read-only)
  - Given the primary user is authenticated
  - When they view `/account/profile`
  - Then the "Locale" field is visible and contains a locale string (e.g. "en"), and is disabled/read-only
  - Priority: P1 · Type: happy · Automated: yes (e2e: "profile page shows locale in read-only field")

- **PROF-04** — Read-only notice (info alert) is displayed
  - Given the primary user is authenticated
  - When they view `/account/profile`
  - Then an informational alert is visible containing text about profile editing not being available
  - Priority: P1 · Type: happy · Automated: yes (e2e: "profile page shows read-only notice")

- **PROF-05** — Locale toggle in topbar changes the locale field value
  - Given the user is on `/account/profile` and the locale is "en"
  - When they click the language toggle in the topbar and select "Čeština"
  - Then the page reloads, the locale field updates (e.g. to "cs"), and the UI text reflects the new locale
  - Priority: P1 · Type: happy · Automated: yes (e2e: "locale toggle updates profile locale field")

- **PROF-06** — Loading skeleton renders before data arrives
  - Given the user navigates to `/account/profile`
  - When the profile query is in-flight
  - Then skeleton placeholders are rendered in place of the field values (no layout shift / blank form)
  - Priority: P2 · Type: happy · Automated: manual (skeleton is transient; hard to assert without network throttling)

- **PROF-07** — Profile page accessible via user menu
  - Given the authenticated user is anywhere in the app
  - When they open the user menu in the sidebar footer and click "Profile"
  - Then they are navigated to `/account/profile`
  - Priority: P0 · Type: happy · Automated: yes (e2e: "user menu Profile link navigates to profile page")

- **PROF-08** — Unauthenticated access redirects to /login
  - Given a user is NOT authenticated
  - When they visit `/account/profile` directly
  - Then the app redirects them to `/login` (tenant layout `redirect("/login")`)
  - Priority: P0 · Type: security · Automated: yes (e2e: "unauthenticated visit to /account/profile redirects to login")

- **PROF-09** — Session tokens are NOT accessible from JavaScript
  - Given the user is authenticated and on `/account/profile`
  - When JavaScript reads `localStorage`, `sessionStorage`, and `document.cookie`
  - Then no session token is found in JS storage; only the readable `mp_csrf` cookie (or none) may appear; the httpOnly session cookie is absent from `document.cookie`
  - Priority: P0 · Type: security · Automated: yes (e2e: "session token is not exposed to JavaScript on profile page")

- **PROF-10** — Email field value does not change between navigations (data persisted in query cache)
  - Given the user views their profile and then navigates away (e.g. to Dashboard) and back
  - When they return to `/account/profile`
  - Then the email field immediately shows the cached value (no blank flash) and refetches in the background
  - Priority: P2 · Type: happy · Automated: manual (requires observing stale-while-revalidate timing)

- **PROF-11** — Display name is empty / "Not set" placeholder when not provided
  - Given a user registers WITHOUT providing a display name
  - When they view their profile
  - Then the "Display name" field shows a placeholder ("Not set") rather than being blank or crashing
  - Priority: P1 · Type: edge · Automated: yes (e2e: "profile page shows 'Not set' placeholder when display name is absent")

- **PROF-12** — No editable submit button / form action is present
  - Given the current build has no update endpoint
  - When the user views the profile page
  - Then there is NO submit / Save button rendered on the form, confirming the page is purely read-only
  - Priority: P1 · Type: edge · Automated: yes (e2e: "profile page has no submit button (update not yet available)")

- **PROF-13** — Keyboard navigation through profile fields
  - Given the user is on `/account/profile`
  - When they tab through the page
  - Then the focusable elements (info alert, fields, any links) receive focus in a logical order and are reachable by keyboard; disabled inputs are skipped per HTML spec
  - Priority: P1 · Type: a11y · Automated: manual (requires visual/AT inspection)

- **PROF-14** — Field labels are associated with inputs (a11y)
  - Given the profile form is rendered
  - When a screen reader inspects the Email, Display name, and Locale fields
  - Then each input has an accessible label via `for`/`id` pairing (`profile-email`, `profile-display-name`, `profile-locale`) and `aria-label`
  - Priority: P1 · Type: a11y · Automated: partial (aria-label presence assertable in spec; full SR audit is manual)

- **PROF-15** — [GAP: No update flow] Edit display name and persist
  - Given a user wants to change their display name
  - When they type a new value and submit
  - Then the name is saved and a success toast is shown
  - Priority: P0 (once backend PATCH endpoint exists) · Type: happy · Automated: manual until `PATCH /v1/identity/users/me` is implemented
  - **NOTE:** Backend has no update endpoint. Frontend form is intentionally read-only. Unblock by adding the Identity `UpdateProfile` slice and wiring `useMutation` in `features/account/api.ts`.

- **PROF-16** — [GAP: No update flow] Validation: empty display name rejected
  - Given the update form exists
  - When the user clears the display name and submits
  - Then a validation error is shown (if the backend enforces non-empty) or the field saves as null
  - Priority: P1 (future) · Type: error · Automated: manual until backend slice exists

- **PROF-17** — [GAP: No update flow] Network error on save
  - Given the update form exists and the API is unreachable
  - When the user submits
  - Then a toast error is displayed and the form remains editable
  - Priority: P1 (future) · Type: error · Automated: manual until backend slice exists

---

## Known Gaps / Assumptions

1. **No update endpoint exists** — PROF-15, PROF-16, PROF-17 are catalogued but cannot be automated until `PATCH /v1/identity/users/me` (and corresponding Identity `UpdateProfileHandler`) is built. The frontend has placeholder schema in `features/account/schema.ts` and a TODO comment in the component.
2. The profile query `staleTime` is 60 s — scenarios testing data freshness (PROF-10) require network interception to assert reliably and are marked manual.
3. Skeleton state (PROF-06) is transient under normal dev-server conditions and is marked manual.
4. Accessibility audit (PROF-13) requires assistive technology and is manual; PROF-14 is partially covered by the spec.
