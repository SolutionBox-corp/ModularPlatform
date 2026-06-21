import { test, expect, type Page } from "@playwright/test";

// The shared primary storageState user is registered fresh per run against the live backend, where
// "marketing" is now a DEFAULT entitlement — so the primary user can see the Marketing nav + page.
// The pull pipeline + vibe agent run on the durable worker (fake gateways), so results arrive
// asynchronously; we poll with reloads (mirrors the backend integration tests' poll loops).

async function pollWithReload(
  page: Page,
  assertion: () => Promise<void>,
  { attempts = 20, delayMs = 1000 } = {},
): Promise<void> {
  for (let i = 0; i < attempts; i++) {
    try {
      await assertion();
      return;
    } catch {
      if (i === attempts - 1) throw new Error("pollWithReload exhausted");
      await page.waitForTimeout(delayMs);
      await page.reload();
    }
  }
}

test.describe("Marketing — nav + page structure", () => {
  test("marketing nav item is visible and the page renders its sections", async ({ page }) => {
    await page.goto("/");
    // Entitled → nav item present.
    await expect(page.getByRole("link", { name: "Marketing", exact: true })).toBeVisible();

    await page.goto("/marketing");
    await expect(page.getByRole("heading", { name: "Marketing", level: 1 })).toBeVisible();
    // The section titles are shadcn CardTitle <div>s (not heading roles) — assert by text.
    await expect(page.getByText("Data pulls", { exact: true })).toBeVisible();
    await expect(page.getByText("Metric snapshots", { exact: true })).toBeVisible();
    await expect(page.getByText("AI analyses", { exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Vibe assistant" })).toBeVisible();
  });
});

test.describe("Marketing — data pull pipeline", () => {
  test("triggering a GA4 pull produces metric snapshots", async ({ page }) => {
    await page.goto("/marketing");

    // Default source is ga4; just click the trigger.
    await page.getByRole("button", { name: "Pull data" }).click();
    await expect(page.getByText("Data pull started.")).toBeVisible();

    // The durable worker pulls (fake GA4) + persists snapshots; the table is invalidated via realtime.
    await pollWithReload(page, async () => {
      // The empty-state title must be gone, i.e. at least one snapshot row exists.
      await expect(page.getByText("No snapshots yet")).toHaveCount(0);
      await expect(page.getByRole("heading", { name: "Metric snapshots" })).toBeVisible();
    });
  });
});

test.describe("Marketing — vibe assistant chat", () => {
  test("starting a conversation and sending a message yields an assistant reply", async ({ page }) => {
    await page.goto("/marketing");

    await page.getByRole("button", { name: "New conversation" }).click();

    const input = page.getByPlaceholder("Type a message…");
    await expect(input).toBeVisible();
    await input.fill("How is my GA4 traffic trending this week?");
    // Submit via Enter (the composer's keydown handler). Avoids the Next.js dev-tools floating
    // button which overlaps the bottom-right Send icon in the dev server (not present in prod).
    await input.press("Enter");

    // User turn echoes immediately.
    await expect(
      page.getByText("How is my GA4 traffic trending this week?"),
    ).toBeVisible();

    // The worker runs the (fake) agent turn; the assistant reply + its tool-call trace arrive async.
    await pollWithReload(page, async () => {
      await expect(page.getByText("Tool calls")).toBeVisible();
    });
  });
});
