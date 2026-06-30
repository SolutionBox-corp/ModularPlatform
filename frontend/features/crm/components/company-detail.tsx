"use client";

import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { PencilIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { crmQueries } from "@/features/crm/api";
import { CompanyFormDialog } from "@/features/crm/components/company-form-dialog";
import { ContactsTable } from "@/features/crm/components/contacts-table";
import { MeetingsTable } from "@/features/crm/components/meetings-table";

export function CompanyDetail({ companyId }: { companyId: string }) {
  const t = useTranslations("crm");
  const { data: company, isLoading } = useQuery(crmQueries.company(companyId));

  if (isLoading) {
    return <Skeleton className="h-40 w-full" />;
  }

  if (!company) {
    return <p className="text-sm text-muted-foreground">{t("companies.notFound")}</p>;
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="flex flex-row items-start justify-between">
          <div>
            <CardTitle>{company.name}</CardTitle>
            <CardDescription>
              {[company.industry, company.domain].filter(Boolean).join(" · ") || "—"}
            </CardDescription>
          </div>
          <CompanyFormDialog
            company={company}
            trigger={
              <Button variant="outline" size="sm">
                <PencilIcon className="h-3.5 w-3.5 mr-1.5" />
                {t("companies.edit")}
              </Button>
            }
          />
        </CardHeader>
        <CardContent className="grid grid-cols-2 gap-3 text-sm">
          <div>
            <span className="text-muted-foreground">{t("companyForm.identificationNumber")}: </span>
            {company.identificationNumber ?? "—"}
          </div>
          <div>
            <span className="text-muted-foreground">{t("companyForm.taxIdentificationNumber")}: </span>
            {company.taxIdentificationNumber ?? "—"}
          </div>
          <div className="col-span-2">
            <span className="text-muted-foreground">{t("companyForm.registeredAddress")}: </span>
            {[company.registeredAddress, company.city, company.postalCode, company.country].filter(Boolean).join(", ") || "—"}
          </div>
          {company.notes && <p className="col-span-2 text-muted-foreground">{company.notes}</p>}
        </CardContent>
      </Card>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-muted-foreground">{t("companies.contactsHeading")}</h2>
        <ContactsTable companyId={companyId} />
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-muted-foreground">{t("companies.meetingsHeading")}</h2>
        <MeetingsTable companyId={companyId} />
      </section>
    </div>
  );
}
