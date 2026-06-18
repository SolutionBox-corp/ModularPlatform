import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { platformQueries } from "@/features/platform/api";
import { PlatformBillingCard } from "@/features/platform/components/platform-billing-card";
import { ProvisionTenantDialog } from "@/features/platform/components/provision-tenant-dialog";
import { TenantEntitlementEditor } from "@/features/platform/components/tenant-entitlement-editor";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Platform — ModularPlatform",
};

export default async function PlatformOverviewPage() {
  const queryClient = getQueryClient();

  // Prefetch billing status for the admin's own tenant.
  void queryClient.prefetchQuery(platformQueries.billingStatus());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-xl font-semibold tracking-tight">
              Platform administration
            </h1>
            <p className="text-sm text-muted-foreground mt-0.5">
              Provision tenants, manage module entitlements, and monitor
              platform-plane billing.
            </p>
          </div>
          <ProvisionTenantDialog />
        </div>

        {/* Top stats */}
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <PlatformBillingCard />
        </div>

        {/* Entitlement editor (by ID — no list endpoint available yet) */}
        <TenantEntitlementEditor />
      </div>
    </HydrationBoundary>
  );
}
