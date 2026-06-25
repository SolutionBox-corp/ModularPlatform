import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";

/** Shape returned by GET /v1/identity/users/me (UserProfileResponse.cs) */
export interface UserProfileResponse {
  id: string;
  email: string;
  displayName: string | null;
  locale: string;
}

export const accountQueries = {
  /** GET /v1/identity/users/me */
  profile: () =>
    queryOptions({
      queryKey: [...queryRoots.identity, "profile", "me"],
      queryFn: () => apiFetch<UserProfileResponse>("identity/users/me"),
      staleTime: 60_000,
    }),
};

/** Body for PATCH /v1/identity/users/me (UpdateProfileRequest.cs). */
export interface UpdateProfileRequest {
  displayName: string | null;
  locale: string;
}

/** PATCH /v1/identity/users/me — update the caller's own display name + locale. */
export function updateProfile(
  body: UpdateProfileRequest,
): Promise<UserProfileResponse> {
  return apiFetch<UserProfileResponse>("identity/users/me", {
    method: "PATCH",
    body,
  });
}

/**
 * POST /v1/identity/users/me/change-password — rotate the caller's password.
 * On success the backend revokes ALL sessions, so the caller must sign in again.
 */
export async function changePassword(body: {
  currentPassword: string;
  newPassword: string;
}): Promise<void> {
  await apiFetch<void>("identity/users/me/change-password", {
    method: "POST",
    body,
  });
}
