import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { ApiError } from "@/lib/api/types";
import { queryRoots } from "@/lib/api/query-keys";

// ---------------------------------------------------------------------------
// Response shapes (mirrored from backend C# records — camelCase)
// ---------------------------------------------------------------------------

export interface CreditBalanceResponse {
  accountId: string;
  userId: string;
  /** Total ever posted (integer credits). */
  posted: number;
  /** Available to spend right now (posted minus pending holds). */
  available: number;
}

export interface CreditPackageResponse {
  id: string;
  name: string;
  creditAmount: number;
  price: number;
  currency: string;
  /** null means credits don't expire. */
  bucketExpiryDays: number | null;
}

export interface SubscriptionPlanResponse {
  planKey: string;
  creditsPerPeriod: number;
  bucketExpiryDays: number | null;
}

export interface SubscriptionResponse {
  id: string;
  planKey: string;
  /** "Active" | "PastDue" | "Canceled" | "Trialing" | ... */
  status: string;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
}

export interface PurchaseCreditPackageResponse {
  purchaseId: string;
  checkoutSessionId: string;
  checkoutUrl: string;
}

export interface CreateSubscriptionCheckoutResponse {
  checkoutSessionId: string;
  checkoutUrl: string;
}

export interface CancelSubscriptionResponse {
  subscriptionId: string;
  status: string;
  cancelAtPeriodEnd: boolean;
}

export interface PromoCodeResponse {
  code: string;
  percentOff: number | null;
  amountOff: number | null;
  currency: string | null;
}

// ---------------------------------------------------------------------------
// Query factories — all keys extend queryRoots.billing
// ---------------------------------------------------------------------------

export const billingQueries = {
  /** GET /v1/billing/credits/balance */
  balance: () =>
    queryOptions({
      queryKey: [...queryRoots.billing, "balance"],
      queryFn: () => apiFetch<CreditBalanceResponse>("billing/credits/balance"),
      staleTime: 30_000,
    }),

  /** GET /v1/billing/packages */
  packages: () =>
    queryOptions({
      queryKey: [...queryRoots.billing, "packages"],
      queryFn: () =>
        apiFetch<CreditPackageResponse[]>("billing/packages"),
      staleTime: 5 * 60_000,
    }),

  /** GET /v1/billing/subscriptions/plans */
  subscriptionPlans: () =>
    queryOptions({
      queryKey: [...queryRoots.billing, "subscription-plans"],
      queryFn: () =>
        apiFetch<SubscriptionPlanResponse[]>("billing/subscriptions/plans"),
      staleTime: 5 * 60_000,
    }),

  /** GET /v1/billing/subscriptions/me. A 404 means "no subscription" — a valid empty
   *  state, NOT an error to toast — so it resolves to null instead of throwing. */
  subscriptionMe: () =>
    queryOptions({
      queryKey: [...queryRoots.billing, "subscription"],
      queryFn: async (): Promise<SubscriptionResponse | null> => {
        try {
          return await apiFetch<SubscriptionResponse>("billing/subscriptions/me");
        } catch (error) {
          if (error instanceof ApiError && error.status === 404) return null;
          throw error;
        }
      },
      staleTime: 60_000,
    }),

  /** Backwards-compat alias used by dashboard. */
  subscription: () => billingQueries.subscriptionMe(),

  /** GET /v1/billing/promo-codes/{code}/validate */
  promoCode: (code: string) =>
    queryOptions({
      queryKey: [...queryRoots.billing, "promo-codes", code],
      queryFn: () =>
        apiFetch<PromoCodeResponse>(
          `billing/promo-codes/${encodeURIComponent(code)}/validate`,
        ),
      staleTime: 60_000,
      retry: false,
    }),
};

// ---------------------------------------------------------------------------
// Mutation functions (called from hooks.ts)
// ---------------------------------------------------------------------------

/** POST /v1/billing/packages/{packageId}/checkout → Stripe checkout URL */
export function checkoutPackage(
  packageId: string,
): Promise<PurchaseCreditPackageResponse> {
  return apiFetch<PurchaseCreditPackageResponse>(
    `billing/packages/${packageId}/checkout`,
    { method: "POST" },
  );
}

/** POST /v1/billing/subscriptions/checkout → Stripe checkout URL */
export function subscribeCheckout(
  planKey: string,
): Promise<CreateSubscriptionCheckoutResponse> {
  return apiFetch<CreateSubscriptionCheckoutResponse>(
    "billing/subscriptions/checkout",
    { method: "POST", body: { planKey } },
  );
}

/** POST /v1/billing/subscriptions/cancel */
export function cancelSubscription(): Promise<CancelSubscriptionResponse> {
  return apiFetch<CancelSubscriptionResponse>(
    "billing/subscriptions/cancel",
    { method: "POST" },
  );
}
