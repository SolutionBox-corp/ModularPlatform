"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { queryRoots } from "@/lib/api/query-keys";
import { assignRole, revokeRole } from "@/features/identity-admin/api";

/**
 * Assign a role to a user. Invalidates the admin root on success so any
 * cached audit trail (which may reflect role changes) refreshes.
 */
export function useAssignRole() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: string }) =>
      assignRole(userId, role),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryRoots.admin });
    },
  });
}

/**
 * Revoke a role from a user. Invalidates the admin root on success.
 */
export function useRevokeRole() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: string }) =>
      revokeRole(userId, role),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryRoots.admin });
    },
  });
}
