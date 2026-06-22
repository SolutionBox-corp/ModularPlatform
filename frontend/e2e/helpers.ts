import { expect, type Page } from "@playwright/test";

/**
 * Shared E2E helpers. Registration is self-serve: a fresh email provisions its own tenant
 * with default module entitlements (billing/files/notifications/operations/gdpr), so each
 * fresh user is an isolated workspace — ideal for destructive tests (erase) and auth flows.
 */

export const PASSWORD = "E2ePass123!secure";

/** A unique, valid e-mail for a throwaway account. */
export function uniqueEmail(prefix = "e2e"): string {
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@test.local`;
}

/** Register a brand-new user through the UI and land authenticated on the dashboard. */
export async function registerFreshUser(
  page: Page,
  opts: { email?: string; displayName?: string } = {},
): Promise<{ email: string }> {
  const email = opts.email ?? uniqueEmail();
  // Drop any inherited session (the chromium project loads the primary user's storageState);
  // otherwise the (auth) layout would redirect an authenticated visitor away from /register.
  await page.context().clearCookies();
  await page.goto("/register");
  await page.getByRole("textbox", { name: "Email" }).fill(email);
  const name = page.getByRole("textbox", { name: /display name/i });
  if (await name.count()) await name.fill(opts.displayName ?? "E2E User");
  await page.getByRole("textbox", { name: "Password" }).fill(PASSWORD);
  // Base UI puts the id on a hidden native input and the accessible name is unreliable
  // (the label wraps Terms/Privacy links), so click the visible checkbox button by its slot.
  // Check the terms box AND use it as a hydration gate: a pre-hydration click is a no-op
  // (and a pre-hydration submit does a native GET that leaks fields into the URL). Once the
  // box reflects aria-checked=true, React has hydrated, so the submit runs the JS handler.
  const terms = page.locator('[data-slot="checkbox"]');
  await expect(async () => {
    if ((await terms.getAttribute("aria-checked")) === "true") return;
    await terms.click();
    await expect(terms).toHaveAttribute("aria-checked", "true", { timeout: 2_000 });
  }).toPass({ timeout: 20_000 });
  await page.getByRole("button", { name: /create account/i }).click();
  await expect(page).toHaveURL("/", { timeout: 20_000 });
  return { email };
}

/** Log in an existing user through the UI. */
export async function login(page: Page, email: string, password = PASSWORD): Promise<void> {
  // Start from a clean session so /login isn't redirected away for an already-authenticated visitor.
  await page.context().clearCookies();
  await page.goto("/login");
  await page.getByRole("textbox", { name: "Email" }).fill(email);
  await page.getByRole("textbox", { name: "Password" }).fill(password);
  await page.getByRole("button", { name: /sign in/i }).click();
  await expect(page).toHaveURL("/", { timeout: 20_000 });
}

/** Start a spec UNAUTHENTICATED (ignore the saved primary storageState). */
export const ANONYMOUS = { storageState: { cookies: [], origins: [] } };
