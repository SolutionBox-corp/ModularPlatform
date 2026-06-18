"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { queryRoots } from "@/lib/api/query-keys";
import {
  billingQueries,
  checkoutPackage,
  subscribeCheckout,
  cancelSubscription,
} from "@/features/billing/api";

/**
 * Only navigate to backend-provided checkout URLs that are valid HTTPS URLs
 * on stripe.com. Anything else is treated as a server-side error.
 */
function safeExternalRedirect(url: string): void {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    toast.error("Checkout failed: invalid redirect URL.");
    return;
  }
  if (
    parsed.protocol !== "https:" ||
    !parsed.host.endsWith("stripe.com")
  ) {
    toast.error("Checkout failed: unexpected redirect destination.");
    return;
  }
  window.location.href = url;
}

// ---------------------------------------------------------------------------
// Query hooks
// ---------------------------------------------------------------------------

export function useCreditBalance() {
  return useQuery(billingQueries.balance());
}

export function usePackages() {
  return useQuery(billingQueries.packages());
}

export function useSubscriptionPlans() {
  return useQuery(billingQueries.subscriptionPlans());
}

export function useSubscription() {
  return useQuery(billingQueries.subscriptionMe());
}

export function usePromoCode(code: string, enabled: boolean) {
  return useQuery({ ...billingQueries.promoCode(code), enabled });
}

// ---------------------------------------------------------------------------
// Mutation hooks
// ---------------------------------------------------------------------------

/**
 * Purchase a credit package — on success redirects the browser to the Stripe
 * checkout URL returned by the backend.
 */
export function useCheckoutPackage() {
  return useMutation({
    mutationFn: (packageId: string) => checkoutPackage(packageId),
    onSuccess: (data) => {
      safeExternalRedirect(data.checkoutUrl);
    },
  });
}

/**
 * Start a subscription checkout — on success redirects to Stripe.
 */
export function useSubscribeCheckout() {
  return useMutation({
    mutationFn: (planKey: string) => subscribeCheckout(planKey),
    onSuccess: (data) => {
      safeExternalRedirect(data.checkoutUrl);
    },
  });
}

/**
 * Cancel the active subscription. Invalidates the subscription query so the
 * SubscriptionCard reflects the updated cancelAtPeriodEnd flag immediately.
 */
export function useCancelSubscription() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => cancelSubscription(),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: [...queryRoots.billing, "subscription"],
      });
    },
  });
}
