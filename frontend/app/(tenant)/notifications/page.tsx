import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { notificationQueries } from "@/features/notifications/api";
import { NotificationsFeed } from "@/features/notifications/components/notifications-feed";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Notifications — ModularPlatform",
};

export default async function NotificationsPage() {
  const queryClient = getQueryClient();

  // Prefetch the first page so it streams to the browser without a waterfall.
  void queryClient.prefetchQuery(notificationQueries.feed({ page: 1, pageSize: 20 }));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Notifications</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Your recent activity and system messages.
          </p>
        </div>

        <NotificationsFeed />
      </div>
    </HydrationBoundary>
  );
}
