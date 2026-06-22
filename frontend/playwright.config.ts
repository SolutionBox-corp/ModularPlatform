import { defineConfig, devices } from "@playwright/test";

/**
 * E2E config. Runs against a LIVE stack: the Next dev server on :3000 (BFF) and the
 * .NET API on :5271 (+ Postgres on :5432, migrated). Start both before `pnpm e2e`:
 *   backend:  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5271 \
 *             dotnet run --project src/hosts/ModularPlatform.Api --no-launch-profile
 *   frontend: pnpm dev   (or rely on the reused webServer below)
 *
 * Serialized (workers:1) on purpose: registration shares one per-IP "auth" rate-limit and
 * the backend state is shared, so parallel tests would flake. The "setup" project registers
 * one primary user and saves its authenticated storageState; most specs reuse it. Auth /
 * security specs opt out via `test.use({ storageState: { cookies: [], origins: [] } })`.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  // One retry everywhere: the suite runs against a LIVE async stack (the welcome notification
  // is created by a background worker, toasts auto-dismiss), so a timing-sensitive test can
  // occasionally flake. A single retry keeps the suite reliably green without masking real bugs.
  retries: 1,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3000",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  projects: [
    { name: "setup", testMatch: /.*\.setup\.ts/ },
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"], storageState: "e2e/.auth/user.json" },
      dependencies: ["setup"],
    },
  ],
  // Reuse a frontend dev server if one is already running; otherwise start it.
  // The .NET backend must be started separately (see header).
  webServer: {
    command: "pnpm dev",
    url: "http://localhost:3000/login",
    reuseExistingServer: true,
    timeout: 120_000,
  },
});
