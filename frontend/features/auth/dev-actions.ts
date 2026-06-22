"use server";

import { loginAction, registerAction, type AuthResult } from "./actions";

/**
 * DEV-ONLY quick login. Logs in the given account; if it doesn't exist yet, registers it
 * (self-serve registration auto-provisions a tenant + logs in), so the button always works.
 * Hard-guarded to development — a no-op in production, where arbitrary credential login would
 * be an obvious security hole.
 */
export async function devQuickLogin(
  email: string,
  password: string,
  displayName: string,
): Promise<AuthResult> {
  if (process.env.NODE_ENV === "production") {
    return { ok: false, status: 403, errorCode: "generic.error", detail: "Disabled in production." };
  }

  const login = await loginAction(email, password);
  if (login.ok) return login;

  // Most likely the account doesn't exist yet on this machine → create it (auto-logs in).
  return registerAction(email, password, displayName);
}
