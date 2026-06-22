import { isTrustedOrigin } from "@/lib/origins";

/**
 * CSRF defence for mutating BFF routes. Three cheap layers, no heavy lib:
 *  1. SameSite=Lax session cookie (blocks cross-site form posts by default).
 *  2. Origin/Referer allowlist — checked in the BFF against the trusted-origins config
 *     (the served host, the root domain + every tenant subdomain, and vanity domains).
 *  3. Signed double-submit: a non-httpOnly `mp_csrf` cookie echoed in `x-csrf-token`.
 * The cookie is JS-readable on purpose so the browser client can echo it; an
 * attacker on another origin cannot read it (SOP) nor set our header.
 */

export const CSRF_COOKIE = "mp_csrf";
export const CSRF_HEADER = "x-csrf-token";

/** Generate a fresh, unguessable token (Web Crypto — runs in proxy + route handlers). */
export function generateCsrfToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return base64url(bytes);
}

/** Constant-time compare of the cookie value and the request header. */
export function csrfMatches(cookieValue: string | undefined, headerValue: string | null): boolean {
  if (!cookieValue || !headerValue) return false;
  if (cookieValue.length !== headerValue.length) return false;
  let diff = 0;
  for (let i = 0; i < cookieValue.length; i++) {
    diff |= cookieValue.charCodeAt(i) ^ headerValue.charCodeAt(i);
  }
  return diff === 0;
}

/** True when the request's Origin (or Referer) is a trusted origin for the served host. */
export function isTrustedRequestOrigin(request: Request): boolean {
  const host = request.headers.get("host");
  if (!host) return false;
  // Prefer Origin; fall back to Referer (some agents omit Origin on same-origin GET-ish posts).
  const origin = request.headers.get("origin") ?? request.headers.get("referer");
  // No Origin and no Referer on a mutating request → reject.
  return isTrustedOrigin(origin, host);
}

function base64url(bytes: Uint8Array): string {
  let bin = "";
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
