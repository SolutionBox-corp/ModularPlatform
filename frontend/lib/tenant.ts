/**
 * Host → tenant resolution. Tenant identity is SOLELY the subdomain (never path/body),
 * mirroring backend Law 10 (IDOR parity). The proxy injects a server-trusted `x-tenant`
 * header after stripping any client-supplied one; server components read it via headers().
 */

export const RESERVED_SUBDOMAINS = new Set(["admin", "www", "api", "static", "assets"]);

export type HostClass =
  | { kind: "admin" }
  | { kind: "tenant"; tenant: string }
  | { kind: "apex" };

/**
 * Classify an incoming host against the configured root domain.
 * Dev roots are `lvh.me:port` / `localhost:port`; prod is the bare domain.
 */
export function classifyHost(host: string | null, rootDomain: string): HostClass {
  if (!host) return { kind: "apex" };
  const hostname = host.split(":")[0].toLowerCase();
  const root = rootDomain.split(":")[0].toLowerCase();

  if (hostname === root || hostname === `www.${root}`) return { kind: "apex" };

  if (hostname.endsWith(`.${root}`)) {
    const label = hostname.slice(0, -(`.${root}`.length)).split(".").pop() ?? "";
    if (label === "admin") return { kind: "admin" };
    if (label && !RESERVED_SUBDOMAINS.has(label)) return { kind: "tenant", tenant: label };
  }

  // Bare localhost or unknown host in dev → treat as apex.
  return { kind: "apex" };
}

export const TENANT_HEADER = "x-tenant";
/** Marker value used in x-tenant for the platform-admin app (not a real tenant). */
export const ADMIN_TENANT = "__admin__";
/** Default tenant used at the apex host in dev so the app is reachable without a subdomain. */
export const DEV_DEFAULT_TENANT = "demo";
