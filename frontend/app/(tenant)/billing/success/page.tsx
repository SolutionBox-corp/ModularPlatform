import type { Metadata } from "next";
import { Suspense } from "react";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { PurchaseSuccessContent } from "@/features/billing/components/purchase-return";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { getQueryClient } from "@/lib/api/query-client";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("billing");
  return { title: t("purchaseReturn.metaTitle") };
}

export default async function BillingSuccessPage() {
  const queryClient = getQueryClient();
  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "billing")) notFound();

  return (
    <div className="mx-auto max-w-2xl">
      <Suspense>
        <PurchaseSuccessContent />
      </Suspense>
    </div>
  );
}
