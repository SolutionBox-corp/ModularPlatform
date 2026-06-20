import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

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
  // Allow accessing the dev server via 127.0.0.1 (a clean host with no cached localhost HSTS):
  // without this, Next blocks cross-origin dev resources + Server Actions from 127.0.0.1.
  allowedDevOrigins: ["127.0.0.1", "localhost"],
  async headers() {
    return [{ source: "/(.*)", headers: securityHeaders }];
  },
};

export default withNextIntl(nextConfig);
