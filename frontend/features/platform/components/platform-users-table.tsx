"use client";

import { useState } from "react";
import Link from "next/link";
import { ScrollTextIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { useListPlatformUsers } from "@/features/platform/hooks";
import type { PlatformUserItem } from "@/features/platform/api";

const PAGE_SIZE = 50;

/** Short, monospace tenant id (first segment of the UUID). */
function shortId(id: string): string {
  return id.split("-")[0] ?? id;
}

type Translate = (key: string, values?: Record<string, string>) => string;

function buildColumns(
  t: Translate,
  locale: string,
): ColumnDef<PlatformUserItem>[] {
  return [
    {
      key: "email",
      header: t("users.table.email"),
      cell: (row) => <span className="text-sm">{row.email}</span>,
    },
    {
      key: "displayName",
      header: t("users.table.name"),
      className: "hidden sm:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">{row.displayName}</span>
      ),
    },
    {
      key: "tenantId",
      header: t("users.table.tenant"),
      className: "hidden md:table-cell",
      cell: (row) => (
        <span
          className="text-xs font-mono text-muted-foreground"
          title={row.tenantId}
        >
          {shortId(row.tenantId)}
        </span>
      ),
    },
    {
      key: "createdAt",
      header: t("users.table.created"),
      className: "hidden md:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">
          {new Date(row.createdAt).toLocaleDateString(locale, {
            year: "numeric",
            month: "short",
            day: "numeric",
          })}
        </span>
      ),
    },
    {
      key: "actions",
      header: "",
      className: "text-right w-10",
      cell: (row) => (
        <Link
          href={`/platform/audit?userId=${row.userId}`}
          aria-label={t("users.table.viewAudit", { email: row.email })}
          className={cn(buttonVariants({ variant: "ghost", size: "icon-sm" }))}
        >
          <ScrollTextIcon className="h-4 w-4" aria-hidden="true" />
        </Link>
      ),
    },
  ];
}

interface PlatformUsersTableProps {
  /** Optional tenant filter (passed through to the backend). */
  tenantId?: string;
}

export function PlatformUsersTable({ tenantId }: PlatformUsersTableProps) {
  const t = useTranslations("platform");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const { data, isLoading } = useListPlatformUsers({
    tenantId,
    limit: PAGE_SIZE,
    offset: (page - 1) * PAGE_SIZE,
  });

  return (
    <DataTable
      columns={buildColumns(t, locale)}
      data={data?.items}
      rowKey={(row) => row.userId}
      isLoading={isLoading}
      total={data?.total}
      page={page}
      pageSize={PAGE_SIZE}
      onPageChange={setPage}
      emptyTitle={t("users.table.emptyTitle")}
      emptyDescription={t("users.table.emptyDescription")}
    />
  );
}
