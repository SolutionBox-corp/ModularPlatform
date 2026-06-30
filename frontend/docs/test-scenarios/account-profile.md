# Account Profile — Test Scenario Catalog

**Route:** `/account/profile`
**Backend:** `GET /v1/identity/users/me` -> `UserProfileResponse { id, email, displayName, locale, emailConfirmed }`
**Update endpoint:** `PATCH /v1/identity/users/me` -> updates `displayName` and `locale`.

Email is intentionally read-only. Changing e-mail needs a separate verified e-mail-change flow.

---

## Scenarios

- **PROF-01** — Page renders with authenticated user's email (read-only)
  - Given the primary user is authenticated
  - When they navigate to `/account/profile`
  - Then the page heading "Profile" is visible, the Email field contains their email address, and the Email field is disabled/read-only
  - Priority: P0 · Type: happy · Automated: yes (e2e: "profile page shows email in read-only field")

- **PROF-02** — Display name field renders and is editable
  - Given the primary user has a display name
  - When they view `/account/profile`
  - Then the "Display name" field is visible, contains the current display name, and is enabled
  - Priority: P0 · Type: happy · Automated: yes (e2e: "profile page shows editable display name field")

- **PROF-03** — Locale field renders as an editable select
  - Given the primary user is authenticated
  - When they view `/account/profile`
  - Then the "Language" select is visible, enabled, and shows a supported locale code (`en` or `cs`)
  - Priority: P1 · Type: happy · Automated: yes (e2e: "profile page shows editable locale select")

- **PROF-04** — Email read-only hint is displayed
  - Given the primary user is authenticated
  - When they view `/account/profile`
  - Then text explains that changing e-mail requires a verified e-mail-change flow
  - Priority: P1 · Type: happy · Automated: yes (e2e: "profile page explains email is read-only")

- **PROF-05** — Locale toggle in topbar changes the UI language
  - Given the user is on `/account/profile` and the UI is English
  - When they click the language toggle and select "Čeština"
  - Then the shell labels switch to Czech
  - Priority: P1 · Type: happy · Automated: yes (e2e: "locale toggle switches the UI language")

- **PROF-06** — Loading skeleton renders before data arrives
  - Given the user navigates to `/account/profile`
  - When the profile query is in-flight
  - Then skeleton placeholders are rendered in place of field values
  - Priority: P2 · Type: happy · Automated: manual (transient under normal dev-server conditions)

- **PROF-07** — Profile page accessible via sidebar navigation
  - Given the authenticated user is anywhere in the app
  - When they click the "Profile" sidebar link
  - Then they are navigated to `/account/profile`
  - Priority: P0 · Type: happy · Automated: yes (e2e: "sidebar Profile link navigates to profile page")

- **PROF-08** — Unauthenticated access redirects to `/login`
  - Given a user is not authenticated
  - When they visit `/account/profile` directly
  - Then the app redirects them to `/login`
  - Priority: P0 · Type: security · Automated: yes (e2e: "unauthenticated visit to /account/profile redirects to login")

- **PROF-09** — Session tokens are not accessible from JavaScript
  - Given the user is authenticated and on `/account/profile`
  - When JavaScript reads local storage, session storage, and readable cookies
  - Then no JWT/session token is visible to JavaScript
  - Priority: P0 · Type: security · Automated: yes (e2e: "session token is not exposed to JavaScript on profile page")

- **PROF-10** — Cached profile value is reused between navigations
  - Given the user views their profile and then navigates away and back
  - When they return within the query stale window
  - Then the form shows the cached value immediately and refetches in the background
  - Priority: P2 · Type: happy · Automated: manual (requires network observation)

- **PROF-11** — Display name placeholder exists for empty names
  - Given the profile form renders
  - When the display name value is empty
  - Then the input exposes the "Not set" placeholder
  - Priority: P1 · Type: edge · Automated: yes (e2e: placeholder attribute is present)

- **PROF-12** — Save button is present and disabled until the form is dirty
  - Given the current profile form is loaded
  - When no editable field has changed
  - Then the "Save changes" button is disabled
  - When the user changes the display name
  - Then the button becomes enabled
  - Priority: P0 · Type: edge · Automated: yes (e2e: "profile save button is disabled until a field changes")

- **PROF-13** — Keyboard navigation through profile fields
  - Given the user is on `/account/profile`
  - When they tab through the page
  - Then focusable elements receive focus in a logical order
  - Priority: P1 · Type: a11y · Automated: manual (requires visual/AT inspection)

- **PROF-14** — Field labels are associated with inputs
  - Given the profile form is rendered
  - When a screen reader inspects the Email, Display name, and Language controls
  - Then each control has an accessible label / aria-label
  - Priority: P1 · Type: a11y · Automated: partial (e2e asserts aria-label presence)

- **PROF-15** — Edit display name and persist
  - Given a user wants to change their display name
  - When they type a new value and submit
  - Then the name is saved, a success toast is shown, and the value survives reload
  - Priority: P0 · Type: happy · Automated: yes (e2e: "profile edit saves display name and survives reload")

- **PROF-16** — Clear display name
  - Given the display name field has a value
  - When the user clears it and submits
  - Then the backend stores `null` and the UI shows the empty/placeholder state
  - Priority: P1 · Type: edge · Automated: backend covered; frontend full path not separately automated

- **PROF-17** — Network/API error on save
  - Given the API is unreachable or returns a validation/business error
  - When the user submits
  - Then a toast error is displayed and the form remains editable
  - Priority: P1 · Type: error · Automated: manual (requires network interception/API stubbing)

---

## Known Gaps / Assumptions

1. E-mail change is intentionally not part of the profile form; it needs a verified e-mail-change flow.
2. Skeleton state (PROF-06), cache freshness (PROF-10), and save-error UI (PROF-17) need network interception or manual observation.
3. Accessibility audit (PROF-13) requires assistive technology; PROF-14 is partially covered by e2e attribute checks.
