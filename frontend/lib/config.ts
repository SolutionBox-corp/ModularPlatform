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

/**
 * iron-session supports keyed passwords for rotation: a `Record<id, password>` where
 * the highest id seals new cookies and ALL ids can unseal old ones. We accept BOTH a
 * bare string (legacy single secret) AND a JSON map `{"1":"old","2":"new"}`.
 *
 * `SESSION_PASSWORD_ID` (default "1") selects which entry seals new cookies. iron-session
 * v8 picks the highest numeric key automatically, but we keep an explicit id so a bare
 * string can be wrapped under a deterministic, configurable key during a rotation.
 */
function parseSessionPassword(raw: string): Record<string, string> {
  const trimmed = raw.trim();
  let map: Record<string, string>;
  if (trimmed.startsWith("{")) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(trimmed);
    } catch {
      throw new Error("SESSION_PASSWORD looks like JSON but failed to parse. Expected a string or a JSON map.");
    }
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
      throw new Error('SESSION_PASSWORD JSON must be an object map, e.g. {"1":"old","2":"new"}.');
    }
    map = {};
    for (const [id, value] of Object.entries(parsed)) {
      if (typeof value !== "string") {
        throw new Error(`SESSION_PASSWORD entry "${id}" must be a string.`);
      }
      map[id] = value;
    }
    if (Object.keys(map).length === 0) {
      throw new Error("SESSION_PASSWORD map must contain at least one entry.");
    }
  } else {
    // Bare string → wrap under the configured id (default "1").
    map = { [process.env.SESSION_PASSWORD_ID ?? "1"]: trimmed };
  }

  // Keep the existing fail-fast min-length validation for EVERY entry.
  for (const [id, value] of Object.entries(map)) {
    if (value.length < 32) {
      throw new Error(`SESSION_PASSWORD entry "${id}" must be at least 32 characters.`);
    }
  }
  return map;
}

const sessionPasswordMap = parseSessionPassword(required("SESSION_PASSWORD", process.env.SESSION_PASSWORD));

export const serverConfig = {
  /** Origin of the .NET API. The BFF appends `/v1`. */
  backendUrl: required("BACKEND_URL", process.env.BACKEND_URL).replace(/\/$/, ""),
  /**
   * iron-session encryption password(s) in keyed-map form `{id: password}`. A bare
   * string env value is auto-wrapped to `{[SESSION_PASSWORD_ID]: value}`. Every entry
   * is >= 32 chars. All ids unseal; the current id seals new cookies (rotation-safe).
   */
  sessionPassword: sessionPasswordMap as Record<string, string>,
  /** The id whose password seals NEW cookies. Old cookies under any id still unseal. */
  sessionPasswordId: process.env.SESSION_PASSWORD_ID ?? "1",
  /** Root domain for subdomain-per-tenant routing, e.g. `lvh.me:3000` or `nasedomena.cz`. */
  rootDomain: process.env.ROOT_DOMAIN ?? "lvh.me:3000",
  /** Redis URL for multi-node coordination (single-flight refresh lock). Undefined = single-node. */
  redisUrl: process.env.REDIS_URL,
  isProduction: process.env.NODE_ENV === "production",
} as const;

// Fail-fast on a known dev secret in production — but only at RUNTIME, not during
// `next build` (the build runs with NODE_ENV=production yet the real secret is injected at
// deploy/serve time, so checking at build would wrongly fail CI on the dev default value).
const isBuildPhase = process.env.NEXT_PHASE === "phase-production-build";
if (serverConfig.isProduction && !isBuildPhase) {
  for (const value of Object.values(serverConfig.sessionPassword)) {
    const lower = value.toLowerCase();
    if (lower.includes("dev-only") || lower.includes("change-me")) {
      throw new Error(
        "SESSION_PASSWORD is still the dev default ('dev-only' / 'change-me'). Set a real secret in production.",
      );
    }
  }
}
