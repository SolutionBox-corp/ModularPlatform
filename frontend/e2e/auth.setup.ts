import { test as setup } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { registerFreshUser, uniqueEmail } from "./helpers";

const AUTH_FILE = "e2e/.auth/user.json";

/**
 * Global auth setup: register one primary user and persist its authenticated session so the
 * bulk of the suite reuses it (no per-test re-registration → avoids the per-IP auth rate-limit).
 */
setup("authenticate primary user", async ({ page }) => {
  mkdirSync("e2e/.auth", { recursive: true });
  await registerFreshUser(page, { email: uniqueEmail("e2e-primary"), displayName: "E2E Primary" });
  await page.context().storageState({ path: AUTH_FILE });
});
