import { test, expect } from "@playwright/test";
import { ANONYMOUS, registerFreshUser } from "./helpers";

// Most tests reuse the primary authenticated session (default storageState from setup).
// Tests that need isolation (fresh user without displayName, unauthenticated) opt out below.

test.describe("Account Profile — /account/profile", () => {
  // ---------------------------------------------------------------------------
  // PROF-07: Sidebar Profile link
  // ---------------------------------------------------------------------------
  test("sidebar Profile link navigates to profile page", async ({ page }) => {
    await page.goto("/");

    const sidebar = page.locator('[data-slot="sidebar"]').first();
    await sidebar.getByRole("link", { name: "Profile", exact: true }).click();

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
  test("profile page shows editable display name field", async ({ page }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    const displayNameInput = page.locator("#profile-display-name");
    await expect(displayNameInput).toBeVisible();
    await expect(displayNameInput).toBeEnabled();

    // Primary user was registered with displayName "E2E Primary" in auth.setup.ts
    const value = await displayNameInput.inputValue();
    expect(value).toBe("E2E Primary");
  });

  // ---------------------------------------------------------------------------
  // PROF-03: Locale field renders as editable select
  // ---------------------------------------------------------------------------
  test("profile page shows editable locale select", async ({ page }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    const localeTrigger = page.locator("#profile-locale");
    await expect(localeTrigger).toBeVisible();
    await expect(localeTrigger).toBeEnabled();
    await expect(localeTrigger).toHaveText(/en|cs/);
  });

  // ---------------------------------------------------------------------------
  // PROF-04: Email read-only hint is displayed
  // ---------------------------------------------------------------------------
  test("profile page explains email is read-only", async ({ page }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    await expect(page.getByText(/Changing your email requires/i)).toBeVisible();
  });

  // ---------------------------------------------------------------------------
  // PROF-12: Save button is present and disabled until the form is dirty
  // ---------------------------------------------------------------------------
  test("profile save button is disabled until a field changes", async ({ page }) => {
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    const save = page.getByRole("button", { name: /save changes/i });
    await expect(save).toBeVisible();
    await expect(save).toBeDisabled();

    await page.locator("#profile-display-name").fill("E2E Primary Updated");
    await expect(save).toBeEnabled();
  });

  // ---------------------------------------------------------------------------
  // PROF-15/16/17: Editable profile update flow
  // ---------------------------------------------------------------------------
  test("profile edit saves display name and survives reload", async ({ page }) => {
    await registerFreshUser(page, { displayName: "Before Profile Save" });
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    const displayName = page.locator("#profile-display-name");
    await expect(displayName).toHaveValue("Before Profile Save");

    await displayName.fill("After Profile Save");
    await page.getByRole("button", { name: /save changes/i }).click();

    await expect(page.getByText(/Profile updated/i)).toBeVisible();
    await page.reload();
    await expect(page.locator("#profile-display-name")).toHaveValue("After Profile Save");
  });

  test("profile save error shows toast and keeps form editable", async ({ page }) => {
    await registerFreshUser(page, { displayName: "Before Failed Profile Save" });
    await page.goto("/account/profile");
    await expect(page.getByRole("heading", { name: /profile/i })).toBeVisible();

    await page.route(/\/api\/bff\/identity\/users\/me$/, async (route) => {
      if (route.request().method() !== "PATCH") {
        await route.fallback();
        return;
      }

      await route.fulfill({
        status: 500,
        contentType: "application/problem+json",
        body: JSON.stringify({
          status: 500,
          errorCode: "profile.update_failed",
          detail: "Profile update failed.",
        }),
      });
    });

    const displayName = page.locator("#profile-display-name");
    await expect(displayName).toHaveValue("Before Failed Profile Save");

    await displayName.fill("After Failed Profile Save");
    const save = page.getByRole("button", { name: /save changes/i });
    await expect(save).toBeEnabled();
    await save.click();

    await expect(page.getByText(/Profile update failed/i)).toBeVisible();
    await expect(displayName).toBeEnabled();
    await expect(displayName).toHaveValue("After Failed Profile Save");
    await expect(save).toBeEnabled();
  });

  // ---------------------------------------------------------------------------
  // PROF-05: Locale toggle in topbar updates locale field
  // ---------------------------------------------------------------------------
  test("locale toggle switches the UI language", async ({ page }) => {
    // The locale toggle changes the UI LANGUAGE (NEXT_LOCALE cookie). Stored profile locale
    // changes through the editable profile form above.
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
  // The current helper always creates a user with a display name, so this test
  // asserts the stable placeholder contract. A full null-name rendering path
  // needs a dedicated backend fixture or direct API setup.
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
