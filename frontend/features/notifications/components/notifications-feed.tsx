"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocale, useTranslations } from "next-intl";
import { toast } from "sonner";
import { BellIcon, CheckIcon, CheckCheckIcon } from "lucide-react";
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
import {
  useMarkNotificationRead,
  useMarkAllNotificationsRead,
  useUnreadNotificationCount,
} from "@/features/notifications/hooks";
import { cn } from "@/lib/utils";

const PAGE_SIZE = 20;

/**
 * Full paged notifications feed.
 * - Page state is local (no URL — user stays on the same page after mark-read).
 * - Unread items are visually distinguished and each has a mark-read button.
 * - Stays live via the RealtimeProvider (event-map invalidates notifications root).
 */
export function NotificationsFeed() {
  const t = useTranslations("notifications");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const { data, isLoading } = useQuery(
    notificationQueries.feed({ page, pageSize: PAGE_SIZE, unreadOnly }),
  );
  const markRead = useMarkNotificationRead();
  const markAll = useMarkAllNotificationsRead();
  const { data: unread } = useUnreadNotificationCount();
  const unreadCount = unread?.count ?? 0;

  const totalPages = data ? Math.ceil(data.totalCount / PAGE_SIZE) : 1;

  function handleMarkAll() {
    markAll.mutate(undefined, {
      onSuccess: (result) =>
        toast.success(t("markAll.done", { count: result.marked })),
    });
  }

  function handleFilterChange(next: boolean) {
    setUnreadOnly(next);
    setPage(1); // a new filter resets to the first page
  }

  return (
    <div className="space-y-4">
      {/* Toolbar: All/Unread filter + mark-all — always visible so the user can switch back. */}
      <div className="flex items-center justify-between gap-2">
        <div className="inline-flex rounded-lg border border-border p-0.5 text-xs">
          <button
            type="button"
            onClick={() => handleFilterChange(false)}
            aria-pressed={!unreadOnly}
            className={cn(
              "rounded-md px-2.5 py-1 font-medium transition-colors",
              !unreadOnly
                ? "bg-muted text-foreground"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {t("filter.all")}
          </button>
          <button
            type="button"
            onClick={() => handleFilterChange(true)}
            aria-pressed={unreadOnly}
            className={cn(
              "rounded-md px-2.5 py-1 font-medium transition-colors",
              unreadOnly
                ? "bg-muted text-foreground"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {t("filter.unread")}
            {unreadCount > 0 && (
              <span className="ml-1 text-muted-foreground">({unreadCount})</span>
            )}
          </button>
        </div>
        <Button
          variant="ghost"
          size="sm"
          className="h-7 gap-1.5 text-xs text-muted-foreground hover:text-foreground"
          disabled={unreadCount === 0 || markAll.isPending}
          onClick={handleMarkAll}
        >
          <CheckCheckIcon className="h-3.5 w-3.5" />
          {markAll.isPending ? t("markAll.pending") : t("markAll.action")}
        </Button>
      </div>

      {isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-lg" />
          ))}
        </div>
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={BellIcon}
          title={t(unreadOnly ? "empty.noUnreadTitle" : "empty.title")}
          description={t(
            unreadOnly ? "empty.noUnreadDescription" : "empty.feedDescription",
          )}
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
                        {t("badge.new")}
                      </Badge>
                    )}
                  </div>
                  <p className="text-sm text-muted-foreground leading-snug">
                    {n.body}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {new Date(n.createdAt).toLocaleString(locale, {
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
                    aria-label={t("markRead.ariaLabel", { title: n.title })}
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
                    tabIndex={page === 1 ? -1 : undefined}
                    onClick={page > 1 ? () => setPage((p) => p - 1) : (e) => e.preventDefault()}
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
                    tabIndex={page === totalPages ? -1 : undefined}
                    onClick={
                      page < totalPages
                        ? () => setPage((p) => p + 1)
                        : (e) => e.preventDefault()
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
