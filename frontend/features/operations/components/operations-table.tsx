"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import {
  CheckCircle2Icon,
  ClockIcon,
  LoaderCircleIcon,
  XCircleIcon,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import {
  operationQueries,
  type OperationListItem,
} from "@/features/operations/api";

const PAGE_SIZE = 20;

type Translate = ReturnType<typeof useTranslations>;

function statusLabel(t: Translate, status: string): string {
  const keys: Record<string, Parameters<typeof t>[0]> = {
    Pending: "status.pending" as Parameters<typeof t>[0],
    Running: "status.running" as Parameters<typeof t>[0],
    Succeeded: "status.succeeded" as Parameters<typeof t>[0],
    Failed: "status.failed" as Parameters<typeof t>[0],
  };
  return keys[status] ? t(keys[status]) : status;
}

function StatusBadge({ status }: { status: string }) {
  const t = useTranslations("operations");
  const iconClass = "h-3.5 w-3.5";

  if (status === "Succeeded") {
    return (
      <Badge variant="secondary" className="gap-1.5 text-success">
        <CheckCircle2Icon className={iconClass} aria-hidden="true" />
        {statusLabel(t, status)}
      </Badge>
    );
  }

  if (status === "Failed") {
    return (
      <Badge variant="destructive" className="gap-1.5">
        <XCircleIcon className={iconClass} aria-hidden="true" />
        {statusLabel(t, status)}
      </Badge>
    );
  }

  if (status === "Running") {
    return (
      <Badge variant="outline" className="gap-1.5">
        <LoaderCircleIcon
          className={`${iconClass} animate-spin`}
          aria-hidden="true"
        />
        {statusLabel(t, status)}
      </Badge>
    );
  }

  return (
    <Badge variant="outline" className="gap-1.5">
      <ClockIcon className={iconClass} aria-hidden="true" />
      {statusLabel(t, status)}
    </Badge>
  );
}

function formatOperationType(value: string): string {
  return value
    .split(/[-_.\s]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function buildColumns(
  t: Translate,
  locale: string,
): ColumnDef<OperationListItem>[] {
  const formatDate = (value: string | null) =>
    value
      ? new Date(value).toLocaleString(locale, {
          month: "short",
          day: "numeric",
          hour: "2-digit",
          minute: "2-digit",
        })
      : t("table.notFinished");

  return [
    {
      key: "type",
      header: t("table.type"),
      cell: (row) => (
        <div className="min-w-0">
          <p className="truncate text-sm font-medium">
            {formatOperationType(row.type)}
          </p>
          <p className="truncate text-xs text-muted-foreground">{row.id}</p>
        </div>
      ),
    },
    {
      key: "status",
      header: t("table.status"),
      cell: (row) => <StatusBadge status={row.status} />,
    },
    {
      key: "created",
      header: t("table.created"),
      className: "hidden md:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">
          {formatDate(row.createdAt)}
        </span>
      ),
    },
    {
      key: "completed",
      header: t("table.completed"),
      className: "hidden lg:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">
          {formatDate(row.completedAt)}
        </span>
      ),
    },
    {
      key: "error",
      header: t("table.error"),
      className: "hidden xl:table-cell",
      cell: (row) => (
        <span className="text-sm text-muted-foreground">
          {row.errorCode ?? "-"}
        </span>
      ),
    },
  ];
}

export function OperationsTable() {
  const t = useTranslations("operations");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const { data, isLoading } = useQuery(operationQueries.list(page, PAGE_SIZE));
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
      emptyTitle={t("empty.title")}
      emptyDescription={t("empty.description")}
    />
  );
}
