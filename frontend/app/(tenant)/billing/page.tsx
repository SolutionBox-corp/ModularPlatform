import { notFound } from "next/navigation";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { billingQueries } from "@/features/billing/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { CreditBalanceCard, SubscriptionCard } from "@/features/billing/components/cards";
import { SubscriptionPlans } from "@/features/billing/components/subscription-plans";
import { PackagesGrid } from "@/features/billing/components/packages-grid";
import { CreditSummaryTable } from "@/features/billing/components/credit-summary-table";
import { CreditLedgerTable } from "@/features/billing/components/credit-ledger-table";
import { PromoCodeInput } from "@/features/billing/components/promo-code-input";
import { Separator } from "@/components/ui/separator";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("billing");
  return { title: t("metaTitle") };
}

export default async function BillingPage() {
  const t = await getTranslations("billing");
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
          <h1 className="text-xl font-semibold tracking-tight">
            {t("header.title")}
          </h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            {t("header.description")}
          </p>
        </div>

        {/* Summary cards */}
        <div className="grid gap-4 sm:grid-cols-2">
          <CreditBalanceCard />
          <SubscriptionCard />
        </div>

        <Separator />

        {/* Subscription plans */}
        <section id="plans" className="space-y-4 scroll-mt-20">
          <div>
            <h2 className="text-base font-semibold">{t("plans.heading")}</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("plans.description")}
            </p>
          </div>
          <SubscriptionPlans />
        </section>

        <Separator />

        {/* Credit packages */}
        <section className="space-y-4">
          <div>
            <h2 className="text-base font-semibold">{t("buyCredits.heading")}</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("buyCredits.description")}
            </p>
          </div>
          <PackagesGrid />
        </section>

        <Separator />

        {/* Promo code */}
        <section className="space-y-4 max-w-sm">
          <div>
            <h2 className="text-base font-semibold">{t("promo.heading")}</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("promo.description")}
            </p>
          </div>
          <PromoCodeInput />
        </section>

        <Separator />

        {/* Credit balance breakdown */}
        <section className="space-y-4">
          <div>
            <h2 className="text-base font-semibold">{t("balance.heading")}</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("balance.description")}
            </p>
          </div>
          <CreditSummaryTable />
        </section>

        <Separator />

        {/* Credit transaction ledger */}
        <section className="space-y-4">
          <div>
            <h2 className="text-base font-semibold">{t("ledger.heading")}</h2>
            <p className="text-sm text-muted-foreground mt-0.5">
              {t("ledger.description")}
            </p>
          </div>
          <CreditLedgerTable />
        </section>
      </div>
    </HydrationBoundary>
  );
}
