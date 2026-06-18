"use server";

import { serverConfig } from "@/lib/config";
import { getSession } from "@/lib/auth/session";
import { ApiError } from "@/lib/api/types";
import type { SessionUser } from "@/lib/auth/session";

interface AuthTokensResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
}

interface RegisterResponse {
  userId: string;
}

/**
 * Decode the JWT payload (base64url JSON) without verifying the signature.
 * Verification happens on the backend; here we only extract display fields.
 */
function decodeJwtPayload(token: string): Record<string, unknown> {
  try {
    const [, payload] = token.split(".");
    const padded = payload.replace(/-/g, "+").replace(/_/g, "/");
    const decoded = atob(padded);
    return JSON.parse(decoded) as Record<string, unknown>;
  } catch {
    return {};
  }
}

function extractArrayClaim(payload: Record<string, unknown>, key: string): string[] {
  const val = payload[key];
  if (Array.isArray(val)) return val.filter((v): v is string => typeof v === "string");
  if (typeof val === "string") return [val];
  return [];
}

async function fetchTokens(
  endpoint: string,
  body: Record<string, unknown>,
): Promise<AuthTokensResponse> {
  const res = await fetch(`${serverConfig.backendUrl}/v1${endpoint}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });

  if (!res.ok) {
    let errorCode: string | undefined;
    let detail: string | undefined;
    let fieldErrors: Record<string, string[]> | undefined;
    try {
      const json = (await res.json()) as {
        errorCode?: string;
        detail?: string;
        errors?: Record<string, string[]>;
      };
      errorCode = json.errorCode;
      detail = json.detail;
      fieldErrors = json.errors;
    } catch {
      // ignore parse failure
    }
    throw new ApiError({ status: res.status, errorCode, detail, fieldErrors });
  }

  const json = (await res.json()) as { data?: AuthTokensResponse } | AuthTokensResponse;
  const tokens =
    "data" in json && json.data ? json.data : (json as AuthTokensResponse);

  if (!tokens.accessToken || !tokens.refreshToken) {
    throw new ApiError({
      status: 500,
      errorCode: "generic.error",
      detail: "Invalid token response from server.",
    });
  }
  return tokens;
}

async function buildSessionUser(
  accessToken: string,
  fallbackEmail: string,
): Promise<SessionUser> {
  const payload = decodeJwtPayload(accessToken);

  // Claim names come from TokenIssuer.cs:
  //   sub / NameIdentifier → userId
  //   email → email
  //   role → roles[]       (AuthorizationClaims.Role = "role")
  //   permission → perms[] (AuthorizationClaims.Permission = "permission")
  const id =
    (typeof payload["sub"] === "string" ? payload["sub"] : undefined) ??
    (typeof payload[
      "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
    ] === "string"
      ? String(
          payload[
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
          ],
        )
      : fallbackEmail);
  const email =
    typeof payload["email"] === "string" ? payload["email"] : fallbackEmail;
  const roles = extractArrayClaim(payload, "role");
  const permissions = extractArrayClaim(payload, "permission");

  // Attempt to fetch the profile for displayName — best-effort.
  let displayName: string | null = null;
  try {
    const profileRes = await fetch(
      `${serverConfig.backendUrl}/v1/identity/users/me`,
      {
        headers: { authorization: `Bearer ${accessToken}` },
        cache: "no-store",
      },
    );
    if (profileRes.ok) {
      const raw = (await profileRes.json()) as {
        data?: { displayName?: string | null };
        displayName?: string | null;
      };
      const profile = "data" in raw && raw.data ? raw.data : (raw as { displayName?: string | null });
      displayName = profile.displayName ?? null;
    }
  } catch {
    // Non-fatal — displayName stays null
  }

  return { id, email, displayName, roles, permissions };
}

/**
 * Authenticates with email + password. On success, establishes the iron-session
 * (tokens + user) and returns void. On failure, throws an ApiError.
 */
export async function loginAction(email: string, password: string): Promise<void> {
  const tokens = await fetchTokens("/identity/auth/login", { email, password });
  const user = await buildSessionUser(tokens.accessToken, email);

  const session = await getSession();
  session.accessToken = tokens.accessToken;
  session.refreshToken = tokens.refreshToken;
  session.accessTokenExpiresAt = Date.parse(tokens.accessTokenExpiresAt);
  session.user = user;
  await session.save();
}

/**
 * Registers a new account and auto-logs in. On success, establishes the session.
 * On failure, throws an ApiError (the calling component shows the error).
 */
export async function registerAction(
  email: string,
  password: string,
  displayName?: string,
  inviteToken?: string,
): Promise<void> {
  // POST /v1/identity/users → { data: { userId } }
  const registerRes = await fetch(
    `${serverConfig.backendUrl}/v1/identity/users`,
    {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email, password, displayName, inviteToken }),
      cache: "no-store",
    },
  );

  if (!registerRes.ok) {
    let errorCode: string | undefined;
    let detail: string | undefined;
    let fieldErrors: Record<string, string[]> | undefined;
    try {
      const json = (await registerRes.json()) as {
        errorCode?: string;
        detail?: string;
        errors?: Record<string, string[]>;
      };
      errorCode = json.errorCode;
      detail = json.detail;
      fieldErrors = json.errors;
    } catch {
      // ignore
    }
    throw new ApiError({
      status: registerRes.status,
      errorCode,
      detail,
      fieldErrors,
    });
  }

  const regJson = (await registerRes.json()) as
    | { data?: RegisterResponse }
    | RegisterResponse;
  void regJson; // userId not needed directly — auto-login follows

  // Auto-login after registration.
  const tokens = await fetchTokens("/identity/auth/login", { email, password });
  const user = await buildSessionUser(tokens.accessToken, email);

  const session = await getSession();
  session.accessToken = tokens.accessToken;
  session.refreshToken = tokens.refreshToken;
  session.accessTokenExpiresAt = Date.parse(tokens.accessTokenExpiresAt);
  session.user = user;
  await session.save();
}

/**
 * Logs out: best-effort POST /v1/identity/auth/logout with the refresh token,
 * then destroys the session unconditionally.
 */
export async function logoutAction(): Promise<void> {
  const session = await getSession();
  const refreshToken = session.refreshToken;

  if (session.accessToken && refreshToken) {
    try {
      await fetch(`${serverConfig.backendUrl}/v1/identity/auth/logout`, {
        method: "POST",
        headers: {
          "content-type": "application/json",
          authorization: `Bearer ${session.accessToken}`,
        },
        body: JSON.stringify({ refreshToken }),
        cache: "no-store",
      });
    } catch {
      // Logout is best-effort; session is destroyed regardless.
    }
  }

  session.destroy();
  await session.save();
}
