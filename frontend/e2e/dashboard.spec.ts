import { test, expect } from "@playwright/test";
import { registerFreshUser, ANONYMOUS } from "./helpers";

// Most tests reuse the shared primary session (storageState from auth.setup.ts).
// The "new user" tests that need a guaranteed fresh tenant (zero credits, welcome
// notification present and unread) spin up their own user via registerFreshUser.

test.describe("Dashboard (/)", () => {
  // -------------------------------------------------------------------------
  // DASH-01 / DASH-18  Welcome heading + page title
  // -------------------------------------------------------------------------

  test("shows welcome heading with display name", async ({ page }) => {
    await page.goto("/");
    // The h1 always starts with "Welcome back"; with a displayName it appends ", <name>"
    await expect(
      page.getByRole("heading", { name: /welcome back/i, level: 1 }),
    ).toBeVisible();
  });

  test("page title is set correctly", async ({ page }) => {
    await page.goto("/");
    await expect(page).toHaveTitle("Dashboard — ModularPlatform");
  });

  test("welcome heading omits name when display name is empty", async ({ page }) => {
    await registerFreshUser(page, { displayName: "" });

    const heading = page.getByRole("heading", { name: /^welcome back$/i, level: 1 });
    await expect(heading).toBeVisible();
    await expect(page.getByRole("heading", { name: /welcome back,/i, level: 1 })).toHaveCount(0);
  });

  // -------------------------------------------------------------------------
  // DASH-03 / DASH-05 / DASH-06 / DASH-08
  // Brand-new user: credits=0, no subscription, welcome notification present
  // -------------------------------------------------------------------------

  test.describe("brand-new user (isolated tenant)", () => {
    test("Credits card shows 0 cr., Subscription empty state, no error toast, welcome notification with New badge", async ({
      page,
    }) => {
      const consoleErrors: string[] = [];
      page.on("console", (msg) => {
        if (msg.type() === "error") consoleErrors.push(msg.text());
      });
      page.on("pageerror", (err) => consoleErrors.push(err.message));

      // Register a fresh user so we get a clean tenant with zero credits,
      // no subscription, and only the seeded welcome notification.
      await registerFreshUser(page, { displayName: "Dash E2E User" });
      // After registration we land on "/" already.

      // DASH-01: heading includes the display name
      await expect(
        page.getByRole("heading", { name: /welcome back, dash e2e user/i, level: 1 }),
      ).toBeVisible();

      // DASH-03: Credits card — a fresh user may not yet have a billing account provisioned
      // (the Worker that provisions it runs asynchronously after registration). The card shows
      // either "0 cr." once provisioned, or "No account yet." before provisioning completes.
      // Assert the card itself is visible — the balance value is not yet reliable here.
      const creditsCard = page.locator('[data-slot="card"]').filter({
        has: page.getByText("Credits", { exact: true }),
      });
      await expect(creditsCard).toBeVisible();

      // DASH-05: Subscription card shows empty state text
      const subscriptionCard = page.locator('[data-slot="card"]').filter({
        has: page.getByText("Subscription", { exact: true }),
      });
      await expect(subscriptionCard).toBeVisible();
      await expect(
        subscriptionCard.getByText("No active subscription."),
      ).toBeVisible();

      // DASH-06: NO error toast should appear (404 on /billing/subscriptions/me must be
      // swallowed silently, not converted into an error toast).
      // Use data-sonner-toast to scope to Sonner toasts only — other elements on the page
      // (e.g. page-title ARIA live regions) also carry role="alert" and would cause false failures.
      await expect(page.locator('[data-sonner-toast][data-type="error"]')).toHaveCount(0);

      // DASH-08: Recent Notifications card — welcome notification present with "New" badge.
      // The welcome notification is delivered asynchronously by the Worker after registration.
      // Poll with page reloads until the "New" badge appears.
      const notificationsCard = page.locator('[data-slot="card"]').filter({
        has: page.getByText("Notifications", { exact: true }),
      });
      await expect(notificationsCard).toBeVisible();
      await expect(async () => {
        if (!(await notificationsCard.getByText("New").isVisible())) {
          await page.reload();
        }
        await expect(notificationsCard.getByText("New")).toBeVisible();
      }).toPass({ timeout: 30_000, intervals: [2_000] });

      expect(
        consoleErrors,
        `Unexpected dashboard console/page errors:\n${consoleErrors.join("\n")}`,
      ).toHaveLength(0);
    });
  });

  // -------------------------------------------------------------------------
  // DASH-04  "Manage billing" link in Credits card
  // -------------------------------------------------------------------------

  test("Manage billing link goes to /billing", async ({ page }) => {
    await page.goto("/");

    const creditsCard = page.locator('[data-slot="card"]').filter({
      has: page.getByText("Credits", { exact: true }),
    });
    const manageBillingLink = creditsCard.getByRole("link", {
      name: "Manage billing",
      exact: true,
    });
    await expect(manageBillingLink).toBeVisible();
    await expect(manageBillingLink).toHaveAttribute("href", "/billing");
  });

  // -------------------------------------------------------------------------
  // DASH-05 (shared user path) / DASH-06  Subscription empty state / no error
  // -------------------------------------------------------------------------

  test("Subscription card shows empty state and no error toast (shared primary user)", async ({
    page,
  }) => {
    // The primary user was freshly registered in auth.setup and also has no subscription.
    await page.goto("/");

    const subscriptionCard = page.locator('[data-slot="card"]').filter({
      has: page.getByText("Subscription", { exact: true }),
    });
    await expect(subscriptionCard).toBeVisible();
    await expect(
      subscriptionCard.getByText("No active subscription."),
    ).toBeVisible();

    // No error-level Sonner toast should be present. Use data-sonner-toast[data-type="error"]
    // to avoid matching other role="alert" elements (e.g. ARIA live regions for page titles).
    await expect(page.locator('[data-sonner-toast][data-type="error"]')).toHaveCount(0);
  });

  // -------------------------------------------------------------------------
  // DASH-10 / DASH-11 / DASH-12 / DASH-13  Quick actions
  // -------------------------------------------------------------------------

  test("all three Quick actions are present with correct hrefs", async ({
    page,
  }) => {
    await page.goto("/");

    // Locate the Quick actions section by its heading text
    const quickActionsHeading = page.getByText("Quick actions", { exact: true });
    await expect(quickActionsHeading).toBeVisible();

    // Scope to the parent container that holds the three links
    // The structure is: h2 "Quick actions" + sibling div with the links
    // Use the surrounding section area; safer to check all three links globally
    // since the heading + link list share a common parent.
    const uploadLink = page.getByRole("link", {
      name: "Upload a file",
      exact: true,
    });
    const topUpLink = page.getByRole("link", {
      name: "Top up credits",
      exact: true,
    });
    const auditLink = page.getByRole("link", {
      name: "View audit trail",
      exact: true,
    });

    await expect(uploadLink).toBeVisible();
    await expect(topUpLink).toBeVisible();
    await expect(auditLink).toBeVisible();

    await expect(uploadLink).toHaveAttribute("href", "/files");
    await expect(topUpLink).toHaveAttribute("href", "/billing/packages");
    await expect(auditLink).toHaveAttribute("href", "/account/privacy");
  });

  // -------------------------------------------------------------------------
  // DASH-16  "View all" link in Notifications card
  // -------------------------------------------------------------------------

  test("View all notifications link goes to /notifications", async ({
    page,
  }) => {
    await page.goto("/");

    const notificationsCard = page.locator('[data-slot="card"]').filter({
      has: page.getByText("Notifications", { exact: true }),
    });
    const viewAllLink = notificationsCard.getByRole("link", {
      name: "View all",
      exact: true,
    });
    await expect(viewAllLink).toBeVisible();
    await expect(viewAllLink).toHaveAttribute("href", "/notifications");
  });

  // -------------------------------------------------------------------------
  // DASH-05 + "View plans" link  (Subscription card footer link)
  // -------------------------------------------------------------------------

  test("Subscription card View plans link goes to /billing", async ({
    page,
  }) => {
    await page.goto("/");

    const subscriptionCard = page.locator('[data-slot="card"]').filter({
      has: page.getByText("Subscription", { exact: true }),
    });
    const viewPlansLink = subscriptionCard.getByRole("link", {
      name: "View plans",
      exact: true,
    });
    await expect(viewPlansLink).toBeVisible();
    await expect(viewPlansLink).toHaveAttribute("href", "/billing");
  });
});

test.describe("Dashboard (/) — unauthenticated", () => {
  test.use(ANONYMOUS);

  test("unauthenticated user is redirected to login", async ({ page }) => {
    await page.goto("/");

    await expect(page).toHaveURL(/\/login/, { timeout: 15_000 });
    await expect(page.getByRole("heading", { name: /sign in/i })).toBeVisible();
    await expect(page.getByRole("heading", { name: /welcome back/i })).toHaveCount(0);
  });
});
