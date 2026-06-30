import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import {
  entitlementQueries,
  isModuleEnabled,
} from "@/features/entitlements/api";
import { operationQueries } from "@/features/operations/api";
import { OperationsTable } from "@/features/operations/components/operations-table";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("operations");
  return { title: t("metaTitle") };
}

export default async function OperationsPage() {
  const t = await getTranslations("operations");
  const queryClient = getQueryClient();

  const entitlements = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(entitlements, "operations")) notFound();

  void queryClient.prefetchQuery(operationQueries.list(1, 20));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            {t("page.heading")}
          </h1>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {t("page.description")}
          </p>
        </div>

        <OperationsTable />
      </div>
    </HydrationBoundary>
  );
}
