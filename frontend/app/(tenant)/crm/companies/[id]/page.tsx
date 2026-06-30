import type { Metadata } from "next";
import { notFound } from "next/navigation";
import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { ArrowLeftIcon } from "lucide-react";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { crmQueries } from "@/features/crm/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { CompanyDetail } from "@/features/crm/components/company-detail";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("crm");
  return { title: t("companies.detailMetaTitle") };
}

interface PageProps {
  params: Promise<{ id: string }>;
}

export default async function CompanyDetailPage({ params }: PageProps) {
  const { id } = await params;
  const t = await getTranslations("crm");
  const queryClient = getQueryClient();

  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "crm")) notFound();

  void queryClient.prefetchQuery(crmQueries.company(id));
  void queryClient.prefetchQuery(crmQueries.contacts({ page: 1, pageSize: 20, companyId: id }));
  void queryClient.prefetchQuery(crmQueries.meetings({ page: 1, pageSize: 20, companyId: id }));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <Link
          href="/crm/companies"
          className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeftIcon className="h-3.5 w-3.5" />
          {t("companies.backToCompanies")}
        </Link>

        <CompanyDetail companyId={id} />
      </div>
    </HydrationBoundary>
  );
}
