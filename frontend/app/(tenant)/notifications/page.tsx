import { notFound } from "next/navigation";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { notificationQueries } from "@/features/notifications/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { NotificationsFeed } from "@/features/notifications/components/notifications-feed";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("notifications");
  return {
    title: t("page.metaTitle"),
  };
}

export default async function NotificationsPage() {
  const t = await getTranslations("notifications");
  const queryClient = getQueryClient();

  // Guard: the layout already awaited this query; fetchQuery reuses the cached result.
  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "notifications")) notFound();

  // Prefetch the first page so it streams to the browser without a waterfall.
  void queryClient.prefetchQuery(notificationQueries.feed({ page: 1, pageSize: 20 }));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">{t("page.heading")}</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            {t("page.description")}
          </p>
        </div>

        <NotificationsFeed />
      </div>
    </HydrationBoundary>
  );
}
