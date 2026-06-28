"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocale, useTranslations } from "next-intl";
import { ArrowDownLeftIcon, ArrowUpRightIcon } from "lucide-react";
import { DataTable } from "@/components/app/data-table";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { billingQueries } from "@/features/billing/api";
import type { CreditLedgerEntry } from "@/features/billing/api";
import type { ColumnDef } from "@/components/app/data-table";

const PAGE_SIZE = 20;

/**
 * The caller's append-only credit ledger — every Topup/Spend/Reservation/Release/Expiry
 * entry, newest first, paged. Replaces the old 3-row balance "summary" stub: that showed
 * only the current projection; this shows the real per-entry history the backend now exposes.
 */
export function CreditLedgerTable() {
  const t = useTranslations("billing");
  const locale = useLocale();
  const [page, setPage] = useState(1);
  const { data, isLoading } = useQuery(billingQueries.ledger(page, PAGE_SIZE));

  const columns: ColumnDef<CreditLedgerEntry>[] = [
    {
      key: "type",
      header: t("ledger.columns.type"),
      cell: (row) => {
        const isCredit = row.direction === "Credit";
        return (
          <span className="flex items-center gap-2">
            {isCredit ? (
              <ArrowDownLeftIcon
                className="h-4 w-4 text-success shrink-0"
                aria-hidden
              />
            ) : (
              <ArrowUpRightIcon
                className="h-4 w-4 text-muted-foreground shrink-0"
                aria-hidden
              />
            )}
            <span className="text-sm font-medium">
              {ledgerTypeLabel(t, row.type)}
            </span>
          </span>
        );
      },
    },
    {
      key: "amount",
      header: t("ledger.columns.amount"),
      cell: (row) => {
        const isCredit = row.direction === "Credit";
        return (
          <span
            className={cn(
              "tabular-nums text-sm font-medium",
              isCredit ? "text-success" : "text-foreground",
            )}
          >
            {isCredit ? "+" : "−"}
            {row.amount.toLocaleString()}
          </span>
        );
      },
      className: "text-right",
    },
    {
      key: "direction",
      header: t("ledger.columns.direction"),
      cell: (row) => (
        <Badge variant={row.direction === "Credit" ? "default" : "secondary"}>
          {row.direction === "Credit"
            ? t("ledger.credit")
            : t("ledger.debit")}
        </Badge>
      ),
    },
    {
      key: "createdAt",
      header: t("ledger.columns.date"),
      cell: (row) => (
        <span className="text-xs text-muted-foreground">
          {new Date(row.createdAt).toLocaleString(locale, {
            dateStyle: "medium",
            timeStyle: "short",
          })}
        </span>
      ),
    },
  ];

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
      emptyTitle={t("ledger.emptyTitle")}
      emptyDescription={t("ledger.emptyDescription")}
    />
  );
}

/** The ledger entry types the backend can emit; anything else falls back to the raw value. */
const KNOWN_LEDGER_TYPES = new Set([
  "topup",
  "spend",
  "reservation",
  "release",
  "expiry",
  "adjustment",
  "refund",
]);

/** Localize a known ledger entry type; fall back to the raw value (forward-compat). */
function ledgerTypeLabel(
  t: ReturnType<typeof useTranslations<"billing">>,
  type: string,
): string {
  const key = type.toLowerCase();
  return KNOWN_LEDGER_TYPES.has(key)
    ? t(`ledger.types.${key}` as Parameters<typeof t>[0])
    : type;
}
