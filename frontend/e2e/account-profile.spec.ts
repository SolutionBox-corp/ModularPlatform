import { test, expect } from "@playwright/test";
import { ANONYMOUS } from "./helpers";

// Most tests reuse the primary authenticated session (default storageState from setup).
// Tests that need isolation (fresh user without displayName, unauthenticated) opt out below.

test.describe("Account Profile — /account/profile", () => {
  // ---------------------------------------------------------------------------
  // PROF-07: User menu Profile link
  // ---------------------------------------------------------------------------
  test("user menu Profile link navigates to profile page", async ({ page }) => {
    await page.goto("/");

    // Open the user menu trigger (aria-label from AppShell)
    const userMenuTrigger = page.getByRole("button", { name: "User menu" });
    await userMenuTrigger.click();

    // Click the Profile item inside the dropdown
    const profileItem = page.getByRole("menuitem", { name: /^Profile$/i });
    await profileItem.click();

    await expect(page).toHaveURL("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();
  });

  // ---------------------------------------------------------------------------
  // PROF-01: Email field is visible and read-only
  // ---------------------------------------------------------------------------
  test("profile page shows email in read-only field", async ({ page }) => {
    await page.goto("/account/profile");

    // Wait for the page heading to confirm the page rendered
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    // Email input: rendered with id="profile-email", type="email", readOnly + disabled
    const emailInput = page.locator("#profile-email");
    await expect(emailInput).toBeVisible();
    await expect(emailInput).toBeDisabled();

    // Value must be a non-empty string that looks like an email
    const value = await emailInput.inputValue();
    expect(value).toMatch(/@/);
    expect(value.length).toBeGreaterThan(4);
  });

  // ---------------------------------------------------------------------------
  // PROF-02: Display name field — primary user registered with "E2E Primary"
  // ---------------------------------------------------------------------------
  test("profile page shows display name in read-only field", async ({ page }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    const displayNameInput = page.locator("#profile-display-name");
    await expect(displayNameInput).toBeVisible();
    await expect(displayNameInput).toBeDisabled();

    // Primary user was registered with displayName "E2E Primary" in auth.setup.ts
    const value = await displayNameInput.inputValue();
    expect(value).toBe("E2E Primary");
  });

  // ---------------------------------------------------------------------------
  // PROF-03: Locale field renders and is read-only
  // ---------------------------------------------------------------------------
  test("profile page shows locale in read-only field", async ({ page }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    const localeInput = page.locator("#profile-locale");
    await expect(localeInput).toBeVisible();
    await expect(localeInput).toBeDisabled();

    // Locale must be a non-empty string (e.g. "en" or "cs")
    const value = await localeInput.inputValue();
    expect(value.length).toBeGreaterThan(0);
  });

  // ---------------------------------------------------------------------------
  // PROF-04: Read-only info alert is displayed
  // ---------------------------------------------------------------------------
  test("profile page shows read-only notice", async ({ page }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    // The Alert contains a sentence about editing not being available
    await expect(
      page.getByText(/profile editing is not yet available/i),
    ).toBeVisible();
  });

  // ---------------------------------------------------------------------------
  // PROF-12: No submit / Save button present (update not yet built)
  // ---------------------------------------------------------------------------
  test("profile page has no submit button (update not yet available)", async ({
    page,
  }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    // Confirm absolutely no submit-type or Save-labeled button is rendered on the card
    const submitButton = page.getByRole("button", { name: /save|submit|update/i });
    await expect(submitButton).toHaveCount(0);
  });

  // ---------------------------------------------------------------------------
  // PROF-05: Locale toggle in topbar updates locale field
  // ---------------------------------------------------------------------------
  test("locale toggle switches the UI language", async ({ page }) => {
    // The locale toggle changes the UI LANGUAGE (NEXT_LOCALE cookie) — NOT the user's stored
    // profile locale (which comes from /me and has no update endpoint). Verify the nav relabels.
    await page.goto("/account/profile");
    const nav = page.locator('[data-slot="sidebar"]').first();
    await expect(nav.getByRole("link", { name: "Dashboard", exact: true })).toBeVisible();

    // Switch to Czech.
    await page.getByRole("button", { name: /switch language|přepnout jazyk/i }).click();
    await page.getByRole("menuitem", { name: "Čeština" }).click();

    // After the reload, the Dashboard nav label is the Czech "Přehled".
    await expect(nav.getByRole("link", { name: "Přehled", exact: true })).toBeVisible({ timeout: 15_000 });

    // Restore English so subsequent specs see a predictable locale.
    await page.getByRole("button", { name: /switch language|přepnout jazyk/i }).click();
    await page.getByRole("menuitem", { name: "English" }).click();
    await expect(nav.getByRole("link", { name: "Dashboard", exact: true })).toBeVisible({ timeout: 15_000 });
  });

  // ---------------------------------------------------------------------------
  // PROF-14 (partial): Fields have accessible labels via id pairing and aria-label
  // ---------------------------------------------------------------------------
  test("profile fields have aria-label attributes", async ({ page }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    // Each input carries an aria-label set directly in the component
    await expect(page.locator("#profile-email")).toHaveAttribute(
      "aria-label",
      "Email address",
    );
    await expect(page.locator("#profile-display-name")).toHaveAttribute(
      "aria-label",
      "Display name",
    );
    await expect(page.locator("#profile-locale")).toHaveAttribute(
      "aria-label",
      "Locale",
    );
  });

  // ---------------------------------------------------------------------------
  // PROF-11: Display name shows placeholder when not set
  // Register a fresh user WITHOUT a displayName option (registerFreshUser falls
  // back to "E2E User", so skip the displayName arg entirely and check the
  // actual backend response: if displayName is null the input value is "" and
  // the placeholder "Not set" is visible in the DOM attribute.
  // We use a fresh user registered with an explicit empty displayName via direct
  // API bypass — however, registerFreshUser always fills "E2E User" when no
  // displayName is given. So we rely on a known behaviour: if the backend stores
  // null the component renders value="" and the <input placeholder="Not set">.
  //
  // Since we CANNOT register without a name via the helper, we assert the
  // placeholder attribute exists on the element (component sets it unconditionally)
  // and note that verifying the null-name rendering path requires a dedicated
  // backend user fixture (marked as partial automation below).
  // ---------------------------------------------------------------------------
  test("profile display name input has 'Not set' placeholder attribute", async ({
    page,
  }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    // The placeholder is always rendered even when the user has a name — the
    // component sets placeholder="Not set" unconditionally (visible when value="").
    await expect(page.locator("#profile-display-name")).toHaveAttribute(
      "placeholder",
      "Not set",
    );
  });
});

// ---------------------------------------------------------------------------
// PROF-08: Unauthenticated access redirects to /login (opt out of saved auth)
// ---------------------------------------------------------------------------
test.describe("Account Profile — unauthenticated", () => {
  test.use(ANONYMOUS);

  test("unauthenticated visit to /account/profile redirects to login", async ({
    page,
  }) => {
    await page.goto("/account/profile");
    // The tenant layout server-redirects to /login immediately
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });
});

// ---------------------------------------------------------------------------
// PROF-09: Session token not exposed to JavaScript
// ---------------------------------------------------------------------------
test.describe("Account Profile — security: token isolation", () => {
  test("session token is not exposed to JavaScript on profile page", async ({
    page,
  }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    // localStorage must contain no token-shaped keys
    const localStorageKeys = await page.evaluate(() =>
      Object.keys(window.localStorage),
    );
    const sessionStorageKeys = await page.evaluate(() =>
      Object.keys(window.sessionStorage),
    );
    const tokenPatterns = /token|session|jwt|auth|access|refresh/i;
    expect(localStorageKeys.filter((k) => tokenPatterns.test(k))).toHaveLength(0);
    expect(sessionStorageKeys.filter((k) => tokenPatterns.test(k))).toHaveLength(0);

    // document.cookie must NOT expose the httpOnly session cookie (it is httpOnly,
    // so the browser never sends it to JS). Only the readable mp_csrf cookie (if
    // present) or locale/theme cookies are acceptable.
    const cookies = await page.evaluate(() => document.cookie);
    // The session cookie name contains "session" — must not appear in readable cookies
    expect(cookies).not.toMatch(/session[^=]*=/i);
    // No raw JWT (starts with "eyJ") must appear in readable cookies
    expect(cookies).not.toMatch(/eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/);
  });
});
