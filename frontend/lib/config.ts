import "server-only";

/**
 * Server-only configuration. Never imported into a Client Component
 * (the `server-only` guard makes that a build error), so secrets like the
 * session password and the backend origin can never leak into the browser bundle.
 */

function required(name: string, value: string | undefined): string {
  if (!value || value.length === 0) {
    throw new Error(`Missing required environment variable: ${name}`);
  }
  return value;
}

export const serverConfig = {
  /** Origin of the .NET API. The BFF appends `/v1`. */
  backendUrl: required("BACKEND_URL", process.env.BACKEND_URL).replace(/\/$/, ""),
  /** iron-session encryption password. Must be >= 32 chars. */
  sessionPassword: required("SESSION_PASSWORD", process.env.SESSION_PASSWORD),
  /** Root domain for subdomain-per-tenant routing, e.g. `lvh.me:3000` or `nasedomena.cz`. */
  rootDomain: process.env.ROOT_DOMAIN ?? "lvh.me:3000",
  isProduction: process.env.NODE_ENV === "production",
} as const;

if (serverConfig.sessionPassword.length < 32) {
  throw new Error("SESSION_PASSWORD must be at least 32 characters.");
}

// Fail-fast on a known dev secret in production — but only at RUNTIME, not during
// `next build` (the build runs with NODE_ENV=production yet the real secret is injected at
// deploy/serve time, so checking at build would wrongly fail CI on the dev default value).
const isBuildPhase = process.env.NEXT_PHASE === "phase-production-build";
if (serverConfig.isProduction && !isBuildPhase) {
  const lower = serverConfig.sessionPassword.toLowerCase();
  if (lower.includes("dev-only") || lower.includes("change-me")) {
    throw new Error(
      "SESSION_PASSWORD is still the dev default ('dev-only' / 'change-me'). Set a real secret in production.",
    );
  }
}
