# Platform Admin (`/platform/*`) — Test Scenarios

The platform admin area is served **exclusively** on the `admin.<root>` host (the "admin host"). The Next.js
proxy (`proxy.ts`) classifies the request host, sets `x-tenant: __admin__`, and rewrites the admin host's root
path into `/platform/*`. When the same `/platform/*` URL is requested from the normal tenant/apex host (e.g.
`localhost:3000`), the proxy silently rewrites it to `/not-found` — the platform UI is never rendered.

Authorization is two-factor inside `app/platform/layout.tsx`:
1. **Host gate** — `x-tenant` header must equal `__admin__` (set only by the proxy for `admin.*` hosts).
2. **Permission gate** — the session user must hold `platform.tenants.manage` OR the `admin` role.

Both gates must pass; a regular authenticated user hitting the admin host still gets the 403 `ProblemDetails`
render. A user on the normal host never reaches the layout at all (rewrites to 404).

All mutating endpoints (`POST /v1/tenant/admin/tenants`, `PUT .../entitlements/{key}`,
`POST .../invites`) require the `platform.tenants.manage` permission.

> **Note on automated coverage:** Because the E2E suite runs against `localhost:3000` (the apex/tenant host
> in dev), most platform admin flows **cannot be exercised end-to-end** without a running admin host at
> `admin.lvh.me:3000` or equivalent. The majority of scenarios below are therefore **manual** with explicit
> preconditions. The automated E2E spec covers the one thing that is observable from the normal host: that
> navigating to `/platform/*` on the tenant host does **not** expose the admin UI.

---

## Scenarios

### PA-01 — Normal host silently guards `/platform` (renders 404)
- **Given** the user navigates to `http://localhost:3000/platform` on the normal (apex/tenant) host
- **When** the page loads
- **Then** the platform admin UI is never rendered; the page shows the 404/Not Found page (the proxy rewrites to `/not-found` before the layout even runs)
- Priority: **P0** · Type: security · Automated: yes (e2e: `platform admin is not exposed on the normal host — /platform renders 404`)

### PA-02 — Normal host guards `/platform/tenants` (renders 404)
- **Given** the user navigates to `http://localhost:3000/platform/tenants` on the normal host
- **When** the page loads
- **Then** the 404 Not Found page is rendered; the platform layout, sidebar, and any tenant/billing data are absent
- Priority: **P0** · Type: security · Automated: yes (e2e: `platform admin is not exposed on the normal host — /platform/tenants renders 404`)

### PA-03 — Normal host guards `/platform/tenants/{uuid}` (renders 404)
- **Given** the user navigates to `http://localhost:3000/platform/tenants/00000000-0000-0000-0000-000000000001`
- **When** the page loads
- **Then** the 404 Not Found page is rendered
- Priority: **P0** · Type: security · Automated: yes (e2e: `platform admin is not exposed on the normal host — /platform/tenants/{id} renders 404`)

### PA-04 — Client cannot spoof `x-tenant` header to gain admin access
- **Given** an authenticated regular user sends a request to `/platform` with a spoofed `x-tenant: __admin__` header
- **When** the proxy processes the request
- **Then** the proxy strips the client-supplied `x-tenant` before passing it downstream; the platform layout receives the real host-derived tenant and the 403 error is shown (not the admin UI)
- Priority: **P0** · Type: security · Automated: manual (requires intercepting HTTP headers at the transport layer)

### PA-05 — Unauthenticated user on admin host is redirected to `/login`
- **Preconditions** running admin host at `admin.lvh.me:3000`; no active session
- **Given** an unauthenticated browser navigates to `https://admin.lvh.me:3000/`
- **When** the platform layout runs its session check
- **Then** the user is redirected to `/login` (the `isAuthenticated` guard fires before the permission check)
- Priority: **P0** · Type: security · Automated: manual

### PA-06 — Authenticated regular user on admin host sees 403 ProblemDetails
- **Preconditions** running admin host; a user who does **not** hold `platform.tenants.manage` or the `admin` role
- **Given** the user is authenticated and navigates to the admin host
- **When** the platform layout evaluates the permission gate
- **Then** a 403 `ProblemDetails` card is rendered ("You do not have access to the platform administration area."); no admin UI is shown; no sensitive data is leaked
- Priority: **P0** · Type: security · Automated: manual

### PA-07 — Admin host root redirects to platform overview
- **Preconditions** running admin host; user holds `platform.tenants.manage` or `admin` role
- **Given** the admin navigates to `https://admin.<root>/`
- **When** the proxy rewrites `/` → `/platform` and the layout passes both gates
- **Then** the platform overview page renders: heading "Platform administration", "Provision tenant" button, Platform billing card, and Tenant entitlement editor
- Priority: **P0** · Type: happy · Automated: manual

### PA-08 — Platform billing card shows current tenant plan and module status
- **Preconditions** admin host + `platform.tenants.manage`; `GET /v1/tenant/admin/platform-billing` returns `{ plan: "free", modules: [...] }`
- **Given** the admin is on the platform overview page
- **When** the Platform billing card loads
- **Then** the card displays the plan name (e.g. "free"), and for each module its key and enabled ("on") / disabled ("off") status
- Priority: **P1** · Type: happy · Automated: manual

### PA-09 — Platform billing card shows "No billing data." on error
- **Preconditions** admin host; `GET /v1/tenant/admin/platform-billing` returns 403 or network error
- **Given** the admin is on the platform overview page
- **When** the billing status query fails
- **Then** the card displays "No billing data." (empty state); no crash
- Priority: **P2** · Type: error · Automated: manual

### PA-09a — Tenant registry search and status filter
- **Preconditions** admin host + at least two tenants with distinct names/subdomains
- **Given** the admin opens `/platform/tenants`
- **When** they type part of a tenant name or subdomain and optionally select a status
- **Then** `GET /v1/tenant/admin/tenants` is called with `search` and `status`; the table shows only matching tenants and pagination resets to the first page
- Priority: **P0** · Type: happy/edge · Automated: backend integration test (`Tenant_list_can_be_filtered_by_search_and_status`); UI manual until admin-host E2E is available

### PA-10 — Provision tenant: happy path
- **Preconditions** admin host + `platform.tenants.manage`
- **Given** the admin opens the "Provision tenant" dialog, enters a valid organisation name (e.g. "Acme Corp") and a valid lowercase subdomain (e.g. "acme-test")
- **When** the admin clicks "Provision"
- **Then** `POST /v1/tenant/admin/tenants` is called; on success the dialog closes; a "Tenant provisioned successfully." toast appears; the returned `tenantId` UUID is available for subsequent entitlement operations
- Priority: **P0** · Type: happy · Automated: manual

### PA-11 — Provision tenant: empty name shows validation error
- **Preconditions** admin host + dialog open
- **Given** the name field is blank
- **When** the admin submits
- **Then** an inline error "Name is required" appears below the name field; the API is not called
- Priority: **P1** · Type: error · Automated: manual

### PA-12 — Provision tenant: name exceeds 256 characters
- **Preconditions** admin host + dialog open
- **Given** the name field contains 257 characters
- **When** the admin submits
- **Then** inline error "Name must be 256 characters or fewer" appears
- Priority: **P2** · Type: edge · Automated: manual

### PA-13 — Provision tenant: invalid subdomain format
- **Preconditions** admin host + dialog open
- **Given** the subdomain field contains "Acme Corp" (uppercase + spaces)
- **When** the admin submits
- **Then** client-side Zod validation shows "Subdomain must be lowercase alphanumeric with hyphens (no leading/trailing hyphen)" before any API call
- Priority: **P1** · Type: error · Automated: manual

### PA-14 — Provision tenant: subdomain with leading/trailing hyphen
- **Preconditions** admin host + dialog open
- **Given** the subdomain is "-bad" or "bad-"
- **When** the admin submits
- **Then** the Zod regex catches it; inline error shown
- Priority: **P2** · Type: edge · Automated: manual

### PA-15 — Provision tenant: reserved subdomain rejected by backend
- **Preconditions** admin host + dialog open
- **Given** the subdomain is one of the reserved labels: "admin", "www", "api", "static", "assets"
- **When** the admin submits (client-side validation passes because the reserved-word check is backend-only in the validator)
- **Then** the backend returns a 409 with `errorCode: "tenant.subdomain.reserved"`; a toast or inline error is shown; the dialog stays open
- Priority: **P1** · Type: error · Automated: manual

### PA-16 — Provision tenant: duplicate subdomain returns 409
- **Preconditions** admin host + a tenant with subdomain "taken" already exists
- **Given** the admin enters subdomain "taken"
- **When** the admin submits
- **Then** the backend returns 409 `tenant.subdomain_taken`; error surfaced to the user; dialog stays open
- Priority: **P1** · Type: error · Automated: manual

### PA-17 — Provision tenant: dialog resets after cancel
- **Preconditions** admin host + dialog open with partial values
- **Given** the admin types a name and subdomain but clicks the close ("×") button or presses Escape
- **When** the dialog closes and is reopened
- **Then** name and subdomain fields are empty (form reset)
- Priority: **P2** · Type: edge · Automated: manual

### PA-18 — Entitlement editor: entering a valid UUID and clicking "Open" loads toggles
- **Preconditions** admin host + platform overview page visible
- **Given** the admin types a valid tenant UUID into the "Tenant ID (UUID)" input
- **When** the admin clicks "Open" (or presses Enter)
- **Then** a separator appears with the tenant UUID displayed, a "Create invite" button, and a list of module toggles loaded from `GET /v1/tenant/admin/tenants/{id}` (billing, notifications, files, operations, gdpr, marketing)
- Priority: **P0** · Type: happy · Automated: manual

### PA-19 — Entitlement editor: empty input keeps "Open" disabled
- **Preconditions** admin host + platform overview
- **Given** the UUID input is blank
- **Then** the "Open" button is disabled
- Priority: **P1** · Type: edge · Automated: manual

### PA-20 — Toggle a module entitlement ON
- **Preconditions** admin host + entitlement editor open with a valid tenant UUID
- **Given** the "Billing" module switch shows as off
- **When** the admin clicks the switch
- **Then** the switch moves to "on" optimistically; `PUT /v1/tenant/admin/tenants/{id}/entitlements/billing` is called with `{ enabled: true }`; a "billing enabled." toast appears; the tenant detail query is refetched so the switch reconciles with persisted state
- Priority: **P0** · Type: happy · Automated: manual

### PA-21 — Toggle a module entitlement OFF
- **Preconditions** admin host + entitlement editor open; a switch currently showing "on"
- **Given** a module switch is in the "on" position
- **When** the admin clicks the switch
- **Then** the switch moves to "off" optimistically; the PUT is called with `{ enabled: false }`; a "billing disabled." toast appears; the tenant detail query is refetched so the switch reconciles with persisted state
- Priority: **P0** · Type: happy · Automated: manual

### PA-22 — Toggle reverts on API error
- **Preconditions** admin host; the PUT endpoint returns 403 or 500 (simulated via network devtools or proxy)
- **Given** the admin clicks a module switch
- **When** the API call fails
- **Then** the switch snaps back to its previous state (optimistic revert); an error toast is shown
- Priority: **P1** · Type: error · Automated: manual

### PA-23 — Toggle with unknown tenant UUID returns 404
- **Preconditions** admin host; a non-existent UUID entered
- **Given** the admin opens the editor with a UUID that does not match any tenant, then clicks a switch
- **When** the PUT is sent
- **Then** the backend returns 404 `tenant.not_found`; the switch reverts; error is surfaced
- Priority: **P1** · Type: error · Automated: manual

### PA-24 — Create invite: happy path (7-day default)
- **Preconditions** admin host + entitlement editor open with a valid tenant UUID
- **Given** the admin clicks "Create invite"
- **When** the dialog opens and the admin submits with the default 7-day expiry
- **Then** `POST /v1/tenant/admin/tenants/{id}/invites` is called; the dialog switches to a "token revealed" view showing the raw hex token in a read-only input, an expiry date, and a "Copy token" button; the token is shown exactly once
- Priority: **P0** · Type: happy · Automated: manual

### PA-25 — Create invite: copy button copies token to clipboard
- **Preconditions** token-revealed view open
- **Given** the invite token is displayed
- **When** the admin clicks the copy icon
- **Then** the clipboard contains the token; the icon briefly shows a checkmark; a "Invite token copied to clipboard." toast appears
- Priority: **P1** · Type: happy · Automated: manual

### PA-26 — Create invite: token is NOT shown again after dialog close
- **Preconditions** token-revealed view open
- **Given** the admin closes the dialog (via the close button) then reopens it
- **When** the dialog reopens
- **Then** the expiry-days form is shown (not the token); the previous raw token is gone from the UI
- Priority: **P0** · Type: security · Automated: manual

### PA-27 — Create invite: custom expiry days validation
- **Preconditions** admin host + create-invite dialog open (expiry-days form)
- **Given** the admin sets expiry to 0 days
- **When** the admin submits
- **Then** client-side error "Minimum 1 day" appears; no API call
- Priority: **P1** · Type: error · Automated: manual

### PA-28 — Create invite: expiry > 30 days validation
- **Preconditions** create-invite dialog open
- **Given** the admin enters 31 days
- **When** the admin submits
- **Then** client-side error "Maximum 30 days" appears and no API call is made
- Priority: **P1** · Type: edge · Automated: manual

### PA-29 — Create invite: backend expiry range error surfaced
- **Preconditions** create-invite dialog; the backend rejects the expiry range despite client validation passing (for example, a stale client bundle or direct API call)
- **When** the API returns 400 `tenant.invite.expiry_out_of_range`
- **Then** an error toast is shown; the dialog stays in the form view
- Priority: **P1** · Type: error · Automated: manual

### PA-30 — Create invite: unknown tenant UUID returns 404
- **Preconditions** create-invite dialog with a non-existent tenant UUID in the editor
- **When** admin submits
- **Then** backend returns 404 `tenant.not_found`; error is surfaced; form stays open
- Priority: **P1** · Type: error · Automated: manual

### PA-31 — Platform overview `/platform` page title and metadata
- **Preconditions** admin host + authenticated admin
- **Given** the admin navigates to `/platform`
- **Then** the HTML `<title>` is "Platform — ModularPlatform"
- Priority: **P2** · Type: a11y · Automated: manual

### PA-32 — Platform tenants page `/platform/tenants` title
- **Preconditions** admin host + authenticated admin
- **Given** the admin navigates to `/platform/tenants`
- **Then** the HTML `<title>` is "Tenants — Platform Admin"
- Priority: **P2** · Type: a11y · Automated: manual

### PA-33 — Platform tenant detail page `/platform/tenants/{id}` title
- **Preconditions** admin host + authenticated admin
- **Given** the admin navigates to `/platform/tenants/{uuid}`
- **Then** the HTML `<title>` is "Tenant detail — Platform Admin"; the page shows the UUID in monospace text and a back link "All tenants"
- Priority: **P2** · Type: a11y · Automated: manual

### PA-34 — Platform nav sidebar is permission-gated
- **Preconditions** admin host + authenticated admin
- **Given** the platform layout renders
- **Then** the sidebar shows:
  - "Tenants" link (requires `platform.tenants.manage`)
  - "Users" link (requires `identity.manage_roles`)
  - "Audit" link (requires `audit.read`)
  Only links for which the user holds the required permission are visible.
- Priority: **P1** · Type: security · Automated: manual

### PA-35 — API call from tenant host to `/v1/tenant/admin/*` is rejected with 403
- **Preconditions** authenticated regular user session (no `platform.tenants.manage` permission)
- **Given** the user makes a direct API call to `POST /v1/tenant/admin/tenants` with their session token
- **When** the backend processes the request
- **Then** the backend returns 403 Forbidden (enforced by `.RequirePermission(PlatformPermissions.PlatformTenantsManage)`)
- Priority: **P0** · Type: security · Automated: manual (requires direct API call; confirmed by the backend integration test `A_non_admin_cannot_provision_a_tenant`)

### PA-36 — Keyboard navigation in Provision Tenant dialog
- **Preconditions** admin host + dialog open
- **Given** the dialog is open
- **When** the user navigates with Tab
- **Then** focus moves in order: Name input → Subdomain input → Provision button; Escape closes the dialog; focus returns to the trigger
- Priority: **P2** · Type: a11y · Automated: manual

### PA-37 — Provision tenant button is disabled while submitting (double-submit prevention)
- **Preconditions** admin host + provision dialog open; slow network
- **Given** the admin clicks "Provision" and the request is in-flight
- **Then** the button shows "Provisioning…" and is disabled; clicking again has no effect
- Priority: **P1** · Type: edge · Automated: manual

### PA-38 — Entitlement toggle shows loading skeleton while fetching
- **Preconditions** admin host + entitlement editor; backend call is intentionally slow
- **Given** `isLoading` is true for the EntitlementToggles component
- **Then** five skeleton rows are rendered instead of switches (no interactive elements)
- Priority: **P2** · Type: edge · Automated: manual

### PA-39 — "No entitlement data available." empty state
- **Preconditions** EntitlementToggles rendered with empty modules array and `fallbackToDefaults=false`
- **Then** the text "No entitlement data available." is shown
- Priority: **P2** · Type: edge · Automated: manual
