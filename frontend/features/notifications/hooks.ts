"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryRoots } from "@/lib/api/query-keys";
import {
  markNotificationRead,
  markAllNotificationsRead,
  notificationQueries,
  setNotificationPreference,
} from "@/features/notifications/api";

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

/** Unread-notification counter for the app-shell bell badge. */
export function useUnreadNotificationCount() {
  return useQuery(notificationQueries.unreadCount());
}

/**
 * Mutation to mark ALL unread notifications as read in one call.
 * Invalidates the notifications root so the feed + unread badge both refresh.
 */
export function useMarkAllNotificationsRead() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => markAllNotificationsRead(),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryRoots.notifications });
    },
  });
}

export function useNotificationPreferences() {
  return useQuery(notificationQueries.preferences());
}

export function useSetNotificationPreference() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: setNotificationPreference,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryRoots.notifications });
    },
  });
}
