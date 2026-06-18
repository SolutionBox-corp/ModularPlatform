"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { BellIcon, CheckIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Pagination,
  PaginationContent,
  PaginationItem,
  PaginationNext,
  PaginationPrevious,
  PaginationLink,
} from "@/components/ui/pagination";
import { EmptyState } from "@/components/app/empty-state";
import { notificationQueries } from "@/features/notifications/api";
import { useMarkNotificationRead } from "@/features/notifications/hooks";
import { cn } from "@/lib/utils";

const PAGE_SIZE = 20;

/**
 * Full paged notifications feed.
 * - Page state is local (no URL — user stays on the same page after mark-read).
 * - Unread items are visually distinguished and each has a mark-read button.
 * - Stays live via the RealtimeProvider (event-map invalidates notifications root).
 */
export function NotificationsFeed() {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useQuery(
    notificationQueries.feed({ page, pageSize: PAGE_SIZE }),
  );
  const markRead = useMarkNotificationRead();

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1;

  return (
    <div className="space-y-4">
      {isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-lg" />
          ))}
        </div>
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={BellIcon}
          title="No notifications"
          description="You're all caught up — no notifications yet."
        />
      ) : (
        <>
          <ul className="divide-y divide-border rounded-lg border border-border overflow-hidden">
            {data.items.map((n) => (
              <li
                key={n.id}
                className={cn(
                  "flex items-start gap-4 px-5 py-4 transition-colors",
                  !n.readAt && "bg-primary/5",
                )}
              >
                {/* Unread dot */}
                <span
                  className={cn(
                    "mt-1.5 h-2 w-2 shrink-0 rounded-full",
                    n.readAt ? "bg-transparent" : "bg-primary",
                  )}
                  aria-hidden="true"
                />

                <div className="flex-1 min-w-0 space-y-0.5">
                  <div className="flex items-center gap-2">
                    <p
                      className={cn(
                        "text-sm leading-snug",
                        n.readAt ? "font-normal" : "font-semibold",
                      )}
                    >
                      {n.title}
                    </p>
                    {!n.readAt && (
                      <Badge variant="default" className="shrink-0">
                        New
                      </Badge>
                    )}
                  </div>
                  <p className="text-sm text-muted-foreground leading-snug">
                    {n.body}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {new Date(n.createdAt).toLocaleString("en", {
                      month: "short",
                      day: "numeric",
                      hour: "2-digit",
                      minute: "2-digit",
                    })}
                  </p>
                </div>

                {!n.readAt && (
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground"
                    aria-label="Mark as read"
                    disabled={markRead.isPending}
                    onClick={() => markRead.mutate(n.id)}
                  >
                    <CheckIcon className="h-4 w-4" />
                  </Button>
                )}
              </li>
            ))}
          </ul>

          {totalPages > 1 && (
            <Pagination>
              <PaginationContent>
                <PaginationItem>
                  <PaginationPrevious
                    aria-disabled={page === 1}
                    onClick={page > 1 ? () => setPage((p) => p - 1) : undefined}
                    className={cn(page === 1 && "pointer-events-none opacity-50")}
                  />
                </PaginationItem>

                {Array.from({ length: totalPages }, (_, i) => i + 1).map(
                  (p) => (
                    <PaginationItem key={p}>
                      <PaginationLink
                        isActive={p === page}
                        onClick={() => setPage(p)}
                      >
                        {p}
                      </PaginationLink>
                    </PaginationItem>
                  ),
                )}

                <PaginationItem>
                  <PaginationNext
                    aria-disabled={page === totalPages}
                    onClick={
                      page < totalPages
                        ? () => setPage((p) => p + 1)
                        : undefined
                    }
                    className={cn(
                      page === totalPages && "pointer-events-none opacity-50",
                    )}
                  />
                </PaginationItem>
              </PaginationContent>
            </Pagination>
          )}
        </>
      )}
    </div>
  );
}
