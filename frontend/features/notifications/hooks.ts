"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { queryRoots } from "@/lib/api/query-keys";
import { markNotificationRead } from "@/features/notifications/api";

/**
 * Mutation to mark a single notification as read.
 * On success, invalidates the entire notifications root so all feed pages refresh.
 */
export function useMarkNotificationRead() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => markNotificationRead(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryRoots.notifications });
    },
  });
}
