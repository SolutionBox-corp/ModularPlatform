import { type NextRequest, NextResponse } from "next/server";
import { backendFetch } from "@/lib/server/backend";
import { CSRF_COOKIE, CSRF_HEADER, csrfMatches, isTrustedRequestOrigin } from "@/lib/auth/csrf";

/**
 * Catch-all Backend-for-Frontend proxy. The browser NEVER calls .NET directly:
 * every `/v1` request goes here, where the bearer is injected from the encrypted
 * session and the refresh token is rotated (server-side single-flight). This route
 * is a transparent proxy — it does NOT unwrap `{data}` or translate errors; the
 * browser `apiFetch` does that. Bodies and responses are streamed so SSE and file
 * download/upload work unchanged.
 */

const MUTATING = new Set(["POST", "PUT", "PATCH", "DELETE"]);
// Headers safe to forward upstream. Everything else (cookie, host, authorization) is dropped.
const FORWARD_HEADERS = ["accept", "accept-language", "content-type", "last-event-id"];

async function handle(request: NextRequest, ctx: { params: Promise<{ path: string[] }> }) {
  const { path } = await ctx.params;
  const method = request.method.toUpperCase();

  // CSRF: mutating requests must be same-origin AND carry a matching double-submit token.
  if (MUTATING.has(method)) {
    const cookieToken = request.cookies.get(CSRF_COOKIE)?.value;
    const headerToken = request.headers.get(CSRF_HEADER);
    if (!isTrustedRequestOrigin(request) || !csrfMatches(cookieToken, headerToken)) {
      return NextResponse.json(
        { errorCode: "security.csrf_failed", status: 403, detail: "CSRF validation failed." },
        { status: 403, headers: { "content-type": "application/problem+json" } },
      );
    }
  }

  const search = request.nextUrl.search; // includes leading "?" or ""
  const upstreamPath = `/${path.map(encodeURIComponent).join("/")}${search}`;

  // Buffer the body so a post-refresh retry can re-send it (JSON is tiny; uploads are
  // capped at 10 MB by the backend, so buffering is acceptable).
  const body = method === "GET" || method === "HEAD" ? null : await request.arrayBuffer();

  const headers: Record<string, string> = {};
  for (const name of FORWARD_HEADERS) {
    const value = request.headers.get(name);
    if (value) headers[name] = value;
  }

  const upstream = await backendFetch(upstreamPath, {
    method,
    body,
    headers,
    allowRefresh: true,
    signal: request.signal,
  });

  // Stream the upstream response straight back. Copy content-type / disposition /
  // retry-after; drop hop-by-hop headers.
  const responseHeaders = new Headers();
  for (const name of ["content-type", "content-disposition", "retry-after", "cache-control"]) {
    const value = upstream.headers.get(name);
    if (value) responseHeaders.set(name, value);
  }

  return new NextResponse(upstream.body, {
    status: upstream.status,
    statusText: upstream.statusText,
    headers: responseHeaders,
  });
}

export const GET = handle;
export const POST = handle;
export const PUT = handle;
export const PATCH = handle;
export const DELETE = handle;

// SSE must not be statically optimized or buffered.
export const dynamic = "force-dynamic";
export const runtime = "nodejs";
