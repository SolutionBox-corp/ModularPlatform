import { test, expect } from "@playwright/test";
import { registerFreshUser, ANONYMOUS } from "./helpers";

/**
 * Notifications E2E spec.
 *
 * Every test that needs an unread welcome notification registers a FRESH user so it starts
 * from a known state. The shared primary session (default storageState) is used only for
 * read-only structural checks that cannot be ruined by prior mark-read mutations.
 *
 * Covered: NOTIF-01, NOTIF-02, NOTIF-03, NOTIF-04, NOTIF-06, NOTIF-09, NOTIF-13, NOTIF-15, NOTIF-20,
 * NOTIF-21, NOTIF-22, NOTIF-23, NOTIF-24.
 */

// ---------------------------------------------------------------------------
// Structural checks (shared primary session — read-only)
// ---------------------------------------------------------------------------

test.describe("Notifications page structure", () => {
  test("notifications page heading renders", async ({ page }) => {
    // NOTIF-02
    await page.goto("/notifications");
    await expect(
      page.getByRole("heading", { name: "Notifications", exact: true }),
    ).toBeVisible();
    await expect(
      page.getByText("Your recent activity and system messages."),
    ).toBeVisible();
  });
});

// ---------------------------------------------------------------------------
// Fresh-user flows (isolated, destructive-safe)
// ---------------------------------------------------------------------------

test.describe("Notifications feed — fresh user", () => {
  /**
   * Register a fresh user once for this describe block and share the page
   * across the tests that only READ (no mark-read). Tests that mutate state
   * register their own user inside the test body.
   */
  test.beforeEach(async () => {
    // Each test registers its own user — isolation is per test, not per describe.
    // Tests that need isolation do their own registerFreshUser call inside the body.
  });

  test("shows welcome notification unread with badge, button and timestamp", async ({
    page,
  }) => {
    // NOTIF-01, NOTIF-06
    await registerFreshUser(page);

    await page.goto("/notifications");

    // The welcome notification is delivered asynchronously by the Worker after registration.
    // Poll with page reloads until it appears — the feed does not auto-refresh via SSE on the
    // initial load if the Worker hasn't processed the event yet.
    const welcomeItem = page
      .locator("ul li")
      .filter({ hasText: "Welcome to the platform!" })
      .first();

    await expect(async () => {
      if (!(await welcomeItem.isVisible())) {
        await page.reload();
      }
      await expect(welcomeItem).toBeVisible();
    }).toPass({ timeout: 30_000, intervals: [2_000] });

    // "New" badge must be present for an unread item.
    await expect(welcomeItem.getByText("New")).toBeVisible();
    await expect(page.locator("ul").filter({ has: welcomeItem })).toBeVisible();

    // Checkmark "Mark as read" button must be present.
    const markReadBtn = welcomeItem.getByRole("button", {
      name: /mark .* as read/i,
    });
    await expect(markReadBtn).toBeVisible();

    // Timestamp: rendered as e.g. "Jun 19, 10:30 AM" — must look like a locale date string,
    // not an ISO 8601 or raw epoch.
    const timestampEl = welcomeItem.locator("p").last();
    await expect(timestampEl).toBeVisible();
    const timestampText = await timestampEl.textContent();
    // The format produced by toLocaleString("en", {month:"short", day:"numeric", hour:"2-digit", minute:"2-digit"})
    // will always contain a comma (e.g. "Jun 19, 10:30 AM") and a colon.
    expect(timestampText).toMatch(/\w{3} \d{1,2},/);
    expect(timestampText).toMatch(/:/);

    // The item row should have the highlighted (unread) background.
    // The component applies the bg-primary/5 Tailwind class on the <li> when unread.
    // Assert via the visible "New" badge being present rather than raw Tailwind class names,
    // since Tailwind class names can change without changing visual behaviour.
    // (badge visibility already asserted above)

    // Unread dot is aria-hidden (NOTIF-21).
    const dot = welcomeItem.locator("span[aria-hidden='true']").first();
    await expect(dot).toBeVisible();

    // Mark-as-read button has an accessible label (NOTIF-20).
    await expect(markReadBtn).toHaveAttribute("aria-label", /^Mark .* as read$/);
  });

  test("unread filter shows unread welcome then empty state after mark-all", async ({
    page,
  }) => {
    // NOTIF-23, NOTIF-24
    await registerFreshUser(page);
    await page.goto("/notifications");

    const welcomeItem = page
      .locator("ul li")
      .filter({ hasText: "Welcome to the platform!" })
      .first();

    await expect(async () => {
      if (!(await welcomeItem.isVisible())) {
        await page.reload();
      }
      await expect(welcomeItem).toBeVisible();
    }).toPass({ timeout: 30_000, intervals: [2_000] });

    const unreadFilter = page.getByRole("button", { name: /unread/i });
    await unreadFilter.click();
    await expect(unreadFilter).toHaveAttribute("aria-pressed", "true");
    await expect(welcomeItem).toBeVisible();
    await expect(welcomeItem.getByText("New")).toBeVisible();

    const markAll = page.getByRole("button", { name: /mark all read/i });
    await expect(markAll).toBeEnabled();
    await markAll.click();

    await expect(page.getByText("No unread notifications")).toBeVisible();
    await expect(
      page.getByText("You're all caught up. Switch to All to see earlier notifications."),
    ).toBeVisible();
    await expect(page.getByText("Welcome to the platform!")).toHaveCount(0);
  });

  test("mark-read removes badge and button from the feed item", async ({
    page,
  }) => {
    // NOTIF-03
    await registerFreshUser(page);
    await page.goto("/notifications");

    const welcomeItem = page
      .locator("ul li")
      .filter({ hasText: "Welcome to the platform!" })
      .first();

    // Poll with reloads: the welcome notification is delivered asynchronously by the Worker.
    await expect(async () => {
      if (!(await welcomeItem.isVisible())) {
        await page.reload();
      }
      await expect(welcomeItem).toBeVisible();
    }).toPass({ timeout: 30_000, intervals: [2_000] });

    // Confirm unread state before acting.
    await expect(welcomeItem.getByText("New")).toBeVisible();
    const markReadBtn = welcomeItem.getByRole("button", {
      name: /mark .* as read/i,
    });
    await expect(markReadBtn).toBeVisible();

    // Act: click the mark-as-read button.
    await markReadBtn.click();

    // After mutation the query is invalidated and the feed re-fetches.
    // "New" badge must be gone.
    await expect(welcomeItem.getByText("New")).not.toBeVisible();

    // "Mark as read" button must be gone (hidden when readAt is set).
    await expect(markReadBtn).not.toBeVisible();
  });

  test("read state reflected on dashboard after mark-read", async ({
    page,
  }) => {
    // NOTIF-04
    await registerFreshUser(page);
    await page.goto("/notifications");

    const welcomeItem = page
      .locator("ul li")
      .filter({ hasText: "Welcome to the platform!" })
      .first();

    // The welcome notification is created asynchronously by the worker — poll with reload
    // until it appears (the same robust pattern the other notification tests use).
    await expect(async () => {
      await page.reload();
      await expect(welcomeItem).toBeVisible({ timeout: 3_000 });
    }).toPass({ timeout: 30_000, intervals: [1_000, 2_000] });
    await expect(welcomeItem.getByText("New")).toBeVisible();

    // Mark as read on the /notifications page.
    const markReadBtn = welcomeItem.getByRole("button", {
      name: /mark .* as read/i,
    });
    await markReadBtn.click();
    // Wait for the badge to disappear before navigating — confirms the mutation completed.
    await expect(welcomeItem.getByText("New")).not.toBeVisible();

    // Navigate to the dashboard.
    await page.goto("/");

    // The "Recent notifications" card on the dashboard.
    // The card is a section with a CardTitle "Notifications" inside it.
    const notifCard = page
      .locator('[class*="card"]')
      .filter({ hasText: "Notifications" })
      .first();

    // Within the card, the welcome item should exist but WITHOUT a "New" badge.
    const dashWelcomeItem = notifCard
      .locator("li")
      .filter({ hasText: "Welcome to the platform!" })
      .first();

    await expect(dashWelcomeItem).toBeVisible({ timeout: 10_000 });
    await expect(dashWelcomeItem.getByText("New")).not.toBeVisible();

    // Mark-as-read button also absent.
    await expect(
      dashWelcomeItem.getByRole("button", { name: /mark .* as read/i }),
    ).not.toBeVisible();
  });

  test("pagination absent for single-page feed", async ({ page }) => {
    // NOTIF-09 — a fresh user has at most 1 notification (welcome), well under PAGE_SIZE=20.
    await registerFreshUser(page);
    await page.goto("/notifications");

    // Wait for the feed to settle (either list or empty state, not skeleton).
    await expect(
      page.locator("ul, [data-slot='empty-state'], [class*='empty']").first(),
    ).toBeVisible({ timeout: 15_000 });

    // Pagination navigation must not be present.
    await expect(page.getByRole("navigation", { name: /pagination/i })).not.toBeVisible();
    // Also check that the "Previous" and "Next" links are absent.
    await expect(page.getByRole("link", { name: /previous/i })).not.toBeVisible();
    await expect(page.getByRole("link", { name: /next/i })).not.toBeVisible();
  });
});

// ---------------------------------------------------------------------------
// Security: unauthenticated redirect
// ---------------------------------------------------------------------------

test.describe("Notifications — unauthenticated", () => {
  test.use(ANONYMOUS);

  test("unauthenticated redirect to login", async ({ page }) => {
    // NOTIF-13
    await page.goto("/notifications");
    // The app must redirect to /login (not serve a 401 JSON).
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });
});

// ---------------------------------------------------------------------------
// Security: tokens not in JS storage
// ---------------------------------------------------------------------------

test.describe("Notifications — token hygiene", () => {
  test("session tokens not in JS storage", async ({ page }) => {
    // NOTIF-15 — uses the shared primary session (already authenticated).
    await page.goto("/notifications");

    // Inspect localStorage, sessionStorage and document.cookie from the page context.
    const result = await page.evaluate(() => {
      const lsKeys = Object.keys(localStorage);
      const ssKeys = Object.keys(sessionStorage);
      const cookieStr = document.cookie; // only readable (non-httpOnly) cookies visible here
      return { lsKeys, ssKeys, cookieStr };
    });

    // Neither storage should contain anything that looks like a JWT or refresh token.
    const tokenPattern = /token|jwt|bearer|refresh/i;
    const lsTokens = result.lsKeys.filter((k) => tokenPattern.test(k));
    const ssTokens = result.ssKeys.filter((k) => tokenPattern.test(k));
    expect(lsTokens).toHaveLength(0);
    expect(ssTokens).toHaveLength(0);

    // The session cookie (httpOnly) must NOT appear in document.cookie.
    // Only the CSRF token (mp_csrf) is allowed as a readable cookie.
    // We verify by checking that any visible cookie is NOT a JWT (no three-segment base64url).
    const jwtPattern = /eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/;
    expect(result.cookieStr).not.toMatch(jwtPattern);
  });
});
