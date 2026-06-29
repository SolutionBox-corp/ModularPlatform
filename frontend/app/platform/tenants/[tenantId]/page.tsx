import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { platformQueries } from "@/features/platform/api";
import { TenantDetailContent } from "@/features/platform/components/tenant-detail-content";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("platform");
  return {
    title: t("meta.tenantDetailTitle"),
  };
}

interface PageProps {
  params: Promise<{ tenantId: string }>;
}

/**
 * Platform-admin tenant detail view.
 * Renders the EntitlementToggles + CreateInviteDialog for a specific tenant,
 * identified by UUID in the URL segment. Prefetches the cross-tenant registry
 * row + persisted entitlements via GET /tenant/admin/tenants/{id} so the
 * entitlement switches hydrate with the real DB state for THAT tenant.
 */
export default async function TenantDetailPage({ params }: PageProps) {
  const { tenantId } = await params;
  const queryClient = getQueryClient();

  void queryClient.prefetchQuery(platformQueries.tenantById(tenantId));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <TenantDetailContent tenantId={tenantId} />
    </HydrationBoundary>
  );
}
