# Identity Admin — Test Scenario Catalog

**Route:** `/admin` (tenant-scoped; served under the `(tenant)` layout)
**Backend endpoints:**
- `POST   /v1/identity/admin/users/{userId}/roles`  — requires `identity.manage_roles`
- `DELETE /v1/identity/admin/users/{userId}/roles/{role}` — requires `identity.manage_roles`
- `GET    /v1/identity/admin/users/{userId}/audit`  — requires `audit.read`
- `POST   /v1/identity/admin/machine-tokens`        — requires `platform.machine_tokens`
- `GET    /v1/identity/admin/machine-tokens?tenantId={tenantId}` — requires `platform.machine_tokens`
- `POST   /v1/identity/admin/machine-tokens/{tokenId}/revoke?tenantId={tenantId}` — requires `platform.machine_tokens`

**Permission model:** The server component reads `session.user.permissions` at render time and
passes `canManageRoles` / `canReadAudit` boolean props down to the client island. The backend is
the real enforcement gate (`.RequirePermission`); the frontend mirrors the gate in the UI so that
unpermitted sections are simply absent from the DOM rather than returning 403 on interaction.

Machine-token issuance is exposed from `/platform/tenants/{tenantId}` because the token is tenant-scoped
and that route already has the target tenant id. The button is shown only when the session includes
`platform.machine_tokens`; the backend remains the real enforcement gate.

**Key behaviour for self-serve users:** A freshly-registered tenant user has NO Identity admin
permissions (`identity.manage_roles`, `audit.read`). Navigating directly to `/admin` triggers
a server-side `redirect("/")` — the user lands on the dashboard without ever seeing a blank
admin page.

**Admin nav:** `/admin` is NOT listed in `NAV_ITEMS` (the tenant sidebar). It is only reachable
by direct URL or a future privileged nav entry. There is no "Admin" link in the sidebar for
normal users.

---

## Scenarios

### Access control

- **IADM-01** — Non-admin user visiting `/admin` is redirected to dashboard
  - Given the primary user is authenticated with no Identity admin permissions
  - When they navigate directly to `/admin`
  - Then the server redirects them to `/` (dashboard) and the Identity admin page content is never rendered
  - Priority: P0 · Type: security · Automated: yes (e2e: "non-admin visiting /admin is redirected to dashboard")

- **IADM-02** — Admin link absent from sidebar for non-admin users
  - Given the primary user has no `identity.manage_roles` or `audit.read` permissions
  - When they view the authenticated app shell
  - Then no "Admin" nav link appears in the sidebar navigation
  - Priority: P1 · Type: security · Automated: yes (e2e: "no admin nav link visible for non-admin user")

- **IADM-03** — Unauthenticated access to `/admin` redirects to login
  - Given the user is not authenticated
  - When they navigate directly to `/admin`
  - Then they are redirected to `/login` (the `(tenant)` layout auth guard fires before the permission check)
  - Priority: P0 · Type: security · Automated: yes (e2e: "unauthenticated /admin redirects to login")

- **IADM-04** — Admin page renders with correct heading for a user with `identity.manage_roles`
  - Given a user has the `identity.manage_roles` permission
  - When they navigate to `/admin`
  - Then the heading "Identity admin" is visible, the "User lookup" card is present, and the "Role management" card appears after looking up a valid user ID
  - Priority: P0 · Type: happy · Automated: manual (requires a seeded admin user; the E2E primary is non-admin)

- **IADM-05** — Admin page renders with correct heading for a user with `audit.read` only
  - Given a user has `audit.read` but not `identity.manage_roles`
  - When they navigate to `/admin`
  - Then the heading "Identity admin" is visible, the "User lookup" card is present, but no "Role management" card appears after lookup
  - Priority: P1 · Type: edge · Automated: manual (requires a seeded user with only `audit.read`)

### User lookup panel

- **IADM-06** — Lookup button disabled when input is empty
  - Given the admin user is on `/admin` (or, for non-admin testing, the panel UI in isolation)
  - When the user ID input is empty
  - Then the "Look up" button is disabled
  - Priority: P1 · Type: edge · Automated: manual (non-admin is redirected before seeing the panel)

- **IADM-07** — Invalid UUID format shows inline validation error
  - Given an admin is on `/admin` with the lookup panel visible
  - When they type a non-UUID string (e.g. "not-a-uuid") into the User ID field and click "Look up"
  - Then an inline error message "Enter a valid user UUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)." appears below the input and no downstream sections appear
  - Priority: P1 · Type: error · Automated: manual

- **IADM-08** — Valid UUID triggers role and audit sections
  - Given an admin with both permissions is on `/admin`
  - When they enter a well-formed UUID and click "Look up" (or press Enter)
  - Then the "Role management" card and "Identity audit trail" card both appear, each showing the looked-up UUID as a subtitle
  - Priority: P0 · Type: happy · Automated: manual

- **IADM-09** — Enter key in UUID input triggers lookup
  - Given an admin is on `/admin` with a valid UUID typed
  - When they press the Enter key while focused on the User ID input
  - Then the same lookup behaviour fires as clicking "Look up"
  - Priority: P2 · Type: a11y · Automated: manual

### Role management (requires `identity.manage_roles`)

- **IADM-10** — Role management card NOT shown for non-admin (canManageRoles=false)
  - Given the primary user (no admin permissions) somehow had the panel rendered (e.g. canManageRoles=false prop)
  - Then the "Role management" card, the "Assign role" input, and the "Assign" button are absent from the DOM
  - Additionally, no revoke (×) buttons appear on role badges
  - Priority: P0 · Type: security · Automated: yes (e2e: "role-management UI absent for non-admin after lookup")

- **IADM-11** — Empty current roles shows "No roles assigned."
  - Given an admin is managing a user with no roles
  - When the role manager renders
  - Then the text "No roles assigned." is visible and no role badges are shown
  - Priority: P1 · Type: edge · Automated: manual

- **IADM-12** — Assign role happy path
  - Given an admin with `identity.manage_roles` has looked up a user
  - When they type a valid role name and click "Assign" (or press Enter)
  - Then `POST /v1/identity/admin/users/{userId}/roles` is called with `{ role }`, and the input clears on success
  - Priority: P0 · Type: happy · Automated: manual

- **IADM-13** — Assign role with blank input — button disabled
  - Given the "Assign role" input is empty
  - Then the "Assign" button is disabled and no request is made
  - Priority: P1 · Type: edge · Automated: manual

- **IADM-14** — Revoke role happy path
  - Given an admin sees a user's role badge with a revoke (×) button
  - When they click the × button for a role
  - Then `DELETE /v1/identity/admin/users/{userId}/roles/{role}` is called and the admin queries are invalidated on success
  - Priority: P0 · Type: happy · Automated: manual

- **IADM-15** — Assign/revoke buttons disabled while a mutation is in-flight
  - Given an assign or revoke request is pending
  - Then both the "Assign" button and any revoke buttons are disabled to prevent double-submit
  - Priority: P1 · Type: edge · Automated: manual

- **IADM-16** — Backend 403 on role assign (token without permission) surfaces as toast error
  - Given a user whose token lacks `identity.manage_roles` somehow calls the assign endpoint
  - Then the backend returns 403 and the error mapper surfaces an error toast (not a page crash)
  - Priority: P0 · Type: security · Automated: manual

- **IADM-17** — Backend 404 on assign for non-existent user surfaces as toast error
  - Given an admin enters a valid UUID that doesn't match any user
  - When they try to assign a role
  - Then the backend returns 404 (`user.not_found`) and a toast error is shown
  - Priority: P1 · Type: error · Automated: manual

- **IADM-18** — Assign role is idempotent (already-assigned role shows success)
  - Given a user already has role "admin"
  - When an admin assigns "admin" again
  - Then the backend returns 200 (the handler is idempotent) and no error is shown
  - Priority: P2 · Type: edge · Automated: manual

### Audit trail (requires `audit.read`)

- **IADM-19** — Audit trail section NOT shown for user without `audit.read`
  - Given the primary user (no `audit.read`) somehow triggers a lookup on the panel
  - Then the "Identity audit trail" card and its table are absent from the DOM, and the text "Audit trail hidden — requires the audit.read permission." appears instead
  - Priority: P0 · Type: security · Automated: yes (e2e: "audit trail section absent for non-admin after lookup")

- **IADM-20** — Audit table columns: Timestamp, Action badge, Changed fields
  - Given an admin with `audit.read` looks up a user with existing audit entries
  - When the audit trail table renders
  - Then columns "Timestamp", "Action", and "Changed fields" are visible; Action is rendered as a coloured badge (Insert/Update/Delete); changed fields show field:value pairs
  - Priority: P0 · Type: happy · Automated: manual

- **IADM-21** — Empty audit trail shows empty state
  - Given an admin looks up a user whose Identity audit entries list is empty
  - When the audit trail query resolves
  - Then the empty state "No audit entries" / "No Identity audit events recorded for this user yet." is shown instead of a table
  - Priority: P1 · Type: edge · Automated: manual

### Machine tokens (requires `platform.machine_tokens`)

- **IADM-22** — Tenant detail shows machine-token action only to permitted admins
  - Given a platform admin opens `/platform/tenants/{tenantId}`
  - When their session includes `platform.machine_tokens`
  - Then the "Machine token" action is visible in the tenant header
  - And a session without that permission does not render the action
  - Priority: P0 · Type: security · Automated: manual

- **IADM-23** — Issue machine token happy path
  - Given a permitted admin opens the tenant detail and clicks "Machine token"
  - When they enter a machine name and submit
  - Then `POST /v1/identity/admin/machine-tokens` is called with `{ tenantId, name }`
  - And the returned raw token is displayed once with its expiry timestamp
  - Priority: P0 · Type: happy · Automated: manual

- **IADM-24** — Empty machine name cannot submit
  - Given the machine-token dialog is open
  - When the name field is empty or whitespace
  - Then the submit button is disabled and no request is sent
  - Priority: P1 · Type: edge · Automated: manual

- **IADM-25** — Raw machine token is one-shot UI state
  - Given a token was issued and is visible in the dialog
  - When the admin copies it or closes the dialog
  - Then the token can be copied to the clipboard, and closing the dialog clears it from UI state
  - Priority: P0 · Type: security · Automated: manual

- **IADM-26** — Machine-token list is metadata-only
  - Given a permitted admin opens `/platform/tenants/{tenantId}`
  - When the machine-token panel loads
  - Then `GET /v1/identity/admin/machine-tokens?tenantId={tenantId}` returns token rows with id, machine subject, name, status, created/expires/revoked timestamps
  - And the raw JWT/access token is not returned or rendered
  - Priority: P0 · Type: security · Automated: yes (backend integration)

- **IADM-27** — Revoke active machine token
  - Given a machine token is listed as Active
  - When the admin clicks revoke
  - Then `POST /v1/identity/admin/machine-tokens/{tokenId}/revoke?tenantId={tenantId}` marks it Revoked, refreshes the list, and disables further use of that JWT
  - Priority: P0 · Type: security · Automated: yes (backend integration)

- **IADM-28** — Revoke is idempotent
  - Given a machine token is already Revoked
  - When the same revoke endpoint is called again
  - Then the endpoint still returns 200 with the original `revokedAt` timestamp
  - Priority: P1 · Type: edge · Automated: yes (backend integration)

- **IADM-29** — Revoked machine JWT is rejected at auth validation
  - Given an issued machine JWT worked before revocation
  - When its issuance row is revoked
  - Then subsequent calls with that same JWT return 401 before endpoint authorization/business logic runs
  - Priority: P0 · Type: security · Automated: yes (backend integration)

- **IADM-30** — Erased PII audit values render as "[erased]"
  - Given a user's DEK has been shredded (GDPR erasure)
  - When an admin views their audit trail
  - Then fields whose values were personal data (email, etc.) show "[erased]" in italic muted text, not the original value
  - Priority: P1 · Type: security · Automated: manual (requires GDPR erasure to have run)

- **IADM-31** — Audit entries ordered newest-first
  - Given a user has multiple audit entries
  - When the audit trail table renders
  - Then the most recent entry appears at the top (backend returns `OrderByDescending(Timestamp)`)
  - Priority: P1 · Type: happy · Automated: manual

- **IADM-32** — Backend 403 on audit trail GET for unpermitted user surfaces as error
  - Given a user whose token lacks `audit.read` somehow calls the audit endpoint
  - Then the backend returns 403 and the UI shows an error (empty state or toast, not a page crash)
  - Priority: P0 · Type: security · Automated: manual

### Keyboard / accessibility

- **IADM-33** — User ID input and Look up button are keyboard-accessible
  - Given an admin is on `/admin`
  - When they tab to the User ID input, type a UUID, and press Enter or Tab to "Look up" then Space/Enter
  - Then lookup fires without requiring a mouse click
  - Priority: P2 · Type: a11y · Automated: manual

- **IADM-34** — Assign role input and button are keyboard-accessible
  - Given the role manager is visible
  - When the admin tabs to the "Assign role" input, types a role, and presses Enter
  - Then the assign action fires
  - Priority: P2 · Type: a11y · Automated: manual
