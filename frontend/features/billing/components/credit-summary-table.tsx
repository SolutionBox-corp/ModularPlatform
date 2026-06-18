"use client";

import { useQuery } from "@tanstack/react-query";
import { DataTable } from "@/components/app/data-table";
import { MoneyAmount } from "@/components/app/money-amount";
import { billingQueries } from "@/features/billing/api";
import type { ColumnDef } from "@/components/app/data-table";

interface CreditRow {
  label: string;
  amount: number;
  note: string;
}

const columns: ColumnDef<CreditRow>[] = [
  {
    key: "label",
    header: "Category",
    cell: (row) => <span className="font-medium text-sm">{row.label}</span>,
  },
  {
    key: "amount",
    header: "Credits",
    cell: (row) => <MoneyAmount value={row.amount} />,
    className: "text-right",
  },
  {
    key: "note",
    header: "Note",
    cell: (row) => (
      <span className="text-xs text-muted-foreground">{row.note}</span>
    ),
  },
];

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
  const { data, isLoading } = useQuery(billingQueries.balance());

  const rows: CreditRow[] = data
    ? [
        {
          label: "Posted",
          amount: data.posted,
          note: "Total credits ever granted to this account.",
        },
        {
          label: "Available",
          amount: data.available,
          note: "Ready to spend (posted minus active holds).",
        },
        {
          label: "Held / pending",
          amount: data.posted - data.available,
          note: "Reserved by in-flight operations; released on confirm or expiry.",
        },
      ]
    : [];

  return (
    <DataTable
      columns={columns}
      data={rows}
      rowKey={(row) => row.label}
      isLoading={isLoading}
      emptyTitle="No balance data"
      emptyDescription="Your credit account will appear here after your first top-up."
    />
  );
}
