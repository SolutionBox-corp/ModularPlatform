"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { TenantEntitlementEditor } from "./tenant-entitlement-editor";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useListPlatformTenants } from "@/features/platform/hooks";
import type { PlatformTenantItem } from "@/features/platform/api";

const PAGE_SIZE = 50;

/**
 * /platform/tenants content: a paged table of ALL tenants (GET /tenant/admin/tenants) + an entitlement editor.
 * "Manage" on a row loads that tenant's PERSISTED entitlements into the editor below; a manual UUID input still works.
 */
export function TenantsContent() {
  const t = useTranslations("platform");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const [activeTenantId, setActiveTenantId] = useState<string | null>(null);
  const { data, isLoading } = useListPlatformTenants({
    limit: PAGE_SIZE,
    offset: (page - 1) * PAGE_SIZE,
  });

  const columns: ColumnDef<PlatformTenantItem>[] = [
    {
      key: "name",
      header: t("tenants.table.name"),
      cell: (row) => <span className="text-sm font-medium">{row.name}</span>,
    },
    {
      key: "subdomain",
      header: t("tenants.table.subdomain"),
      className: "hidden sm:table-cell",
      cell: (row) => (
        <span className="text-sm font-mono text-muted-foreground">{row.subdomain}</span>
      ),
    },
    {
      key: "status",
      header: t("tenants.table.status"),
      className: "hidden sm:table-cell",
      cell: (row) => (
        <Badge variant="secondary" className="text-xs">
          {row.status}
        </Badge>
      ),
    },
    {
      key: "created",
      header: t("tenants.table.created"),
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
      className: "text-right w-24",
      cell: (row) => (
        <Button
          size="sm"
          variant="ghost"
          onClick={() => setActiveTenantId(row.tenantId)}
        >
          {t("tenants.table.manage")}
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <DataTable
        columns={columns}
        data={data?.items}
        rowKey={(row) => row.tenantId}
        isLoading={isLoading}
        total={data?.total}
        page={page}
        pageSize={PAGE_SIZE}
        onPageChange={setPage}
        emptyTitle={t("tenants.table.emptyTitle")}
        emptyDescription={t("tenants.table.emptyDescription")}
      />

      <TenantEntitlementEditor
        tenantId={activeTenantId}
        onTenantChange={setActiveTenantId}
      />
    </div>
  );
}
