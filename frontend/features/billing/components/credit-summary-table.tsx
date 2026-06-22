"use client";

import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { DataTable } from "@/components/app/data-table";
import { MoneyAmount } from "@/components/app/money-amount";
import { billingQueries } from "@/features/billing/api";
import type { ColumnDef } from "@/components/app/data-table";

interface CreditRow {
  label: string;
  amount: number;
  note: string;
}

/**
 * Credit balance summary table.
 *
 * Note: the backend does not expose a paginated credit-entries ledger endpoint.
 * This table surfaces the authoritative balance projection from
 * GET /v1/billing/credits/balance (posted / available), which is the source of
 * truth per CLAUDE.md §4. A full per-entry ledger can be added once the backend
 * exposes GET /v1/billing/credits/entries.
 */
export function CreditSummaryTable() {
  const t = useTranslations("billing");
  const { data, isLoading } = useQuery(billingQueries.balance());

  const columns: ColumnDef<CreditRow>[] = [
    {
      key: "label",
      header: t("balance.columns.category"),
      cell: (row) => <span className="font-medium text-sm">{row.label}</span>,
    },
    {
      key: "amount",
      header: t("balance.columns.credits"),
      cell: (row) => <MoneyAmount value={row.amount} />,
      className: "text-right",
    },
    {
      key: "note",
      header: t("balance.columns.note"),
      cell: (row) => (
        <span className="text-xs text-muted-foreground">{row.note}</span>
      ),
    },
  ];

  const rows: CreditRow[] = data
    ? [
        {
          label: t("balance.rows.posted"),
          amount: data.posted,
          note: t("balance.rows.postedNote"),
        },
        {
          label: t("balance.rows.available"),
          amount: data.available,
          note: t("balance.rows.availableNote"),
        },
        {
          label: t("balance.rows.held"),
          amount: data.posted - data.available,
          note: t("balance.rows.heldNote"),
        },
      ]
    : [];

  return (
    <DataTable
      columns={columns}
      data={rows}
      rowKey={(row) => row.label}
      isLoading={isLoading}
      emptyTitle={t("balance.emptyTitle")}
      emptyDescription={t("balance.emptyDescription")}
    />
  );
}
