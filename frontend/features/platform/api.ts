import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";
import type { UserAuditTrailResponse } from "@/features/identity-admin/api";

// ─── Response shapes (mirroring backend records) ───────────────────────────

export interface ProvisionTenantResponse {
  tenantId: string;
}

/** A row of GET /v1/identity/platform/users. */
export interface PlatformUserItem {
  userId: string;
  email: string;
  displayName: string;
  tenantId: string;
  createdAt: string;
}

/** Envelope of GET /v1/identity/platform/users — limit/offset paged. */
export interface PlatformUsersResponse {
  items: PlatformUserItem[];
  total: number;
  limit: number;
  offset: number;
}

export interface ListPlatformUsersParams {
  tenantId?: string;
  limit?: number;
  offset?: number;
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

/** A row of GET /v1/tenant/admin/tenants (cross-tenant registry list). */
export interface PlatformTenantItem {
  tenantId: string;
  subdomain: string;
  name: string;
  status: string;
  placement: string;
  createdAt: string;
}

/** Envelope of GET /v1/tenant/admin/tenants — limit/offset paged. */
export interface PlatformTenantsResponse {
  items: PlatformTenantItem[];
  total: number;
  limit: number;
  offset: number;
}

export interface ListPlatformTenantsParams {
  limit?: number;
  offset?: number;
}

/** GET /v1/tenant/admin/tenants/{id} — registry row + the tenant's PERSISTED module entitlements. */
export interface TenantDetail {
  tenantId: string;
  subdomain: string;
  name: string;
  status: string;
  placement: string;
  createdAt: string;
  modules: PlatformBillingModuleView[];
}

/** A row of GET /v1/billing/admin/packages — the full catalogue (active + inactive). */
export interface AdminCreditPackage {
  id: string;
  name: string;
  creditAmount: number;
  price: number;
  currency: string;
  bucketExpiryDays: number | null;
  active: boolean;
  stripePriceId: string | null;
}

/** POST /v1/billing/admin/packages body. */
export interface CreatePackageInput {
  name: string;
  creditAmount: number;
  price: number;
  currency: string;
  bucketExpiryDays: number | null;
  active: boolean;
  stripePriceId: string | null;
}

/** PUT /v1/billing/admin/packages/{id} body (currency is immutable). */
export interface UpdatePackageInput {
  name: string;
  creditAmount: number;
  price: number;
  bucketExpiryDays: number | null;
  active: boolean;
  stripePriceId: string | null;
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

  /**
   * GET /v1/identity/platform/users?tenantId&limit&offset
   * Platform-wide user list (cross-tenant). Requires platform.users.list.
   */
  users: ({ tenantId, limit = 50, offset = 0 }: ListPlatformUsersParams = {}) =>
    queryOptions({
      queryKey: [
        ...queryRoots.admin,
        "platform",
        "users",
        tenantId ?? null,
        limit,
        offset,
      ],
      queryFn: () => {
        const sp = new URLSearchParams({
          limit: String(limit),
          offset: String(offset),
        });
        if (tenantId) sp.set("tenantId", tenantId);
        return apiFetch<PlatformUsersResponse>(
          `identity/platform/users?${sp.toString()}`,
        );
      },
      staleTime: 30_000,
    }),

  /**
   * GET /v1/tenant/admin/tenants?limit&offset
   * Cross-tenant registry list. Requires platform.tenants.manage.
   */
  tenants: ({ limit = 50, offset = 0 }: ListPlatformTenantsParams = {}) =>
    queryOptions({
      queryKey: [...queryRoots.admin, "platform", "tenants", limit, offset],
      queryFn: () => {
        const sp = new URLSearchParams({ limit: String(limit), offset: String(offset) });
        return apiFetch<PlatformTenantsResponse>(`tenant/admin/tenants?${sp.toString()}`);
      },
      staleTime: 30_000,
    }),

  /**
   * GET /v1/tenant/admin/tenants/{id}
   * One tenant's registry row + its PERSISTED entitlements. Requires platform.tenants.manage.
   */
  tenantById: (tenantId: string) =>
    queryOptions({
      queryKey: [...queryRoots.admin, "platform", "tenants", tenantId],
      queryFn: () => apiFetch<TenantDetail>(`tenant/admin/tenants/${tenantId}`),
      enabled: tenantId.trim().length > 0,
      staleTime: 30_000,
    }),

  /**
   * GET /v1/billing/admin/packages
   * Full credit-package catalogue (active + inactive). Requires billing.manage.
   */
  adminPackages: () =>
    queryOptions({
      queryKey: [...queryRoots.admin, "platform", "packages"],
      queryFn: () => apiFetch<AdminCreditPackage[]>("billing/admin/packages"),
      staleTime: 30_000,
    }),

  /**
   * GET /v1/identity/platform/users/{userId}/audit
   * Same UserAuditTrailResponse shape as the per-tenant audit view.
   * Requires audit.read.
   */
  userAudit: (userId: string) =>
    queryOptions({
      queryKey: [...queryRoots.admin, "platform", "users", userId, "audit"],
      queryFn: () =>
        apiFetch<UserAuditTrailResponse>(
          `identity/platform/users/${userId}/audit`,
        ),
      enabled: userId.trim().length > 0,
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

/** POST /v1/billing/admin/packages — create a credit package. */
export async function createCreditPackage(
  input: CreatePackageInput,
): Promise<{ id: string }> {
  return apiFetch<{ id: string }>("billing/admin/packages", {
    method: "POST",
    body: input,
  });
}

/** PUT /v1/billing/admin/packages/{id} — update a credit package (also used to (de)activate). */
export async function updateCreditPackage(
  id: string,
  input: UpdatePackageInput,
): Promise<{ id: string; active: boolean }> {
  return apiFetch<{ id: string; active: boolean }>(
    `billing/admin/packages/${id}`,
    { method: "PUT", body: input },
  );
}
