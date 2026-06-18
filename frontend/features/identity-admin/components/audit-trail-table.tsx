"use client";

import { useQuery } from "@tanstack/react-query";
import { ShieldAlertIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { EmptyState } from "@/components/app/empty-state";
import {
  identityAdminQueries,
  type AuditTrailEntryResponse,
} from "@/features/identity-admin/api";

interface AuditTrailTableProps {
  userId: string;
}

const ACTION_BADGE_VARIANT: Record<
  string,
  "default" | "secondary" | "destructive" | "outline"
> = {
  Insert: "default",
  Update: "secondary",
  Delete: "destructive",
};

const COLUMNS: ColumnDef<AuditTrailEntryResponse>[] = [
  {
    key: "timestamp",
    header: "Timestamp",
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
    header: "Action",
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
    header: "Changed fields",
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
                  <span className="italic text-muted-foreground">null</span>
                ) : value === "[erased]" ? (
                  <span className="italic text-muted-foreground">[erased]</span>
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

export function AuditTrailTable({ userId }: AuditTrailTableProps) {
  const { data, isLoading } = useQuery(identityAdminQueries.auditTrail(userId));

  if (!isLoading && (!data || data.entries.length === 0)) {
    return (
      <EmptyState
        icon={ShieldAlertIcon}
        title="No audit entries"
        description="No Identity audit events recorded for this user yet."
        className="py-10"
      />
    );
  }

  return (
    <DataTable
      columns={COLUMNS}
      data={data?.entries}
      rowKey={(row) => row.id}
      isLoading={isLoading}
      emptyTitle="No audit entries"
      emptyDescription="No Identity audit events recorded for this user."
    />
  );
}
