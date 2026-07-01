/**
 * Authentication E2E — logged-out flows.
 * All tests here run UNAUTHENTICATED (ANONYMOUS) or register a fresh isolated user via
 * registerFreshUser(). Never relies on the shared primary storageState so it can freely
 * test register/login/logout without corrupting the suite.
 *
 * Catalog reference: docs/test-scenarios/auth.md
 */
import { test, expect } from "@playwright/test";
import {
  ANONYMOUS,
  PASSWORD,
  login,
  registerFreshUser,
  uniqueEmail,
} from "./helpers";

// ---------------------------------------------------------------------------
// Register
// ---------------------------------------------------------------------------
test.describe("register", () => {
  test.use(ANONYMOUS);

  test("happy path: fresh email lands on dashboard", async ({ page }) => {
    // AUTH-01
    await registerFreshUser(page);
    // After registration we should be on "/" with the app shell visible.
    await expect(page).toHaveURL("/");
    await expect(
      page.getByRole("heading", { name: /welcome back/i }),
    ).toBeVisible();
  });

  test("validation: empty submit shows inline errors", async ({ page }) => {
    // AUTH-03
    await page.goto("/register");
    // Click submit without filling anything; wait for hydration by checking the button text.
    await expect(
      page.getByRole("button", { name: /create account/i }),
    ).toBeVisible();
    await page.getByRole("button", { name: /create account/i }).click();

    // Email error
    await expect(page.getByText("Email is required")).toBeVisible();
    // Password error (client-side min-length fires because value is empty → "required" OR min-length)
    await expect(
      page.getByText(/password must be at least 8 characters|password is required/i),
    ).toBeVisible();
    // Terms error
    await expect(
      page.getByText(/you must accept the terms/i),
    ).toBeVisible();
    // Still on /register
    await expect(page).toHaveURL("/register");
  });

  test("validation: invalid email format", async ({ page }) => {
    // AUTH-04
    await page.goto("/register");
    await page.getByRole("textbox", { name: "Email" }).fill("not-an-email");
    await page.getByRole("textbox", { name: "Password" }).fill(PASSWORD);
    // Accept terms so the only blocking error is the email format
    const terms = page.locator('[data-slot="checkbox"]');
    await expect(async () => {
      if ((await terms.getAttribute("aria-checked")) === "true") return;
      await terms.click();
      await expect(terms).toHaveAttribute("aria-checked", "true", {
        timeout: 2_000,
      });
    }).toPass({ timeout: 20_000 });
    await page.getByRole("button", { name: /create account/i }).click();

    await expect(page.getByText("Enter a valid email")).toBeVisible();
    await expect(page).toHaveURL("/register");
  });

  test("validation: short password", async ({ page }) => {
    // AUTH-05
    await page.goto("/register");
    await page.getByRole("textbox", { name: "Email" }).fill(uniqueEmail());
    await page.getByRole("textbox", { name: "Password" }).fill("abc1234"); // 7 chars
    const terms = page.locator('[data-slot="checkbox"]');
    await expect(async () => {
      if ((await terms.getAttribute("aria-checked")) === "true") return;
      await terms.click();
      await expect(terms).toHaveAttribute("aria-checked", "true", {
        timeout: 2_000,
      });
    }).toPass({ timeout: 20_000 });
    await page.getByRole("button", { name: /create account/i }).click();

    await expect(
      page.getByText("Password must be at least 8 characters"),
    ).toBeVisible();
    await expect(page).toHaveURL("/register");
  });

  test("validation: terms not accepted blocks submit", async ({ page }) => {
    // AUTH-06
    await page.goto("/register");
    await page.getByRole("textbox", { name: "Email" }).fill(uniqueEmail());
    await page.getByRole("textbox", { name: "Password" }).fill(PASSWORD);
    // Deliberately do NOT check the Terms box.
    // Wait for the button to be clickable (hydration signal).
    const btn = page.getByRole("button", { name: /create account/i });
    await expect(btn).toBeVisible();
    await btn.click();

    await expect(
      page.getByText(/you must accept the terms/i),
    ).toBeVisible();
    await expect(page).toHaveURL("/register");
  });

  test("terms checkbox toggles with keyboard space", async ({ page }) => {
    // AUTH-21
    await page.goto("/register");

    const terms = page.locator('[data-slot="checkbox"]');
    await expect(terms).toBeVisible();
    await expect(terms).toHaveAttribute("aria-checked", "false");

    await terms.focus();
    await page.keyboard.press("Space");
    await expect(terms).toHaveAttribute("aria-checked", "true");

    await page.keyboard.press("Space");
    await expect(terms).toHaveAttribute("aria-checked", "false");
  });

  test("duplicate email shows field error", async ({ page }) => {
    // AUTH-07: register once, then attempt registration with the same email.
    const { email } = await registerFreshUser(page);

    // Navigate back to /register (we are now authenticated after the first reg;
    // the auth layout will bounce us back to /, so log out first via direct
    // session destruction: just use a second browser context via ANONYMOUS is simpler —
    // we replicate the form steps manually here on the already-logged-in page is
    // not possible because the layout redirects. Use page.goto with a forced
    // non-authenticated context isn't available mid-test.
    // Instead: open /register in the same page (which will redirect to /) then
    // we need another anonymous session. The cleanest approach: open a new page
    // with cleared storage state. We use page.context().newPage() with cleared cookies.
    // Actually simpler: just directly call the BFF via fetch from page context
    // to simulate the duplicate. We replicate the visible behavior the user sees.
    //
    // The recommended pattern: go to /register BEFORE any auth state.
    // Since registerFreshUser already logged us in, we open a fresh context.
    // In a serial workers:1 suite a BrowserContext trick isn't available from
    // within a test — so we POST directly to the BFF route to trigger the duplicate.
    // We validate that the form shows the error; the action is the same path.
    //
    // Simplest correct approach: navigate to /login, then navigate to /register —
    // the AuthLayout redirects an authenticated user to "/". Instead we rely on
    // the fact that the BFF action is what drives the error, so we re-test via
    // a new user that registers and then force an API duplicate via fetch in page.
    //
    // Cleanest: start fresh by calling the BFF REST endpoint that backs registerAction.
    // We verify the form shows the error by triggering the server action directly.
    // We do this by evaluating a fetch in the browser context (still on /).
    const dupeResponse = await page.evaluate(
      async ({ email, password }) => {
        const res = await fetch("/api/auth/register-check", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ email, password }),
        }).catch(() => null);
        // This internal endpoint may not exist; see NOTE below.
        return res ? res.status : null;
      },
      { email, password: PASSWORD },
    );
    // NOTE: if no BFF /api/auth/register-check route exists, skip the fetch and
    // instead verify the error via a second UI registration in a new unauthenticated
    // page. Since we can't easily get an unauthenticated page mid-test in workers:1,
    // we verify the behavior exists by checking that the same email attempt from
    // a fresh anonymous context (simulated via goto + clear cookies) shows the error.
    //
    // The approach below directly navigates: clear cookies to simulate logout, then
    // attempt registration with the duplicate email.
    void dupeResponse; // not relying on the speculative endpoint above

    // Clear the session so /register is accessible again.
    await page.context().clearCookies();

    await page.goto("/register");
    await page.getByRole("textbox", { name: "Email" }).fill(email);
    const nameField = page.getByRole("textbox", { name: /display name/i });
    if (await nameField.count()) await nameField.fill("Dupe Attempt");
    await page.getByRole("textbox", { name: "Password" }).fill(PASSWORD);
    const terms = page.locator('[data-slot="checkbox"]');
    await expect(async () => {
      if ((await terms.getAttribute("aria-checked")) === "true") return;
      await terms.click();
      await expect(terms).toHaveAttribute("aria-checked", "true", {
        timeout: 2_000,
      });
    }).toPass({ timeout: 20_000 });
    await page.getByRole("button", { name: /create account/i }).click();

    // Expect the field-level error for the duplicate email.
    await expect(
      page.getByText("That email is already registered."),
    ).toBeVisible();
    // Must remain on /register (no redirect happened).
    await expect(page).toHaveURL("/register");
  });
});

// ---------------------------------------------------------------------------
// Auth routes while already authenticated
// ---------------------------------------------------------------------------
test.describe("authenticated auth routes", () => {
  test("authenticated user visiting /register is redirected to dashboard", async ({ page }) => {
    // AUTH-02
    await page.goto("/register");

    await expect(page).toHaveURL("/", { timeout: 15_000 });
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /create account/i })).toHaveCount(0);
  });

  test("authenticated user visiting /login is redirected to dashboard", async ({ page }) => {
    // AUTH-09
    await page.goto("/login");

    await expect(page).toHaveURL("/", { timeout: 15_000 });
    await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /^sign in$/i })).toHaveCount(0);
  });
});

// ---------------------------------------------------------------------------
// Login
// ---------------------------------------------------------------------------
test.describe("login", () => {
  test.use(ANONYMOUS);

  test("happy path: correct credentials land on dashboard", async ({
    page,
  }) => {
    // AUTH-08: register a fresh user, then log in as them from scratch.
    const { email } = await registerFreshUser(page);
    // registerFreshUser lands on "/" authenticated; clear cookies to force re-login.
    await page.context().clearCookies();

    await login(page, email);
    await expect(page).toHaveURL("/");
    await expect(
      page.getByRole("heading", { name: /welcome back/i }),
    ).toBeVisible();
  });

  test("wrong password shows generic error", async ({ page }) => {
    // AUTH-10: no user enumeration — same message for wrong password as unknown email.
    const { email } = await registerFreshUser(page);
    await page.context().clearCookies();

    await page.goto("/login");
    await page.getByRole("textbox", { name: "Email" }).fill(email);
    await page.getByRole("textbox", { name: "Password" }).fill("WrongPass99!");
    await page.getByRole("button", { name: /sign in/i }).click();

    // The error is placed on the password field by the login form (error code auth.invalid_credentials).
    await expect(
      page.getByText("Incorrect email or password."),
    ).toBeVisible();
    await expect(page).toHaveURL("/login");
  });

  test("non-existent email shows same generic error (no enumeration)", async ({
    page,
  }) => {
    // AUTH-10b: unknown email → same message as wrong password.
    await page.goto("/login");
    await page
      .getByRole("textbox", { name: "Email" })
      .fill(uniqueEmail("unknown"));
    await page
      .getByRole("textbox", { name: "Password" })
      .fill("SomePass999!");
    await page.getByRole("button", { name: /sign in/i }).click();

    await expect(
      page.getByText("Incorrect email or password."),
    ).toBeVisible();
    await expect(page).toHaveURL("/login");
  });

  test("empty submit shows inline errors", async ({ page }) => {
    // AUTH-11
    await page.goto("/login");
    await expect(
      page.getByRole("button", { name: /sign in/i }),
    ).toBeVisible();
    await page.getByRole("button", { name: /sign in/i }).click();

    await expect(page.getByText("Email is required")).toBeVisible();
    await expect(page.getByText("Password is required")).toBeVisible();
    await expect(page).toHaveURL("/login");
  });

  test("invalid email format blocked client-side", async ({ page }) => {
    // AUTH-12
    await page.goto("/login");
    await page.getByRole("textbox", { name: "Email" }).fill("notanemail");
    await page.getByRole("textbox", { name: "Password" }).fill(PASSWORD);
    await page.getByRole("button", { name: /sign in/i }).click();

    await expect(page.getByText("Enter a valid email")).toBeVisible();
    await expect(page).toHaveURL("/login");
  });
});

// ---------------------------------------------------------------------------
// Logout
// ---------------------------------------------------------------------------
test.describe("logout", () => {
  test.use(ANONYMOUS);

  test("user menu sign-out redirects to login", async ({ page }) => {
    // AUTH-13: register a fresh user so we have an isolated session to destroy.
    await registerFreshUser(page);
    await expect(page).toHaveURL("/");

    // Open the user menu (DropdownMenuTrigger with aria-label "User menu")
    await page.getByRole("button", { name: "User menu" }).click();
    // Click the Sign out item
    await page.getByRole("menuitem", { name: /sign out/i }).click();

    // Expect redirect to /login
    await expect(page).toHaveURL("/login", { timeout: 20_000 });
  });

  test("protected route redirects after logout", async ({ page }) => {
    // AUTH-14: after sign-out, the protected "/" should bounce to /login.
    await registerFreshUser(page);
    await expect(page).toHaveURL("/");

    // Sign out via user menu
    await page.getByRole("button", { name: "User menu" }).click();
    await page.getByRole("menuitem", { name: /sign out/i }).click();
    await expect(page).toHaveURL("/login", { timeout: 20_000 });

    // Now try to visit a protected route directly.
    await page.goto("/");
    await expect(page).toHaveURL("/login");
  });
});

// ---------------------------------------------------------------------------
// Unauthenticated redirect
// ---------------------------------------------------------------------------
test.describe("unauthenticated", () => {
  test.use(ANONYMOUS);

  test("accessing protected route / redirects to /login", async ({ page }) => {
    // AUTH-15
    await page.goto("/");
    await expect(page).toHaveURL("/login");
  });

  test("accessing /billing redirects to /login", async ({ page }) => {
    // AUTH-16
    await page.goto("/billing");
    await expect(page).toHaveURL("/login");
  });

  test("redirect URL contains no leaking query params", async ({ page }) => {
    // AUTH-17: navigating to /billing unauthenticated → /login (no ?next= etc.)
    await page.goto("/billing");
    const url = new URL(page.url());
    // The pathname must be /login and there must be no query parameters.
    expect(url.pathname).toBe("/login");
    expect(url.search).toBe("");
  });
});

// ---------------------------------------------------------------------------
// Session security
// ---------------------------------------------------------------------------
test.describe("session security", () => {
  test.use(ANONYMOUS);

  test("tokens are httpOnly and not in JS storage", async ({ page }) => {
    // AUTH-18
    await registerFreshUser(page);
    await expect(page).toHaveURL("/");

    // Check that neither localStorage nor sessionStorage contain any token-like value.
    const localStorageKeys = await page.evaluate(() =>
      Object.keys(window.localStorage),
    );
    const sessionStorageKeys = await page.evaluate(() =>
      Object.keys(window.sessionStorage),
    );

    // No storage keys should look like auth tokens.
    const hasTokenInLocal = localStorageKeys.some(
      (k) =>
        /token|session|auth|access|refresh/i.test(k),
    );
    const hasTokenInSession = sessionStorageKeys.some(
      (k) =>
        /token|session|auth|access|refresh/i.test(k),
    );
    expect(hasTokenInLocal).toBe(false);
    expect(hasTokenInSession).toBe(false);

    // The httpOnly session cookie must NOT appear in document.cookie.
    const cookieString = await page.evaluate(() => document.cookie);
    // mp_session is httpOnly → not readable.
    expect(cookieString).not.toContain("mp_session");
    // mp_csrf IS readable (intentional, SOP-guarded double-submit).
    // We don't assert it must be present (it may not always be set in dev),
    // but we assert the session itself is hidden.
  });

  test("password never in URL after login", async ({ page }) => {
    // AUTH-19
    const { email } = await registerFreshUser(page);
    await page.context().clearCookies();

    await page.goto("/login");
    await page.getByRole("textbox", { name: "Email" }).fill(email);
    await page.getByRole("textbox", { name: "Password" }).fill(PASSWORD);
    await page.getByRole("button", { name: /sign in/i }).click();
    await expect(page).toHaveURL("/", { timeout: 20_000 });

    // The final URL (and any intermediate URL in the browser) must not contain the password.
    expect(page.url()).not.toContain(PASSWORD);
  });

  test("password never in URL after registration", async ({ page }) => {
    // AUTH-19 (registration variant)
    const email = uniqueEmail("sec");
    await page.goto("/register");
    await page.getByRole("textbox", { name: "Email" }).fill(email);
    const nameField = page.getByRole("textbox", { name: /display name/i });
    if (await nameField.count()) await nameField.fill("SecUser");
    await page.getByRole("textbox", { name: "Password" }).fill(PASSWORD);
    const terms = page.locator('[data-slot="checkbox"]');
    await expect(async () => {
      if ((await terms.getAttribute("aria-checked")) === "true") return;
      await terms.click();
      await expect(terms).toHaveAttribute("aria-checked", "true", {
        timeout: 2_000,
      });
    }).toPass({ timeout: 20_000 });
    await page.getByRole("button", { name: /create account/i }).click();
    await expect(page).toHaveURL("/", { timeout: 20_000 });

    // Password must not appear in the resulting URL.
    expect(page.url()).not.toContain(PASSWORD);
  });
});
