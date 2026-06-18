import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { platformQueries } from "@/features/platform/api";
import { TenantDetailContent } from "@/features/platform/components/tenant-detail-content";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Tenant detail — Platform Admin",
};

interface PageProps {
  params: Promise<{ tenantId: string }>;
}

/**
 * Platform-admin tenant detail view.
 * Renders the EntitlementToggles + CreateInviteDialog + plan summary for a
 * specific tenant, identified by UUID in the URL segment.
 *
 * NOTE: No GET /tenant/admin/tenants/{id} endpoint exists on the backend yet;
 * this page uses GET /tenant/admin/platform-billing which is tenant-scoped to
 * the token's tenant. It can display a full editor for the tenant whose UUID is
 * in the path if that tenant matches the token's context.
 * Cross-tenant read (arbitrary UUID) requires a backend endpoint not yet built.
 */
export default async function TenantDetailPage({ params }: PageProps) {
  const { tenantId } = await params;
  const queryClient = getQueryClient();

  void queryClient.prefetchQuery(platformQueries.billingStatus());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <TenantDetailContent tenantId={tenantId} />
    </HydrationBoundary>
  );
}
