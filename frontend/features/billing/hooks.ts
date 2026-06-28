"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { queryRoots } from "@/lib/api/query-keys";
import {
  billingQueries,
  checkoutPackage,
  subscribeCheckout,
  cancelSubscription,
  createBillingPortalSession,
  configurePaymentGateway,
  type ConfigurePaymentGatewayInput,
} from "@/features/billing/api";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";

interface CheckoutRedirectMessages {
  invalidRedirect: string;
  unexpectedRedirect: string;
}

/**
 * Only navigate to backend-provided checkout URLs that are valid HTTPS URLs
 * on stripe.com. Anything else is treated as a server-side error.
 */
function safeExternalRedirect(
  url: string,
  messages: CheckoutRedirectMessages,
): void {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    toast.error(messages.invalidRedirect);
    return;
  }
  if (
    parsed.protocol !== "https:" ||
    !parsed.host.endsWith("stripe.com")
  ) {
    toast.error(messages.unexpectedRedirect);
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
  const t = useTranslations("billing");
  return useMutation({
    mutationFn: (packageId: string) => checkoutPackage(packageId),
    onSuccess: (data) => {
      safeExternalRedirect(data.checkoutUrl, {
        invalidRedirect: t("checkout.invalidRedirect"),
        unexpectedRedirect: t("checkout.unexpectedRedirect"),
      });
    },
  });
}

/**
 * Start a subscription checkout — on success redirects to Stripe.
 */
export function useSubscribeCheckout() {
  const t = useTranslations("billing");
  return useMutation({
    mutationFn: (planKey: string) => subscribeCheckout(planKey),
    onSuccess: (data) => {
      safeExternalRedirect(data.checkoutUrl, {
        invalidRedirect: t("checkout.invalidRedirect"),
        unexpectedRedirect: t("checkout.unexpectedRedirect"),
      });
    },
  });
}

/**
 * Open the Stripe Customer Portal (manage payment methods + view/download invoices).
 * On success redirects to Stripe's hosted page; a user with no billing account yet
 * gets a friendly toast instead of an error screen.
 */
export function useBillingPortal() {
  const t = useTranslations("billing");
  return useMutation({
    mutationFn: () => createBillingPortalSession(),
    onSuccess: (data) => {
      safeExternalRedirect(data.url, {
        invalidRedirect: t("checkout.invalidRedirect"),
        unexpectedRedirect: t("checkout.unexpectedRedirect"),
      });
    },
    onError: (err) => {
      if (err instanceof ApiError && err.errorCode === "billing.no_billing_account") {
        toast.error(t("portal.noAccount"));
        return;
      }
      toast.error(
        err instanceof ApiError
          ? toDisplayMessage(err, currentLocale())
          : t("portal.error"),
      );
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

export function useConfigurePaymentGateway() {
  const queryClient = useQueryClient();
  const t = useTranslations("billing");

  return useMutation({
    mutationFn: (input: ConfigurePaymentGatewayInput) =>
      configurePaymentGateway(input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryRoots.billing });
      void queryClient.invalidateQueries({ queryKey: queryRoots.admin });
      toast.success(t("paymentGateway.saved"));
    },
  });
}
