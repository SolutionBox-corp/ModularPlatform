import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { platformQueries } from "@/features/platform/api";
import { ProvisionTenantDialog } from "@/features/platform/components/provision-tenant-dialog";
import { TenantsContent } from "@/features/platform/components/tenants-content";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("platform");
  return {
    title: t("meta.tenantsTitle"),
  };
}

export default async function TenantsPage() {
  const t = await getTranslations("platform");
  const queryClient = getQueryClient();

  // Prefetch the platform billing status (operator's own plan) + the first page of tenants for the table.
  void queryClient.prefetchQuery(platformQueries.billingStatus());
  void queryClient.prefetchQuery(platformQueries.tenants());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-xl font-semibold tracking-tight">
              {t("tenants.heading")}
            </h1>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("tenants.description")}
            </p>
          </div>
          <ProvisionTenantDialog />
        </div>

        <TenantsContent />
      </div>
    </HydrationBoundary>
  );
}
