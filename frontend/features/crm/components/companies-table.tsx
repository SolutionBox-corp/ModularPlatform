"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { PlusIcon, Trash2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { crmQueries, type CompanyListItem } from "@/features/crm/api";
import { useDeleteCompany } from "@/features/crm/hooks";
import { CompanyFormDialog } from "@/features/crm/components/company-form-dialog";

const PAGE_SIZE = 20;

export function CompaniesTable() {
  const t = useTranslations("crm");
  const [page, setPage] = useState(1);
  const deleteMutation = useDeleteCompany();

  const { data, isLoading } = useQuery(crmQueries.companies({ page, pageSize: PAGE_SIZE }));

  const columns: ColumnDef<CompanyListItem>[] = [
    {
      key: "name",
      header: t("table.companyName"),
      cell: (row) => (
        <Link href={`/crm/companies/${row.id}`} className="font-medium hover:underline">
          {row.name}
        </Link>
      ),
    },
    { key: "domain", header: t("table.domain"), cell: (row) => <span className="text-muted-foreground">{row.domain ?? "—"}</span> },
    { key: "industry", header: t("table.industry"), cell: (row) => <span className="text-muted-foreground">{row.industry ?? "—"}</span> },
    {
      key: "actions",
      header: "",
      className: "text-right",
      cell: (row) => (
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7 text-destructive"
          aria-label={t("companies.delete")}
          disabled={deleteMutation.isPending}
          onClick={() => deleteMutation.mutate(row.id)}
        >
          <Trash2Icon className="h-3.5 w-3.5" />
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-end">
        <CompanyFormDialog
          trigger={
            <Button size="sm">
              <PlusIcon className="h-3.5 w-3.5 mr-1.5" />
              {t("companies.new")}
            </Button>
          }
        />
      </div>

      <DataTable
        columns={columns}
        data={data?.items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        total={data?.totalCount}
        page={page}
        pageSize={PAGE_SIZE}
        onPageChange={setPage}
        emptyTitle={t("companies.emptyTitle")}
        emptyDescription={t("companies.emptyDescription")}
      />
    </div>
  );
}
