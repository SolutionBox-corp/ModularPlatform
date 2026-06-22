/**
 * Identity Admin — E2E spec
 *
 * The primary E2E user (shared storageState) is a self-serve registrant with NO
 * Identity admin permissions (identity.manage_roles / audit.read). That is the
 * correct subject for the permission-gate assertions here.
 *
 * Tests that require an actual admin user (role assign/revoke, audit trail data)
 * are documented in docs/test-scenarios/identity-admin.md and marked manual/partial.
 *
 * IADM-01  — non-admin visiting /admin is redirected to dashboard
 * IADM-02  — no "Admin" nav link visible for non-admin user
 * IADM-03  — unauthenticated /admin redirects to login
 * IADM-10  — role-management UI absent for non-admin after lookup  (partial — panel not reachable, see note)
 * IADM-19  — audit trail section absent for non-admin after lookup (partial — panel not reachable, see note)
 *
 * Note on IADM-10 / IADM-19: because a non-admin is redirected before the panel renders,
 * we cannot reach the lookup panel to exercise the conditional rendering of the role
 * manager and audit table via the real page. Those scenarios are verified structurally:
 * the redirect (IADM-01) means the panel is never in the DOM for a non-admin user.
 * Full verification of the conditionality requires an admin user (manual / partial).
 */

import { test, expect } from "@playwright/test";
import { ANONYMOUS } from "./helpers";

// ---------------------------------------------------------------------------
// IADM-01 + IADM-02 — authenticated non-admin (shared primary storageState)
// ---------------------------------------------------------------------------

test.describe("Identity admin — authenticated non-admin primary user", () => {
  test("IADM-01: non-admin visiting /admin is redirected to dashboard", async ({
    page,
  }) => {
    // The server component reads permissions from the session and redirects to "/"
    // when the user has neither identity.manage_roles nor audit.read.
    await page.goto("/admin");

    // Must land on "/" (the redirect target), not stay on /admin.
    await expect(page).toHaveURL("/", { timeout: 15_000 });

    // The dashboard heading confirms we are on the right page.
    await expect(
      page.getByRole("heading", { name: /welcome back/i }),
    ).toBeVisible();
  });

  test("IADM-02: no admin nav link visible for non-admin user", async ({
    page,
  }) => {
    await page.goto("/");

    // NAV_ITEMS does not include an /admin entry — there should be no "Admin" link
    // in the tenant sidebar for a non-admin self-serve user.
    const sidebar = page
      .getByRole("navigation")
      .or(page.locator('[data-slot="sidebar"]'))
      .first();

    // Dashboard link IS there (sanity check that the sidebar rendered).
    await expect(
      sidebar.getByRole("link", { name: "Dashboard", exact: true }),
    ).toBeVisible();

    // No "Admin" link must exist anywhere in the sidebar.
    await expect(
      sidebar.getByRole("link", { name: /admin/i }),
    ).toHaveCount(0);
  });

  test("IADM-10+IADM-19 structural: /admin page content never renders for non-admin", async ({
    page,
  }) => {
    // The redirect (IADM-01) guarantees the IdentityAdminPanel is never mounted.
    // Assert that key admin-specific text is absent from the DOM entirely.
    await page.goto("/admin");
    await expect(page).toHaveURL("/", { timeout: 15_000 });

    // These strings only appear inside the admin panel — they must be absent.
    await expect(page.getByText("User lookup")).toHaveCount(0);
    await expect(page.getByText("Role management")).toHaveCount(0);
    await expect(page.getByText("Identity audit trail")).toHaveCount(0);
    await expect(page.getByText("identity.manage_roles")).toHaveCount(0);
    await expect(page.getByText("audit.read")).toHaveCount(0);
  });
});

// ---------------------------------------------------------------------------
// IADM-03 — unauthenticated access
// ---------------------------------------------------------------------------

test.describe("Identity admin — unauthenticated", () => {
  test.use(ANONYMOUS);

  test("IADM-03: unauthenticated /admin redirects to login", async ({
    page,
  }) => {
    // The (tenant) layout fires before the admin page's own permission check.
    // An unauthenticated user must land on /login.
    await page.goto("/admin");

    await expect(page).toHaveURL("/login", { timeout: 15_000 });

    // The login form confirms the redirect destination.
    await expect(
      page.getByRole("button", { name: /sign in/i }),
    ).toBeVisible();
  });
});
