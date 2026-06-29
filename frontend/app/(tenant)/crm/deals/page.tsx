import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { crmQueries } from "@/features/crm/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { DealsTable } from "@/features/crm/components/deals-table";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("crm");
  return { title: t("deals.metaTitle") };
}

export default async function CrmDealsPage() {
  const t = await getTranslations("crm");
  const queryClient = getQueryClient();

  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "crm")) notFound();

  void queryClient.prefetchQuery(crmQueries.deals({ page: 1, pageSize: 20 }));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">{t("deals.pageHeading")}</h1>
          <p className="text-sm text-muted-foreground mt-0.5">{t("deals.pageDescription")}</p>
        </div>

        <DealsTable />
      </div>
    </HydrationBoundary>
  );
}
