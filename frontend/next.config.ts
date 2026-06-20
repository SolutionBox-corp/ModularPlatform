import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";
import { allowedDevOrigins, serverActionsAllowedOrigins } from "./lib/origins";

const withNextIntl = createNextIntlPlugin("./lib/i18n/request.ts");

const isProd = process.env.NODE_ENV === "production";

// Static sibling security headers (the per-request CSP nonce is set in proxy.ts).
// HSTS is PRODUCTION-ONLY: on the HTTP dev server it would force the browser to upgrade
// localhost to https (which the dev server can't serve) — and the browser caches that for
// up to 2 years. Never send it in dev.
const securityHeaders = [
  ...(isProd
    ? [{ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains; preload" }]
    : []),
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), browsing-topics=()" },
  { key: "X-DNS-Prefetch-Control", value: "off" },
];

const nextConfig: NextConfig = {
  // Cross-origin allowlists, derived from ROOT_DOMAIN (see lib/origins.ts):
  //  - allowedDevOrigins (dev): hosts that may reach dev resources + Server Actions — localhost,
  //    127.0.0.1, and every tenant subdomain (*.lvh.me). Without it Next blocks them.
  //  - serverActions.allowedOrigins (prod): the Origin↔Host CSRF allowlist that survives the
  //    reverse proxy + subdomain-per-tenant (*.nasedomena.cz, admin., apex).
  allowedDevOrigins: allowedDevOrigins(),
  experimental: {
    serverActions: { allowedOrigins: serverActionsAllowedOrigins() },
  },
  async headers() {
    return [{ source: "/(.*)", headers: securityHeaders }];
  },
};

export default withNextIntl(nextConfig);
