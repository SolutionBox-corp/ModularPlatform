import { notFound } from "next/navigation";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { billingQueries } from "@/features/billing/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { CreditBalanceCard, SubscriptionCard } from "@/features/billing/components/cards";
import { PackagesGrid } from "@/features/billing/components/packages-grid";
import { CreditSummaryTable } from "@/features/billing/components/credit-summary-table";
import { PromoCodeInput } from "@/features/billing/components/promo-code-input";
import { Separator } from "@/components/ui/separator";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Billing — ModularPlatform",
};

export default async function BillingPage() {
  const queryClient = getQueryClient();

  // Guard: the layout already awaited this query; fetchQuery reuses the cached result.
  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "billing")) notFound();

  // Prefetch all billing data in parallel — no await, streamed via HydrationBoundary.
  void queryClient.prefetchQuery(billingQueries.balance());
  void queryClient.prefetchQuery(billingQueries.subscriptionMe());
  void queryClient.prefetchQuery(billingQueries.packages());
  void queryClient.prefetchQuery(billingQueries.subscriptionPlans());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-8">
        {/* Page header */}
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Billing</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Manage your credits, subscription, and payment settings.
          </p>
        </div>

        {/* Summary cards */}
        <div className="grid gap-4 sm:grid-cols-2">
          <CreditBalanceCard />
          <SubscriptionCard />
        </div>

        <Separator />

        {/* Credit packages */}
        <section className="space-y-4">
          <div>
            <h2 className="text-base font-semibold">Buy credits</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              One-time top-ups processed securely via Stripe.
            </p>
          </div>
          <PackagesGrid />
        </section>

        <Separator />

        {/* Promo code */}
        <section className="space-y-4 max-w-sm">
          <div>
            <h2 className="text-base font-semibold">Promo code</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              Have a discount code? Verify it here before checkout.
            </p>
          </div>
          <PromoCodeInput />
        </section>

        <Separator />

        {/* Credit balance breakdown */}
        <section className="space-y-4">
          <div>
            <h2 className="text-base font-semibold">Credit balance</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              Authoritative balance projection from the ledger.
            </p>
          </div>
          <CreditSummaryTable />
        </section>
      </div>
    </HydrationBoundary>
  );
}
