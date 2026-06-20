import "server-only";
import { getIronSession, type IronSession, type SessionOptions } from "iron-session";
import { cookies } from "next/headers";
import { serverConfig } from "@/lib/config";

/**
 * The current user, snapshotted from the JWT claims at login/refresh. Roles +
 * permissions drive nav/route guards on the client; the backend remains the real
 * enforcement (Law 10/authorization). Never trust this for a security decision the
 * backend can make instead.
 */
export interface SessionUser {
  id: string;
  email: string;
  displayName: string | null;
  roles: string[];
  permissions: string[];
}

/**
 * Server-side session. BOTH tokens live here, encrypted, httpOnly — the browser
 * never sees them (not even the access token in memory). The BFF reads the access
 * token to inject `Authorization: Bearer` and rotates the refresh token in place.
 */
export interface SessionData {
  accessToken?: string;
  /** Epoch ms when the access token expires; the BFF refreshes proactively. */
  accessTokenExpiresAt?: number;
  refreshToken?: string;
  user?: SessionUser;
}

export const sessionOptions: SessionOptions = {
  // Keyed-map password (rotation-safe). iron-session v8 seals new cookies with the
  // highest-keyed entry and unseals with ANY entry, so a cookie sealed under id "1"
  // still decrypts after we add id "2" and rotate. See lib/config.ts.
  password: serverConfig.sessionPassword,
  cookieName: "mp_session",
  // No `domain` → bound to the exact subdomain (host-only). NEVER set Domain=.root
  // or a tenant's session would bleed across tenants.
  cookieOptions: {
    httpOnly: true,
    secure: serverConfig.isProduction,
    sameSite: "lax",
    path: "/",
  },
};

/** Read/write the session bound to the current request's cookies. */
export async function getSession(): Promise<IronSession<SessionData>> {
  return getIronSession<SessionData>(await cookies(), sessionOptions);
}

export function isAuthenticated(session: SessionData): boolean {
  return Boolean(session.accessToken && session.user);
}
