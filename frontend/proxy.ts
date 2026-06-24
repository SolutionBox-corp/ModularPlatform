import { NextResponse, type NextRequest } from "next/server";
import { classifyHost, TENANT_HEADER, ADMIN_TENANT, DEV_DEFAULT_TENANT } from "@/lib/tenant";
import { CSRF_COOKIE, generateCsrfToken } from "@/lib/auth/csrf";

/**
 * Edge proxy (Next 16 rename of middleware). Responsibilities, in order:
 *  1. Strip any client-supplied `x-tenant` (anti-spoof) and inject a server-trusted one.
 *  2. Host-based routing: `admin.<root>` → internal `/platform/*`; everyone else → the
 *     tenant app at root. Tenant identity comes ONLY from the subdomain.
 *  3. Per-request CSP nonce + `Content-Security-Policy`.
 *  4. Ensure the `mp_csrf` double-submit cookie exists.
 */
const ROOT_DOMAIN = process.env.ROOT_DOMAIN ?? "lvh.me:3000";

export function proxy(request: NextRequest): NextResponse {
  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const isDev = process.env.NODE_ENV !== "production";

  const url = request.nextUrl;
  const host = request.headers.get("host");
  const hostClass = classifyHost(host, ROOT_DOMAIN);

  // Build the forwarded request headers: drop spoofed x-tenant, add the trusted one + nonce.
  const requestHeaders = new Headers(request.headers);
  requestHeaders.delete(TENANT_HEADER);
  requestHeaders.set("x-nonce", nonce);

  // Guard internal-only path prefixes against direct access from the wrong host.
  const isPlatformPath = url.pathname === "/platform" || url.pathname.startsWith("/platform/");

  let rewriteTo: URL | null = null;
  if (hostClass.kind === "admin") {
    requestHeaders.set(TENANT_HEADER, ADMIN_TENANT);
    // The (auth) route group lives at the ROOT (/login, /register) — NOT under /platform. They must stay
    // reachable on the admin host, otherwise the platform layout's redirect("/login") gets rewritten to
    // /platform/login (which doesn't exist) → 404, and the admin can never sign in to the console.
    const isAuthPath = url.pathname === "/login" || url.pathname === "/register";
    // Rewrite the admin host's other root paths into the internal /platform segment.
    if (!isPlatformPath && !isAuthPath) {
      rewriteTo = new URL(`/platform${url.pathname === "/" ? "" : url.pathname}${url.search}`, url);
    }
  } else {
    // Tenant (subdomain) or apex (dev convenience → a default tenant).
    const tenant = hostClass.kind === "tenant" ? hostClass.tenant : DEV_DEFAULT_TENANT;
    requestHeaders.set(TENANT_HEADER, tenant);
    // The /platform tree is admin-only; block it from tenant/apex hosts.
    if (isPlatformPath) {
      rewriteTo = new URL("/not-found", url);
    }
  }

  const csp = buildCsp(nonce, isDev);
  requestHeaders.set("content-security-policy", csp);

  const response = rewriteTo
    ? NextResponse.rewrite(rewriteTo, { request: { headers: requestHeaders } })
    : NextResponse.next({ request: { headers: requestHeaders } });

  response.headers.set("content-security-policy", csp);

  if (!request.cookies.get(CSRF_COOKIE)) {
    response.cookies.set(CSRF_COOKIE, generateCsrfToken(), {
      httpOnly: false, // JS-readable so the browser client can echo it in x-csrf-token.
      secure: !isDev,
      sameSite: "lax",
      path: "/",
    });
  }

  return response;
}

function buildCsp(nonce: string, isDev: boolean): string {
  const directives = [
    `default-src 'self'`,
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${isDev ? " 'unsafe-eval'" : ""}`,
    // A nonce makes the browser IGNORE 'unsafe-inline', so dev (which needs inline <style>
    // from the dev tooling + HMR) uses 'unsafe-inline' WITHOUT the nonce; prod is nonce-strict.
    isDev ? `style-src 'self' 'unsafe-inline'` : `style-src 'self' 'nonce-${nonce}'`,
    // Inline style ATTRIBUTES (React sets element style for widths/transforms) aren't
    // covered by a style-src nonce; allow them narrowly. Low XSS risk, no script.
    `style-src-attr 'unsafe-inline'`,
    `img-src 'self' blob: data:`,
    `font-src 'self'`,
    `connect-src 'self'`,
    `object-src 'none'`,
    `base-uri 'self'`,
    `form-action 'self'`,
    `frame-ancestors 'none'`,
  ];
  // HTTPS-forcing directives MUST NOT run on the HTTP dev server (they'd upgrade _next/static
  // asset requests to https and break CSS/JS loading). Production only.
  if (!isDev) {
    directives.push(`upgrade-insecure-requests`);
    // NOTE: `require-trusted-types-for 'script'` was removed — Next.js 16's client chunk loader sets
    // script.src / innerHTML without a Trusted Types policy, so enforcing TT blocks all client JS in
    // production (TrustedHTML/TrustedScriptURL errors → blank "Something went wrong" page). The nonce +
    // 'strict-dynamic' script-src already constrains script execution; TT is an extra layer Next can't satisfy yet.
  }
  return directives.join("; ");
}

export const config = {
  matcher: [
    {
      source: "/((?!api|_next/static|_next/image|favicon.ico).*)",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};
