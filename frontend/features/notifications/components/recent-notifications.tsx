"use client";

import { useQuery } from "@tanstack/react-query";
import { BellIcon, CheckIcon } from "lucide-react";
import Link from "next/link";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/app/empty-state";
import { notificationQueries } from "@/features/notifications/api";
import { useMarkNotificationRead } from "@/features/notifications/hooks";
import { cn } from "@/lib/utils";

/**
 * Compact recent-notification feed for the dashboard.
 * Shows the first page (5 items), supports mark-as-read per item,
 * and stays live via the RealtimeProvider (event-map invalidates the
 * notifications root on "notification.created" / "notification.read").
 */
export function RecentNotifications() {
  const { data, isLoading } = useQuery(
    notificationQueries.feed({ page: 1, pageSize: 5 }),
  );
  const markRead = useMarkNotificationRead();

  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between">
          <CardTitle className="text-sm font-medium">Notifications</CardTitle>
          <Link
            href="/notifications"
            className="text-xs text-primary underline-offset-4 hover:underline"
          >
            View all
          </Link>
        </div>
        <CardDescription className="text-xs">Recent activity</CardDescription>
      </CardHeader>
      <CardContent className="p-0">
        {isLoading ? (
          <div className="space-y-2 px-6 pb-4">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full rounded" />
            ))}
          </div>
        ) : !data || data.items.length === 0 ? (
          <EmptyState
            icon={BellIcon}
            title="No notifications"
            description="You're all caught up."
            className="py-8"
          />
        ) : (
          <ul className="divide-y divide-border">
            {data.items.map((n) => (
              <li
                key={n.id}
                className={cn(
                  "px-6 py-3 flex items-start gap-3 transition-colors",
                  !n.readAt && "bg-primary/5",
                )}
              >
                <div className="flex-1 min-w-0">
                  <p
                    className={cn(
                      "text-sm truncate",
                      n.readAt ? "font-normal" : "font-medium",
                    )}
                  >
                    {n.title}
                  </p>
                  <p className="text-xs text-muted-foreground line-clamp-1 mt-0.5">
                    {n.body}
                  </p>
                  <p className="text-xs text-muted-foreground mt-0.5">
                    {new Date(n.createdAt).toLocaleDateString("en", {
                      month: "short",
                      day: "numeric",
                    })}
                  </p>
                </div>
                {!n.readAt ? (
                  <div className="flex items-center gap-1.5 shrink-0">
                    <Badge variant="default" className="">
                      New
                    </Badge>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-6 w-6"
                      aria-label="Mark as read"
                      disabled={markRead.isPending}
                      onClick={() => markRead.mutate(n.id)}
                    >
                      <CheckIcon className="h-3.5 w-3.5" />
                    </Button>
                  </div>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
