import "server-only";
import { createHash } from "node:crypto";
import { serverConfig } from "@/lib/config";
import { getSession, type SessionData } from "@/lib/auth/session";
import { getRedis } from "@/lib/server/redis";
import type { IronSession } from "iron-session";

/**
 * Server-side gateway to the .NET `/v1` API. The ONLY place a bearer token is
 * attached and the ONLY place the refresh token is used. Both the BFF route and
 * RSC prefetch go through here so there is exactly one auth seam on the server.
 *
 * Refresh is single-flight PER SESSION (keyed by the current refresh token) so N
 * concurrent 401s trigger exactly one `/refresh` — mandatory because the backend
 * rotates refresh tokens one-time-use and reuse-detection would kill the session.
 *
 * SINGLE-NODE: an in-process `Map` coalesces concurrent refreshes (below).
 * MULTI-NODE (REDIS_URL set): a Redis `SET NX EX` lock elects ONE node to refresh;
 * the winner publishes the rotated token pair into Redis keyed by the old token's
 * hash, and contending nodes read that published result instead of re-consuming the
 * one-time refresh token (which would trip backend reuse-detection and kill the
 * session). Redis errors degrade gracefully back to the in-proc Map.
 */

interface RefreshResult {
  ok: boolean;
  accessToken?: string;
  refreshToken?: string;
  expiresAt?: number;
}

const refreshInFlight = new Map<string, Promise<RefreshResult>>();

// --- Redis distributed single-flight (multi-node) ---------------------------------
const REFRESH_LOCK_PREFIX = "modularplatform:refresh:lock:";
const REFRESH_RESULT_PREFIX = "modularplatform:refresh:result:";
const LOCK_TTL_SECONDS = 10;
const RESULT_TTL_SECONDS = 30; // outlives the lock so late contenders still read it
const POLL_INTERVAL_MS = 50;
const POLL_TIMEOUT_MS = LOCK_TTL_SECONDS * 1000;

/** Hash the refresh token so it never appears in plaintext as a Redis key. */
function tokenHash(token: string): string {
  return createHash("sha256").update(token).digest("hex");
}

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

interface BackendFetchOptions {
  method?: string;
  /** Pre-buffered body (so a request can be safely re-sent after a refresh retry). */
  body?: ArrayBuffer | string | null;
  headers?: Record<string, string>;
  /**
   * Whether a 401 may trigger a token rotation. TRUE only where we can persist the
   * rotated cookie (BFF route handler / server action). FALSE in RSC render, where
   * cookies are read-only — an un-persisted rotation would orphan the new refresh
   * token and trip reuse-detection on the next real request.
   */
  allowRefresh?: boolean;
  signal?: AbortSignal;
}

/** Result of a backend call: the raw upstream Response (status + body streamed through). */
export async function backendFetch(path: string, opts: BackendFetchOptions = {}): Promise<Response> {
  const session = await getSession();
  if (!session.accessToken) {
    return new Response(JSON.stringify({ errorCode: "auth.unauthenticated", status: 401 }), {
      status: 401,
      headers: { "content-type": "application/problem+json" },
    });
  }

  const send = (token: string) =>
    fetch(`${serverConfig.backendUrl}/v1${path}`, {
      method: opts.method ?? "GET",
      headers: buildHeaders(opts.headers, token),
      body: opts.body ?? undefined,
      signal: opts.signal,
      // Never let Next cache an authenticated upstream call.
      cache: "no-store",
    });

  let res = await send(session.accessToken);

  if (res.status === 401 && opts.allowRefresh && session.refreshToken) {
    const refreshed = await refreshSession(session);
    if (refreshed && session.accessToken) {
      res = await send(session.accessToken);
    }
    // If refresh failed, the original 401 propagates and the browser logs out.
  }

  return res;
}

function buildHeaders(extra: Record<string, string> | undefined, token: string): Headers {
  const headers = new Headers(extra);
  headers.set("authorization", `Bearer ${token}`);
  // Hop-by-hop / host headers must not be forwarded; strip anything dangerous.
  headers.delete("host");
  headers.delete("connection");
  headers.delete("content-length");
  return headers;
}

async function refreshSession(session: IronSession<SessionData>): Promise<boolean> {
  const key = session.refreshToken;
  if (!key) return false;

  const result = await coalesceRefresh(key);
  if (!result.ok || !result.accessToken) {
    await clearSession(session);
    return false;
  }

  session.accessToken = result.accessToken;
  session.refreshToken = result.refreshToken;
  session.accessTokenExpiresAt = result.expiresAt;
  await session.save();
  return true;
}

/**
 * Single-flight the refresh of a one-time refresh token. Within a process the in-proc
 * Map already coalesces concurrent callers; across processes (multi-node) a Redis lock
 * elects one winner that publishes the rotated tokens for the losers to consume.
 */
async function coalesceRefresh(refreshToken: string): Promise<RefreshResult> {
  // In-proc coalescing (always — cheap, and the only mechanism on single-node).
  let inFlight = refreshInFlight.get(refreshToken);
  if (inFlight) return inFlight;

  inFlight = serverConfig.redisUrl ? distributedRefresh(refreshToken) : doRefresh(refreshToken);
  refreshInFlight.set(refreshToken, inFlight);
  void inFlight.finally(() => {
    if (refreshInFlight.get(refreshToken) === inFlight) refreshInFlight.delete(refreshToken);
  });
  return inFlight;
}

/**
 * Multi-node single-flight. Acquire `SET NX EX` lock keyed by the token hash; the winner
 * refreshes and publishes the result, contenders poll for the published result rather
 * than double-consuming the one-time token. Any Redis failure falls back to a local
 * refresh (single-node behavior) so we never block auth on Redis availability.
 */
async function distributedRefresh(refreshToken: string): Promise<RefreshResult> {
  const redis = getRedis();
  if (!redis) return doRefresh(refreshToken);

  const hash = tokenHash(refreshToken);
  const lockKey = `${REFRESH_LOCK_PREFIX}${hash}`;
  const resultKey = `${REFRESH_RESULT_PREFIX}${hash}`;

  let acquired = false;
  try {
    acquired = (await redis.set(lockKey, "1", "EX", LOCK_TTL_SECONDS, "NX")) === "OK";
  } catch {
    // Redis unreachable → degrade to a local refresh (the in-proc Map still prevents
    // a local double-refresh; cross-node risk reverts to pre-Redis behavior).
    return doRefresh(refreshToken);
  }

  if (acquired) {
    const result = await doRefresh(refreshToken);
    try {
      // Publish only a SUCCESS the losers can apply. On failure, leave nothing so a
      // contender (after the lock TTL frees it) can retry independently.
      if (result.ok && result.accessToken) {
        await redis.set(resultKey, JSON.stringify(result), "EX", RESULT_TTL_SECONDS);
      }
    } catch {
      // Best-effort publish; losers will time out their poll and the original 401
      // propagates (browser logs out) — strictly safer than a double-consume.
    }
    return result;
  }

  // Lost the race: poll for the winner's published result.
  const deadline = Date.now() + POLL_TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      const raw = await redis.get(resultKey);
      if (raw) return JSON.parse(raw) as RefreshResult;
    } catch {
      break; // Redis went away mid-poll — fall through to the unauthenticated result.
    }
    await sleep(POLL_INTERVAL_MS);
  }
  // No result appeared (winner failed, or its publish was lost). Do NOT re-refresh the
  // same one-time token — return unauthenticated so the caller logs out cleanly.
  return { ok: false };
}

async function doRefresh(refreshToken: string): Promise<RefreshResult> {
  try {
    const res = await fetch(`${serverConfig.backendUrl}/v1/identity/auth/refresh`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    });
    if (!res.ok) return { ok: false };
    const json = (await res.json()) as { data?: TokenPair } | TokenPair;
    const tokens = "data" in json && json.data ? json.data : (json as TokenPair);
    if (!tokens.accessToken || !tokens.refreshToken) return { ok: false };
    return {
      ok: true,
      accessToken: tokens.accessToken,
      refreshToken: tokens.refreshToken,
      expiresAt: tokens.accessTokenExpiresAt ? Date.parse(tokens.accessTokenExpiresAt) : undefined,
    };
  } catch {
    return { ok: false };
  }
}

async function clearSession(session: IronSession<SessionData>): Promise<void> {
  session.destroy();
  try {
    await session.save();
  } catch {
    // RSC render context can't write cookies; the browser path will hard-logout.
  }
}

interface TokenPair {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt?: string;
}

/** Convenience used by RSC prefetch: never refreshes (read-only cookie context). */
export function backendFetchReadOnly(path: string, signal?: AbortSignal): Promise<Response> {
  return backendFetch(path, { allowRefresh: false, signal });
}

/** Re-export so callers don't import session just for the helper. */
export { getSession };
