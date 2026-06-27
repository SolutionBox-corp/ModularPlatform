import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { crmQueries } from "@/features/crm/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { ContactsTable } from "@/features/crm/components/contacts-table";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("crm");
  return { title: t("page.metaTitle") };
}

export default async function CrmPage() {
  const t = await getTranslations("crm");
  const queryClient = getQueryClient();

  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "crm")) notFound();

  void queryClient.prefetchQuery(crmQueries.contacts({ page: 1, pageSize: 20 }));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">{t("page.heading")}</h1>
          <p className="text-sm text-muted-foreground mt-0.5">{t("page.description")}</p>
        </div>

        <ContactsTable />
      </div>
    </HydrationBoundary>
  );
}
