import { test, expect, type Page } from "@playwright/test";
import { registerFreshUser } from "./helpers";

async function startDemoOperation(page: Page): Promise<string> {
  return page.evaluate(async () => {
    const csrf = document.cookie
      .split("; ")
      .find((part) => part.startsWith("mp_csrf="))
      ?.split("=")[1];

    const response = await fetch("/api/bff/operations/demo", {
      method: "POST",
      credentials: "same-origin",
      headers: {
        accept: "application/json",
        ...(csrf ? { "x-csrf-token": decodeURIComponent(csrf) } : {}),
      },
    });

    if (!response.ok) {
      throw new Error(`Failed to start demo operation: ${response.status}`);
    }

    const json = (await response.json()) as { data: { operationId: string } };
    return json.data.operationId;
  });
}

test.describe("Operations page", () => {
  test("sidebar exposes Operations link", async ({ page }) => {
    await page.goto("/");

    const sidebar = page.locator('[data-slot="sidebar"]').first();
    await expect(
      sidebar.getByRole("link", { name: "Operations", exact: true }),
    ).toBeVisible();
  });

  test("fresh account shows empty operations history", async ({ page }) => {
    await registerFreshUser(page);
    await page.goto("/operations");

    await expect(
      page.getByRole("heading", { name: "Operations", level: 1 }),
    ).toBeVisible();
    await expect(page.getByText("No operations yet")).toBeVisible();
    await expect(
      page.getByText(/Long-running imports, exports, and background tasks/i),
    ).toBeVisible();
  });

  test("started demo operation appears in operations history", async ({
    page,
  }) => {
    await registerFreshUser(page);
    await page.goto("/");

    const operationId = await startDemoOperation(page);
    await page.goto("/operations");

    const row = page.getByRole("row", { name: new RegExp(operationId, "i") });
    await expect(row).toBeVisible({ timeout: 10_000 });
    await expect(row.getByText("Demo")).toBeVisible();
    await expect(row.getByText(/Pending|Running|Completed|Failed/)).toBeVisible();
  });
});
