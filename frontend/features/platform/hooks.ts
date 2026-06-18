"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryRoots } from "@/lib/api/query-keys";
import {
  platformQueries,
  provisionTenant,
  setEntitlement,
  createTenantInvite,
} from "./api";

export function usePlatformBillingStatus() {
  return useQuery(platformQueries.billingStatus());
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
        queryKey: queryRoots.admin,
      });
      toast.success(
        `${variables.moduleKey} ${variables.enabled ? "enabled" : "disabled"}.`,
      );
    },
  });
}

export function useCreateTenantInvite() {
  return useMutation({
    mutationFn: createTenantInvite,
    // No invalidation needed — invite token is shown once and not cached.
  });
}
