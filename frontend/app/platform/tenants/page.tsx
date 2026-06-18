import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { platformQueries } from "@/features/platform/api";
import { ProvisionTenantDialog } from "@/features/platform/components/provision-tenant-dialog";
import { TenantsContent } from "@/features/platform/components/tenants-content";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Tenants — Platform Admin",
};

export default async function TenantsPage() {
  const queryClient = getQueryClient();

  // Prefetch the platform billing status (shows operator's own plan).
  void queryClient.prefetchQuery(platformQueries.billingStatus());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-xl font-semibold tracking-tight">Tenants</h1>
            <p className="text-sm text-muted-foreground mt-0.5">
              Provision new tenants and manage their module entitlements. Use
              the tenant ID from the provision response to open the entitlement
              editor.
            </p>
          </div>
          <ProvisionTenantDialog />
        </div>

        <TenantsContent />
      </div>
    </HydrationBoundary>
  );
}
