import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { crmQueries } from "@/features/crm/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { TasksTable } from "@/features/crm/components/tasks-table";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("crm");
  return { title: t("tasks.metaTitle") };
}

export default async function CrmTasksPage() {
  const t = await getTranslations("crm");
  const queryClient = getQueryClient();

  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "crm")) notFound();

  void queryClient.prefetchQuery(crmQueries.tasks({ page: 1, pageSize: 20, status: "open" }));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <p className="text-sm text-muted-foreground">{t("tasks.pageDescription")}</p>

        <TasksTable />
      </div>
    </HydrationBoundary>
  );
}
