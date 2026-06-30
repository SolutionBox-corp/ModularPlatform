# Operations — Test Scenario Catalog

**Route:** `/operations`
**Backend:** `GET /v1/operations` -> `PagedResponse<OperationListItem>`

The Operations module is a base-platform history/status surface for long-running work. Product modules can use it when they create generic operations through `IOperationStore`.

---

## Scenarios

- **OPS-01** — Operations page is visible from tenant navigation
  - Given the user has the `operations` entitlement
  - When they open the tenant app
  - Then the sidebar contains an "Operations" link
  - Priority: P0 · Type: happy · Automated: yes (e2e: "sidebar exposes Operations link")

- **OPS-02** — Empty history for a fresh account
  - Given a fresh user has no operations
  - When they visit `/operations`
  - Then the page shows the heading and the empty-state copy
  - Priority: P0 · Type: happy · Automated: yes (e2e: "fresh account shows empty operations history")

- **OPS-03** — Started operation appears in history
  - Given a user starts a long-running operation
  - When they visit `/operations`
  - Then the history table contains that operation type, status, id, and timestamps
  - Priority: P0 · Type: happy · Automated: yes (e2e: "started demo operation appears in operations history")

- **OPS-04** — Owner scoping
  - Given another user has operations
  - When the current user opens `/operations`
  - Then those foreign operations are not shown
  - Priority: P0 · Type: security · Automated: backend integration test (`Operations_list_is_paged_owner_scoped_newest_first_and_has_empty_state`)

- **OPS-05** — Pagination
  - Given the user has more operations than one page
  - When they open `/operations`
  - Then the list shows newest first and exposes pagination controls
  - Priority: P1 · Type: edge · Automated: backend integration test for page shape/order; frontend manual until enough seeded rows exist

---

## Known Gaps / Assumptions

1. The page lists generic Operations records. Domain-specific details such as imported row counts or export links belong in the owning module's own run/detail page.
2. Operations still use polling on individual status components; realtime transition pushes are a separate base-platform gap.
3. Cancel/retry/progress-percent are not part of the current backend contract.
