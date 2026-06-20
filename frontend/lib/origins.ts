/**
 * Single source of truth for the app's TRUSTED ORIGINS, derived from `ROOT_DOMAIN`.
 *
 * Used in three places so the allowlist never drifts:
 *   1. next.config `allowedDevOrigins`            — dev-only cross-origin dev resources + Server Actions
 *   2. next.config `serverActions.allowedOrigins` — prod CSRF (Origin↔Host, survives a reverse proxy)
 *   3. the BFF CSRF check (lib/auth/csrf)          — mutating /api/bff requests
 *
 * Subdomain-per-tenant means every tenant is its own origin: `{tenant}.<root>`, `admin.<root>`, the
 * apex `<root>`. We trust the root domain + all its subdomains (one wildcard covers every tenant),
 * plus localhost/127.0.0.1 for dev and an optional `ALLOWED_ORIGINS` list for Phase-3 vanity domains.
 *
 * Pure + framework-free (only reads `process.env`) so it runs in the next.config context, on the edge
 * (proxy), and in Node route handlers alike. Host matching is hostname-based (ports are ignored).
 */

/** Root hostname without the port (`ROOT_DOMAIN` may carry one in dev, e.g. "lvh.me:3000"). */
export function rootHostname(): string {
  return (process.env.ROOT_DOMAIN ?? "lvh.me:3000").split(":")[0].toLowerCase();
}

/** Optional extra trusted hostnames (comma-separated), e.g. vanity domains `portal.acme.com`. */
function extraHosts(): string[] {
  return (process.env.ALLOWED_ORIGINS ?? "")
    .split(",")
    .map((s) => s.trim().toLowerCase())
    .filter(Boolean);
}

/**
 * Strip scheme/port → bare lowercase hostname. Handles both a full origin URL
 * ("http://demo.lvh.me:3000") and a bare host:port ("localhost:3000"). NOTE: `new URL`
 * mis-parses a scheme-less "host:port" (it reads "host:" as the scheme), so we only call it
 * when a scheme is actually present.
 */
function hostnameOf(originOrHost: string): string {
  const value = originOrHost.trim().toLowerCase();
  if (value.includes("://")) {
    try {
      return new URL(value).hostname;
    } catch {
      return "";
    }
  }
  // Bare host[:port] (e.g. the Host header) → drop the port.
  return value.split(":")[0];
}

/** DEV ONLY: hosts allowed to reach the dev server's cross-origin resources + Server Actions. */
export function allowedDevOrigins(): string[] {
  const root = rootHostname();
  return [
    "localhost",
    "*.localhost",
    "127.0.0.1",
    root,
    `*.${root}`,
    ...extraHosts(),
  ];
}

/** PROD: safe origins for Server Actions (Next compares Origin to Host; this covers proxies + tenants). */
export function serverActionsAllowedOrigins(): string[] {
  const root = rootHostname();
  return [root, `*.${root}`, ...extraHosts()];
}

/**
 * Is `origin` trusted to make a mutating request served by `requestHost`?
 * (1) exactly the served origin (the normal same-origin case), or
 * (2) the configured root domain or any of its subdomains (every tenant + admin + apex), or
 * (3) an explicit extra/vanity host.
 */
export function isTrustedOrigin(origin: string | null, requestHost: string | null): boolean {
  if (!origin) return false;
  const originHostname = hostnameOf(origin);
  if (!originHostname) return false;

  // (1) Same host as the page that served the request.
  if (requestHost && originHostname === hostnameOf(requestHost)) return true;

  // (2) The root domain or any subdomain of it.
  const root = rootHostname();
  if (originHostname === root || originHostname.endsWith(`.${root}`)) return true;

  // (3) Explicit extra hosts.
  return extraHosts().includes(originHostname);
}
