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
