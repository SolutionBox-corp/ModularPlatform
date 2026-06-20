"use client";

import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { ShieldAlertIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { EmptyState } from "@/components/app/empty-state";
import {
  identityAdminQueries,
  type AuditTrailEntryResponse,
} from "@/features/identity-admin/api";

interface AuditTrailTableProps {
  /** Resolved audit entries (undefined while loading). */
  entries: AuditTrailEntryResponse[] | undefined;
  isLoading?: boolean;
}

const ACTION_BADGE_VARIANT: Record<
  string,
  "default" | "secondary" | "destructive" | "outline"
> = {
  Insert: "default",
  Update: "secondary",
  Delete: "destructive",
};

type AuditTrailTranslator = ReturnType<typeof useTranslations<"identityAdmin">>;

function buildColumns(
  t: AuditTrailTranslator,
): ColumnDef<AuditTrailEntryResponse>[] {
  return [
    {
      key: "timestamp",
      header: t("audit.columns.timestamp"),
      className: "w-44 shrink-0",
      cell: (row) =>
        new Date(row.timestamp).toLocaleString("en", {
          year: "numeric",
          month: "short",
          day: "numeric",
          hour: "2-digit",
          minute: "2-digit",
          second: "2-digit",
        }),
    },
    {
      key: "action",
      header: t("audit.columns.action"),
      className: "w-24 shrink-0",
      cell: (row) => (
        <Badge
          variant={ACTION_BADGE_VARIANT[row.action] ?? "outline"}
          className="text-xs capitalize"
        >
          {row.action}
        </Badge>
      ),
    },
    {
      key: "values",
      header: t("audit.columns.changedFields"),
      cell: (row) => {
        const entries = Object.entries(row.values);
        if (entries.length === 0) return <span className="text-muted-foreground text-xs">—</span>;
        return (
          <ul className="space-y-0.5">
            {entries.map(([field, value]) => (
              <li key={field} className="text-xs flex items-baseline gap-1.5">
                <span className="font-mono text-muted-foreground">{field}:</span>
                <span className="truncate max-w-xs">
                  {value === null ? (
                    <span className="italic text-muted-foreground">{t("audit.nullValue")}</span>
                  ) : value === "[erased]" ? (
                    <span className="italic text-muted-foreground">{t("audit.erasedValue")}</span>
                  ) : (
                    value
                  )}
                </span>
              </li>
            ))}
          </ul>
        );
      },
    },
  ];
}

/**
 * Presentational audit-trail table. Renders a {@link UserAuditTrailResponse}'s
 * entries. The caller owns the data fetch, so the SAME table + columns + type
 * are reused for both the per-tenant identity-admin endpoint and the
 * cross-tenant platform-admin endpoint without duplicating the rendering.
 */
export function AuditTrailTable({ entries, isLoading = false }: AuditTrailTableProps) {
  const t = useTranslations("identityAdmin");

  if (!isLoading && (!entries || entries.length === 0)) {
    return (
      <EmptyState
        icon={ShieldAlertIcon}
        title={t("audit.empty.title")}
        description={t("audit.empty.description")}
        className="py-10"
      />
    );
  }

  return (
    <DataTable
      columns={buildColumns(t)}
      data={entries}
      rowKey={(row) => row.id}
      isLoading={isLoading}
      emptyTitle={t("audit.empty.title")}
      emptyDescription={t("audit.empty.tableDescription")}
    />
  );
}

/**
 * Thin wrapper that fetches the per-tenant identity-admin audit trail and feeds
 * it to {@link AuditTrailTable}. Used by the (tenant) Identity admin panel.
 */
export function UserAuditTrailTable({ userId }: { userId: string }) {
  const { data, isLoading } = useQuery(identityAdminQueries.auditTrail(userId));
  return <AuditTrailTable entries={data?.entries} isLoading={isLoading} />;
}
