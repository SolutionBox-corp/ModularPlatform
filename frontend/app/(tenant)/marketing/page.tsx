import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { marketingQueries } from "@/features/marketing/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { PullPanel } from "@/features/marketing/components/pull-panel";
import { SnapshotsTable } from "@/features/marketing/components/snapshots-table";
import { AnalysesList } from "@/features/marketing/components/analyses-list";
import { VibeChat } from "@/features/marketing/components/vibe-chat";
import { Separator } from "@/components/ui/separator";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("marketing");
  return { title: t("metaTitle") };
}

export default async function MarketingPage() {
  const t = await getTranslations("marketing");
  const queryClient = getQueryClient();

  // Guard: the layout already awaited this query; fetchQuery reuses the cached result.
  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "marketing")) notFound();

  // Prefetch the panels' data in parallel — streamed via HydrationBoundary.
  void queryClient.prefetchQuery(marketingQueries.pulls());
  void queryClient.prefetchQuery(marketingQueries.analyses());
  void queryClient.prefetchQuery(marketingQueries.vibeConversations());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-8">
        {/* Page header */}
        <div>
          <h1 className="text-xl font-semibold tracking-tight">{t("page.heading")}</h1>
          <p className="text-sm text-muted-foreground mt-0.5">{t("page.description")}</p>
        </div>

        {/* Data pulls */}
        <PullPanel />

        <Separator />

        {/* Metric snapshots */}
        <section className="space-y-4">
          <div>
            <h2 className="text-base font-semibold">{t("snapshots.heading")}</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("snapshots.description")}
            </p>
          </div>
          <SnapshotsTable />
        </section>

        <Separator />

        {/* AI analyses */}
        <section className="space-y-4">
          <div>
            <h2 className="text-base font-semibold">{t("analyses.heading")}</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("analyses.description")}
            </p>
          </div>
          <AnalysesList />
        </section>

        <Separator />

        {/* Vibe AI chat */}
        <section className="space-y-4">
          <div>
            <h2 className="text-base font-semibold">{t("vibeChat.heading")}</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("vibeChat.description")}
            </p>
          </div>
          <VibeChat />
        </section>
      </div>
    </HydrationBoundary>
  );
}
