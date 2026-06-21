"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { marketingQueries, type SnapshotListItem } from "@/features/marketing/api";

const PAGE_SIZE = 20;

type Translate = ReturnType<typeof useTranslations>;

function buildColumns(t: Translate, locale: string): ColumnDef<SnapshotListItem>[] {
  return [
    {
      key: "metric",
      header: t("snapshots.table.metric"),
      cell: (row) => (
        <div className="flex flex-col min-w-0">
          <span className="truncate text-sm font-medium">{row.metricName}</span>
          {row.dimension && (
            <span className="truncate text-xs text-muted-foreground">{row.dimension}</span>
          )}
        </div>
      ),
    },
    {
      key: "source",
      header: t("snapshots.table.source"),
      className: "hidden sm:table-cell",
      cell: (row) => (
        <Badge variant="secondary" className="text-xs font-normal uppercase">
          {row.source}
        </Badge>
      ),
    },
    {
      key: "value",
      header: t("snapshots.table.value"),
      className: "text-right tabular-nums",
      cell: (row) => (
        <span className="text-sm tabular-nums">
          {row.value.toLocaleString(locale)}
        </span>
      ),
    },
    {
      key: "recorded",
      header: t("snapshots.table.recorded"),
      className: "hidden md:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">
          {new Date(row.recordedAt).toLocaleDateString(locale, {
            year: "numeric",
            month: "short",
            day: "numeric",
          })}
        </span>
      ),
    },
  ];
}

/** Paged metric snapshots, optionally filtered by `source`. */
export function SnapshotsTable({ source }: { source?: string }) {
  const t = useTranslations("marketing");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const { data, isLoading } = useQuery(
    marketingQueries.snapshots({ source, page, pageSize: PAGE_SIZE }),
  );
  const columns = buildColumns(t, locale);

  return (
    <DataTable
      columns={columns}
      data={data?.items}
      rowKey={(row) => row.id}
      isLoading={isLoading}
      total={data?.totalCount}
      page={page}
      pageSize={PAGE_SIZE}
      onPageChange={setPage}
      emptyTitle={t("snapshots.table.emptyTitle")}
      emptyDescription={t("snapshots.table.emptyDescription")}
    />
  );
}
