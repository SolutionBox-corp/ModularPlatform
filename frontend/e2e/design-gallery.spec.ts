import { test, expect } from "@playwright/test";

/**
 * Design Gallery (/design) — E2E spec.
 *
 * The /design route is a standalone dev-only page (no AppShell / auth guard).
 * It is accessible to authenticated AND anonymous users. All tests here run
 * authenticated (default storageState) because that is the realistic usage;
 * anonymous access is trivially equivalent since there is no auth gate.
 *
 * Dark-mode toggle: the page has NO ThemeToggle button of its own (it lives in
 * the shell topbar which is absent on this route). We drive theme changes via
 * localStorage — next-themes reads `localStorage.theme` on mount and applies
 * the `dark` class to <html>.
 */

// ── DES-17 / DES-01 · No uncaught console errors on load ────────────────────

test.describe("renders without uncaught console errors", () => {
  test("DES-17 / DES-01 · no pageerror or uncaught console error on /design", async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", (err) => errors.push(err.message));

    await page.goto("/design");

    // Sticky header title
    await expect(page.getByRole("heading", { name: "Design Gallery", exact: true })).toBeVisible();

    // Badge in header
    await expect(page.getByText("dev only")).toBeVisible();

    expect(errors).toHaveLength(0);
  });
});

// ── DES-02 · All 25 section headings visible ────────────────────────────────

test.describe("all section headings are visible", () => {
  test("DES-02 · every numbered section heading is present", async ({ page }) => {
    await page.goto("/design");
    // Wait for hydration by checking the first section before looping
    await expect(page.getByRole("heading", { name: /1 · Design Tokens/i })).toBeVisible();

    const sections = [
      "1 · Design Tokens",
      "2 · Button",
      "3 · Badge",
      "4 · Avatar",
      "5 · Form Inputs",
      "6 · Card",
      "7 · Alert",
      "8 · Tabs",
      "9 · Progress",  // "9 · Progress & Skeleton" — substring match is fine
      "10 · Tooltip",
      "11 · Dropdown Menu",
      "12 · Popover",
      "13 · Dialog",
      "14 · Sheet",
      "15 · Scroll Area",
      "16 · Separator",
      "17 · Toast",  // "17 · Toast (Sonner)"
      "18 · DataTable",
      "19 · EmptyState",
      "20 · MoneyAmount",
      "21 · ProblemDetails",
      "22 · OperationStatus",
      "23 · RealtimeIndicator",
      "24 · Composed Form Pattern",
      "25 · Dark Mode",
    ];

    for (const title of sections) {
      await expect(
        page.getByRole("heading", { name: title }),
        `Expected section "${title}" to be visible`,
      ).toBeVisible();
    }
  });

  // DES-09 / DES-10 / DES-12 / DES-13 / DES-14 / DES-21 / DES-25 — spot-checks
  // inside specific sections (all drive visibility, so grouped here).

  test("DES-09 · DataTable with-data sub-section shows sample rows", async ({ page }) => {
    await page.goto("/design");
    await expect(page.getByRole("cell", { name: "Alice Johnson" })).toBeVisible();
    await expect(page.getByRole("cell", { name: "Bob Smith" })).toBeVisible();
    // Pagination control present (total=42, pageSize=5); use exact aria-label to avoid matching the Next.js dev tools button
    await expect(page.getByRole("button", { name: "Go to next page" })).toBeVisible();
  });

  test("DES-10 · DataTable empty-state shows 'No users found'", async ({ page }) => {
    await page.goto("/design");
    await expect(page.getByText("No users found")).toBeVisible();
  });

  test("DES-12 · MoneyAmount renders credit and fiat values", async ({ page }) => {
    await page.goto("/design");
    // Scope to the #money section to avoid strict-mode violations from the DataTable rows
    const moneySection = page.locator("#money");
    // Credits: 0 cr. and 1,250 cr. are in the section
    await expect(moneySection.getByLabel("0 cr.", { exact: true })).toBeVisible();
    // Fiat: $9.99 (USD, 2 decimal places via Intl.NumberFormat)
    await expect(moneySection.getByLabel("$9.99")).toBeVisible();
  });

  test("DES-13 · ProblemDetails renders known and generic error panels", async ({ page }) => {
    await page.goto("/design");
    // user.email_taken is mapped to "That email is already registered." by the error catalog
    await expect(page.getByText("That email is already registered.")).toBeVisible();
    // new Error("Internal error") is not an ApiError → falls back to the generic catalog entry
    await expect(page.getByText("Something went wrong. Please try again.")).toBeVisible();
  });

  test("DES-14 · OperationStatus panels show all five states", async ({ page }) => {
    await page.goto("/design");
    for (const status of ["Pending", "Running", "Completed", "Failed", "Cancelled"]) {
      await expect(
        page.getByLabel(`Operation: ${status}`),
        `Expected operation status panel "${status}"`,
      ).toBeVisible();
    }
  });

  test("DES-21 · Scroll area renders with scrollable content", async ({ page }) => {
    await page.goto("/design");
    await expect(page.getByText("Item 1 — scrollable content row")).toBeVisible();
  });

  test("DES-25 · RealtimeIndicator section shows all three static states", async ({ page }) => {
    await page.goto("/design");
    // Scope to the #realtime-indicator section to avoid strict-mode violations —
    // "Live" also appears as a substring in surrounding paragraph text on the page.
    const section = page.locator("#realtime-indicator");
    await expect(section.getByText("Live", { exact: true })).toBeVisible();
    await expect(section.getByText("Connecting", { exact: true })).toBeVisible();
    await expect(section.getByText("Offline", { exact: true })).toBeVisible();
  });
});

// ── DES-15 / DES-16 · Dark-mode class flips on theme change ─────────────────

test.describe("dark-mode class flips on theme change", () => {
  test("DES-15 · setting localStorage.theme='dark' adds html.dark on reload", async ({ page }) => {
    // Start in a known light state
    await page.goto("/design");
    // Force light first so the state is deterministic
    await page.evaluate(() => localStorage.setItem("theme", "light"));
    await page.reload();
    await expect(page.locator("html")).not.toHaveClass(/\bdark\b/);

    // Switch to dark
    await page.evaluate(() => localStorage.setItem("theme", "dark"));
    await page.reload();
    // next-themes applies 'dark' to <html>
    await expect(page.locator("html")).toHaveClass(/\bdark\b/);
  });

  test("DES-16 · gallery remains functional in dark mode (no crash)", async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", (err) => errors.push(err.message));

    // Must navigate to the app origin BEFORE accessing localStorage to avoid a
    // SecurityError when Playwright starts on about:blank.
    await page.goto("/design");
    await page.evaluate(() => localStorage.setItem("theme", "dark"));
    await page.reload();

    // The page must still render its heading without errors
    await expect(page.getByRole("heading", { name: "Design Gallery", exact: true })).toBeVisible();
    expect(errors).toHaveLength(0);

    // Clean up so other tests start in light
    await page.evaluate(() => localStorage.setItem("theme", "light"));
  });
});

// ── DES-05 / DES-18 / DES-19 / DES-20 / DES-11 · Interactive controls ───────

test.describe("interactive controls respond", () => {
  test("DES-05 · Switch in Form Inputs section toggles aria-checked", async ({ page }) => {
    await page.goto("/design");
    // The controlled switch is labelled via the label text "Off" / "On"
    const sw = page.locator('[data-slot="switch"]').first();
    await expect(sw).toHaveAttribute("aria-checked", "false");
    await sw.click();
    await expect(sw).toHaveAttribute("aria-checked", "true");
  });

  test("DES-06 · clicking Success toast button shows a toast", async ({ page }) => {
    await page.goto("/design");
    // Scroll to section 17 to ensure the button is in view
    await page.getByRole("heading", { name: "17 · Toast" }).scrollIntoViewIfNeeded();
    await page.getByRole("button", { name: "Success" }).click();
    await expect(page.getByText("Operation completed successfully.")).toBeVisible();
  });

  test("DES-07 / DES-22 · dialog opens via button click and closes via Escape", async ({ page }) => {
    await page.goto("/design");
    await page.getByRole("heading", { name: "13 · Dialog" }).scrollIntoViewIfNeeded();
    await page.getByRole("button", { name: "Open dialog" }).click();
    // Dialog title must be visible
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Confirm action" })).toBeVisible();
    // Close with Escape
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).not.toBeVisible();
  });

  test("DES-08 · right sheet opens and closes via Escape", async ({ page }) => {
    await page.goto("/design");
    await page.getByRole("heading", { name: "14 · Sheet" }).scrollIntoViewIfNeeded();
    await page.getByRole("button", { name: "Right sheet" }).click();
    await expect(page.getByRole("heading", { name: "Right sheet" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("heading", { name: "Right sheet" })).not.toBeVisible();
  });

  test("DES-11 · EmptyState action button fires upload toast", async ({ page }) => {
    await page.goto("/design");
    await page.getByRole("heading", { name: "19 · EmptyState" }).scrollIntoViewIfNeeded();
    await page.getByRole("button", { name: "Upload a file" }).click();
    await expect(page.getByText("Upload triggered")).toBeVisible();
  });

  test("DES-18 · Tabs switch content panel", async ({ page }) => {
    await page.goto("/design");
    await page.getByRole("heading", { name: "8 · Tabs" }).scrollIntoViewIfNeeded();
    // Click the Activity tab
    await page.getByRole("tab", { name: "Activity" }).first().click();
    await expect(page.getByText("Activity feed goes here.")).toBeVisible();
  });

  test("DES-19 · Dropdown menu opens on trigger click", async ({ page }) => {
    await page.goto("/design");
    await page.getByRole("heading", { name: "11 · Dropdown Menu" }).scrollIntoViewIfNeeded();
    // Scope to the #dropdown section to avoid strict-mode violations — the page has
    // multiple buttons and the unscoped /actions/i pattern may match more than one.
    await page.locator("#dropdown").getByRole("button", { name: /actions/i }).click();
    await expect(page.getByRole("menuitem", { name: "Profile" })).toBeVisible();
    await expect(page.getByRole("menuitem", { name: "Settings" })).toBeVisible();
    await expect(page.getByRole("menuitem", { name: "Sign out" })).toBeVisible();
  });

  test("DES-20 · Popover opens on trigger click", async ({ page }) => {
    await page.goto("/design");
    await page.getByRole("heading", { name: "12 · Popover" }).scrollIntoViewIfNeeded();
    await page.getByRole("button", { name: "Open popover" }).click();
    await expect(page.getByText("Popover title")).toBeVisible();
  });
});
