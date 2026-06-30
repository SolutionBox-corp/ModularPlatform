"use client";

import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangleIcon, CircleDollarSignIcon, ListChecksIcon, TrophyIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Progress } from "@/components/ui/progress";
import { Skeleton } from "@/components/ui/skeleton";
import { crmQueries, type PipelineStageSummary } from "@/features/crm/api";

function formatMoney(cents: number) {
  return new Intl.NumberFormat(undefined, { style: "currency", currency: "USD", maximumFractionDigits: 0 }).format(cents / 100);
}

function totalAmount(stages: PipelineStageSummary[]) {
  return stages.reduce((sum, stage) => sum + stage.amountCents, 0);
}

export function CrmDashboard() {
  const t = useTranslations("crm");
  const { data, isLoading } = useQuery(crmQueries.dashboard());

  if (isLoading) {
    return <Skeleton className="h-52 w-full" />;
  }

  if (!data) return null;

  const stageTotal = totalAmount(data.stages);

  return (
    <section className="space-y-4">
      <div className="grid gap-3 md:grid-cols-4">
        <div className="rounded-xl border bg-card p-4 shadow-sm">
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <CircleDollarSignIcon className="h-4 w-4" />
            {t("dashboard.openPipeline")}
          </div>
          <div className="mt-2 text-2xl font-semibold tabular-nums">{formatMoney(data.openPipelineAmountCents)}</div>
          <p className="text-xs text-muted-foreground">{t("dashboard.openDeals", { count: data.openDealsCount })}</p>
        </div>
        <div className="rounded-xl border bg-card p-4 shadow-sm">
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <TrophyIcon className="h-4 w-4" />
            {t("dashboard.wonDeals")}
          </div>
          <div className="mt-2 text-2xl font-semibold tabular-nums">{data.wonDealsCount}</div>
          <p className="text-xs text-muted-foreground">{t("dashboard.closedWon")}</p>
        </div>
        <div className="rounded-xl border bg-card p-4 shadow-sm">
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <ListChecksIcon className="h-4 w-4" />
            {t("dashboard.openTasks")}
          </div>
          <div className="mt-2 text-2xl font-semibold tabular-nums">{data.openTasksCount}</div>
          <p className="text-xs text-muted-foreground">{t("dashboard.overdueTasks", { count: data.overdueTasksCount })}</p>
        </div>
        <div className="rounded-xl border bg-card p-4 shadow-sm">
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <AlertTriangleIcon className="h-4 w-4" />
            {t("dashboard.overdueDeals")}
          </div>
          <div className="mt-2 text-2xl font-semibold tabular-nums">{data.overdueDealsCount}</div>
          <p className="text-xs text-muted-foreground">{t("dashboard.needsAttention")}</p>
        </div>
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        <div className="rounded-xl border bg-card p-4 shadow-sm">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-medium">{t("dashboard.pipelineByStage")}</h2>
            <Badge variant="secondary">{formatMoney(stageTotal)}</Badge>
          </div>
          <div className="space-y-3">
            {data.stages.map((stage) => {
              const pct = stageTotal > 0 ? Math.round((stage.amountCents / stageTotal) * 100) : 0;
              return (
                <div key={stage.stage} className="space-y-1">
                  <div className="flex items-center justify-between text-xs">
                    <span>{t(`dealStage.${stage.stage}`)} · {stage.count}</span>
                    <span className="tabular-nums text-muted-foreground">{formatMoney(stage.amountCents)}</span>
                  </div>
                  <Progress value={pct} />
                </div>
              );
            })}
          </div>
        </div>

        <div className="rounded-xl border bg-card p-4 shadow-sm">
          <h2 className="mb-3 text-sm font-medium">{t("dashboard.byLeadSource")}</h2>
          <div className="space-y-2">
            {data.leadSources.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("dashboard.noLeadSources")}</p>
            ) : (
              data.leadSources.map((source) => (
                <div key={source.leadSource} className="flex items-center justify-between rounded-lg bg-muted/40 px-3 py-2 text-sm">
                  <span>{source.leadSource}</span>
                  <span className="tabular-nums text-muted-foreground">{source.count} · {formatMoney(source.amountCents)}</span>
                </div>
              ))
            )}
          </div>
        </div>
      </div>
    </section>
  );
}
