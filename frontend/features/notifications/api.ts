import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";
import type { Paged } from "@/lib/api/types";

export interface NotificationItem {
  id: string;
  templateKey: string;
  title: string;
  body: string;
  readAt: string | null;
  createdAt: string;
}

export interface UnreadCountResponse {
  count: number;
}

export const notificationQueries = {
  /** GET /v1/notifications/me — paginated feed. */
  feed: (params?: { unreadOnly?: boolean; page?: number; pageSize?: number }) =>
    queryOptions({
      queryKey: [
        ...queryRoots.notifications,
        "feed",
        params?.unreadOnly ?? false,
        params?.page ?? 1,
        params?.pageSize ?? 20,
      ],
      queryFn: () => {
        const sp = new URLSearchParams();
        if (params?.unreadOnly) sp.set("unreadOnly", "true");
        if (params?.page) sp.set("page", String(params.page));
        if (params?.pageSize) sp.set("pageSize", String(params.pageSize));
        const qs = sp.toString();
        return apiFetch<Paged<NotificationItem>>(
          `notifications/me${qs ? `?${qs}` : ""}`,
        );
      },
      staleTime: 30_000,
    }),

  /** Backwards-compat alias used by the dashboard prefetch. */
  list: (params?: { unreadOnly?: boolean; page?: number; pageSize?: number }) =>
    notificationQueries.feed(params),

  /** GET /v1/notifications/me/unread-count — the unread badge counter. */
  unreadCount: () =>
    queryOptions({
      queryKey: [...queryRoots.notifications, "unread-count"],
      queryFn: () =>
        apiFetch<UnreadCountResponse>("notifications/me/unread-count"),
      staleTime: 30_000,
    }),
};

/** POST /v1/notifications/{id}/read — marks a single notification as read. */
export function markNotificationRead(id: string): Promise<void> {
  return apiFetch<void>(`notifications/${id}/read`, { method: "POST" });
}

/** POST /v1/notifications/me/read-all — marks all of the caller's unread notifications read. */
export function markAllNotificationsRead(): Promise<{ marked: number }> {
  return apiFetch<{ marked: number }>("notifications/me/read-all", {
    method: "POST",
  });
}
