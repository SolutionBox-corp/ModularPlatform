"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { PlusIcon, Trash2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { crmQueries, DEAL_STAGES, type Deal } from "@/features/crm/api";
import { useDeleteDeal, useMoveDealStage } from "@/features/crm/hooks";
import { DealFormDialog } from "@/features/crm/components/deal-form-dialog";

const PAGE_SIZE = 20;
const ALL = "all";

const STAGE_VARIANT: Record<string, "default" | "secondary" | "outline" | "destructive"> = {
  lead: "secondary",
  qualified: "secondary",
  proposal: "outline",
  negotiation: "outline",
  won: "default",
  lost: "destructive",
};

interface DealsTableProps {
  contactId?: string;
}

export function DealsTable({ contactId }: DealsTableProps) {
  const t = useTranslations("crm");
  const [page, setPage] = useState(1);
  const [stage, setStage] = useState<string>(ALL);
  const moveMutation = useMoveDealStage();
  const deleteMutation = useDeleteDeal();

  const { data, isLoading } = useQuery(
    crmQueries.deals({ page, pageSize: PAGE_SIZE, contactId, stage: stage === ALL ? undefined : stage }),
  );

  const fmtAmount = (cents: number, currency: string) =>
    new Intl.NumberFormat(undefined, { style: "currency", currency }).format(cents / 100);

  const isTerminal = (s: string) => s === "won" || s === "lost";

  const columns: ColumnDef<Deal>[] = [
    {
      key: "title",
      header: t("table.dealTitle"),
      cell: (row) => (
        <Link href={`/crm/deals/${row.id}`} className="font-medium hover:underline">
          {row.title}
        </Link>
      ),
    },
    {
      key: "amount",
      header: t("table.amount"),
      cell: (row) => <span className="tabular-nums">{fmtAmount(row.amountCents, row.currency)}</span>,
    },
    {
      key: "stage",
      header: t("table.stage"),
      cell: (row) =>
        isTerminal(row.stage) ? (
          <Badge variant={STAGE_VARIANT[row.stage]}>{t(`dealStage.${row.stage}`)}</Badge>
        ) : (
          <Select value={row.stage} onValueChange={(v) => moveMutation.mutate({ id: row.id, stage: v ?? row.stage })}>
            <SelectTrigger className="h-7 w-36" aria-label={t("table.stage")}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {DEAL_STAGES.map((s) => (
                <SelectItem key={s} value={s}>
                  {t(`dealStage.${s}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ),
    },
    {
      key: "probability",
      header: t("table.probability"),
      cell: (row) => <span className="text-muted-foreground tabular-nums">{row.probabilityPercent}%</span>,
    },
    {
      key: "nextStep",
      header: t("table.nextStep"),
      cell: (row) => <span className="text-muted-foreground">{row.nextStep ?? "—"}</span>,
    },
    {
      key: "actions",
      header: "",
      className: "text-right",
      cell: (row) => (
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7 text-destructive"
          aria-label={t("deals.delete")}
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
      <div className="flex items-center justify-between gap-2">
        <Select
          value={stage}
          onValueChange={(v) => {
            setStage(v ?? ALL);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-44" aria-label={t("filter.stage")}>
            <SelectValue placeholder={t("filter.allStages")} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>{t("filter.allStages")}</SelectItem>
            {DEAL_STAGES.map((s) => (
              <SelectItem key={s} value={s}>
                {t(`dealStage.${s}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <DealFormDialog
          contactId={contactId}
          trigger={
            <Button size="sm">
              <PlusIcon className="h-3.5 w-3.5 mr-1.5" />
              {t("deals.new")}
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
        emptyTitle={t("deals.emptyTitle")}
        emptyDescription={t("deals.emptyDescription")}
      />
    </div>
  );
}
