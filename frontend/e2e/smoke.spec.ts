import { test, expect } from "@playwright/test";

/** Sanity: the saved primary session lands on an authenticated dashboard with the app shell. */
test("authenticated dashboard renders with nav", async ({ page }) => {
  await page.goto("/");
  const nav = page.getByRole("navigation").or(page.locator('[data-slot="sidebar"]')).first();
  await expect(page.getByRole("heading", { name: /welcome back/i })).toBeVisible();
  await expect(nav.getByRole("link", { name: "Dashboard", exact: true })).toBeVisible();
  await expect(nav.getByRole("link", { name: "Billing", exact: true })).toBeVisible();
  await expect(nav.getByRole("link", { name: "Files", exact: true })).toBeVisible();
});
