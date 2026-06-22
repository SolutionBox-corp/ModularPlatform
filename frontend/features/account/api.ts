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

/**
 * NOTE: No profile UPDATE endpoint exists in the backend (Identity module has no
 * UpdateProfile / PatchProfile slice under Features/Users as of this build). The
 * form is therefore read-only for all fields. Wire up a mutation here once
 * PATCH /v1/identity/users/me (or equivalent) is added to the backend.
 */
