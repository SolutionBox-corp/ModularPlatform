"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryRoots } from "@/lib/api/query-keys";
import {
  platformQueries,
  provisionTenant,
  setEntitlement,
  createTenantInvite,
  revokeTenantInvite,
  createCreditPackage,
  updateCreditPackage,
  type ListTenantInvitesParams,
  type ListPlatformUsersParams,
  type ListPlatformTenantsParams,
  type CreatePackageInput,
  type UpdatePackageInput,
} from "./api";

export function usePlatformBillingStatus() {
  return useQuery(platformQueries.billingStatus());
}

export function useListPlatformUsers(params: ListPlatformUsersParams = {}) {
  return useQuery(platformQueries.users(params));
}

export function useListPlatformTenants(params: ListPlatformTenantsParams = {}) {
  return useQuery(platformQueries.tenants(params));
}

/** One tenant's registry row + persisted entitlements. An empty id keeps the query disabled. */
export function useTenantDetail(tenantId: string) {
  return useQuery(platformQueries.tenantById(tenantId));
}

export function useTenantInvites(params: ListTenantInvitesParams) {
  return useQuery(platformQueries.tenantInvites(params));
}

export function usePlatformUserAudit(userId: string) {
  return useQuery(platformQueries.userAudit(userId));
}

export function useProvisionTenant() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: provisionTenant,
    onSuccess: () => {
      // Invalidate admin root so any future list or status queries refresh.
      void queryClient.invalidateQueries({ queryKey: queryRoots.admin });
      toast.success("Tenant provisioned successfully.");
    },
  });
}

export function useSetEntitlement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: setEntitlement,
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.admin, "entitlements", variables.tenantId],
      });
      void queryClient.invalidateQueries({
        queryKey: platformQueries.tenantById(variables.tenantId).queryKey,
      });
      void queryClient.invalidateQueries({
        queryKey: queryRoots.admin,
      });
      void queryClient.invalidateQueries({
        queryKey: queryRoots.entitlements,
      });
      toast.success(
        `${variables.moduleKey} ${variables.enabled ? "enabled" : "disabled"}.`,
      );
    },
  });
}

export function useCreateTenantInvite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createTenantInvite,
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.admin, "platform", "tenants", variables.tenantId, "invites"],
      });
    },
  });
}

export function useRevokeTenantInvite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: revokeTenantInvite,
    onSuccess: (_data, variables) => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.admin, "platform", "tenants", variables.tenantId, "invites"],
      });
      toast.success("Invite revoked.");
    },
  });
}

export function useAdminPackages() {
  return useQuery(platformQueries.adminPackages());
}

export function useCreatePackage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreatePackageInput) => createCreditPackage(input),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.admin, "platform", "packages"],
      });
      toast.success("Package created.");
    },
  });
}

export function useUpdatePackage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdatePackageInput }) =>
      updateCreditPackage(id, input),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.admin, "platform", "packages"],
      });
      toast.success("Package updated.");
    },
  });
}
