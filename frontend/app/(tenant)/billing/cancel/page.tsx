import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PurchaseCancelContent } from "@/features/billing/components/purchase-return";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { getQueryClient } from "@/lib/api/query-client";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("billing");
  return { title: t("purchaseCancel.metaTitle") };
}

export default async function BillingCancelPage() {
  const queryClient = getQueryClient();
  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "billing")) notFound();

  return (
    <div className="mx-auto max-w-2xl">
      <PurchaseCancelContent />
    </div>
  );
}
