import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";

// ─── Response shapes (mirroring backend records) ───────────────────────────

export interface ProvisionTenantResponse {
  tenantId: string;
}

export interface SetEntitlementResponse {
  tenantId: string;
  moduleKey: string;
  enabled: boolean;
}

export interface CreateTenantInviteResponse {
  inviteToken: string;
  expiresAt: string;
}

export interface PlatformBillingModuleView {
  key: string;
  enabled: boolean;
  tier: string | null;
}

export interface PlatformBillingStatusView {
  tenantId: string;
  plan: string;
  modules: PlatformBillingModuleView[];
}

// ─── Query factories ────────────────────────────────────────────────────────

export const platformQueries = {
  /**
   * GET /v1/tenant/admin/platform-billing
   * Returns the current tenant's platform-plane billing status (plan + module breakdown).
   * Requires platform.tenants.manage.
   */
  billingStatus: () =>
    queryOptions({
      queryKey: [...queryRoots.admin, "platform-billing"],
      queryFn: () =>
        apiFetch<PlatformBillingStatusView>("tenant/admin/platform-billing"),
      staleTime: 30_000,
    }),
};

// ─── Mutation functions ─────────────────────────────────────────────────────

/** POST /v1/tenant/admin/tenants — provision a new tenant. */
export async function provisionTenant(params: {
  name: string;
  subdomain: string;
}): Promise<ProvisionTenantResponse> {
  return apiFetch<ProvisionTenantResponse>("tenant/admin/tenants", {
    method: "POST",
    body: params,
  });
}

/** PUT /v1/tenant/admin/tenants/{tenantId}/entitlements/{moduleKey} — toggle a module entitlement. */
export async function setEntitlement(params: {
  tenantId: string;
  moduleKey: string;
  enabled: boolean;
  tier: string | null;
}): Promise<SetEntitlementResponse> {
  return apiFetch<SetEntitlementResponse>(
    `tenant/admin/tenants/${params.tenantId}/entitlements/${params.moduleKey}`,
    {
      method: "PUT",
      body: { enabled: params.enabled, tier: params.tier },
    },
  );
}

/** POST /v1/tenant/admin/tenants/{tenantId}/invites — mint a single-use invite token. */
export async function createTenantInvite(params: {
  tenantId: string;
  expiresInDays?: number;
}): Promise<CreateTenantInviteResponse> {
  return apiFetch<CreateTenantInviteResponse>(
    `tenant/admin/tenants/${params.tenantId}/invites`,
    {
      method: "POST",
      body: { expiresInDays: params.expiresInDays ?? 7 },
    },
  );
}
