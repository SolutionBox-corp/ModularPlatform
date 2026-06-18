import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";

/**
 * Per-module entitlement view — mirrors TenantEntitlementsView /
 * ModuleEntitlementView from ModularPlatform.Abstractions/Ports.cs.
 */
export interface ModuleEntitlementView {
  key: string;
  enabled: boolean;
  tier: string | null;
}

export interface TenantEntitlementsView {
  tenantId: string;
  tier: string | null;
  modules: ModuleEntitlementView[];
}

export const entitlementQueries = {
  /** GET /v1/tenant/me/entitlements — the single nav source of truth. */
  me: () =>
    queryOptions({
      queryKey: [...queryRoots.entitlements],
      queryFn: () => apiFetch<TenantEntitlementsView>("tenant/me/entitlements"),
      staleTime: 60_000,
    }),
};

/** Returns true when the tenant has a given module enabled. */
export function isModuleEnabled(
  entitlements: TenantEntitlementsView | undefined,
  moduleKey: string,
): boolean {
  if (!entitlements) return false;
  const target = moduleKey.toLowerCase();
  return entitlements.modules.some((m) => m.key.toLowerCase() === target && m.enabled);
}
