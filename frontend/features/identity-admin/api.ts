import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";

// ---------------------------------------------------------------------------
// Response types (mirroring the backend records)
// ---------------------------------------------------------------------------

export interface AuditTrailEntryResponse {
  id: string;
  action: string;
  timestamp: string;
  values: Record<string, string | null>;
}

export interface UserAuditTrailResponse {
  entries: AuditTrailEntryResponse[];
}

/** GET /v1/identity/admin/users/{userId} - a user's profile + CURRENT role names.
 *  email/displayName surface the literal "[erased]" for a GDPR-erased subject. */
export interface UserDetailResponse {
  id: string;
  email: string;
  displayName: string | null;
  roles: string[];
  isLocked: boolean;
  createdAt: string;
}

// ---------------------------------------------------------------------------
// Query factories
// ---------------------------------------------------------------------------

/**
 * GET /v1/identity/admin/users/{userId}/audit
 * Requires permission: audit.read
 */
export const identityAdminQueries = {
  auditTrail: (userId: string) =>
    queryOptions({
      queryKey: [...queryRoots.admin, "identity", "audit", userId],
      queryFn: () =>
        apiFetch<UserAuditTrailResponse>(
          `identity/admin/users/${userId}/audit`,
        ),
      enabled: userId.trim().length > 0,
      staleTime: 30_000,
    }),

  /**
   * GET /v1/identity/admin/users/{userId} - profile + current roles.
   * Requires permission: identity.manage_roles. 404 when the user is unknown/erased.
   */
  userDetail: (userId: string) =>
    queryOptions({
      queryKey: [...queryRoots.admin, "identity", "user", userId],
      queryFn: () =>
        apiFetch<UserDetailResponse>(`identity/admin/users/${userId}`),
      enabled: userId.trim().length > 0,
      staleTime: 30_000,
    }),
};

// ---------------------------------------------------------------------------
// Mutation helpers (plain async fns — wrapped by hooks.ts)
// ---------------------------------------------------------------------------

/**
 * POST /v1/identity/admin/users/{userId}/roles  { role: string }
 * Requires permission: identity.manage_roles
 */
export function assignRole(userId: string, role: string): Promise<void> {
  return apiFetch<void>(`identity/admin/users/${userId}/roles`, {
    method: "POST",
    body: { role },
  });
}

/**
 * DELETE /v1/identity/admin/users/{userId}/roles/{role}
 * Requires permission: identity.manage_roles
 */
export function revokeRole(userId: string, role: string): Promise<void> {
  return apiFetch<void>(`identity/admin/users/${userId}/roles/${role}`, {
    method: "DELETE",
  });
}
