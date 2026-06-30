"use client";

import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { CalendarIcon, CircleDollarSignIcon, MailIcon, PhoneIcon, StickyNoteIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { crmQueries, type Deal, type Interaction } from "@/features/crm/api";
import { TasksTable } from "@/features/crm/components/tasks-table";
import { MeetingsTable } from "@/features/crm/components/meetings-table";

const STAGE_VARIANT: Record<string, "default" | "secondary" | "outline" | "destructive"> = {
  lead: "secondary",
  qualified: "secondary",
  proposal: "outline",
  negotiation: "outline",
  won: "default",
  lost: "destructive",
};

const TYPE_ICON: Record<string, typeof PhoneIcon> = {
  call: PhoneIcon,
  email: MailIcon,
  note: StickyNoteIcon,
  meeting: CalendarIcon,
};

function formatAmount(deal: Deal) {
  return new Intl.NumberFormat(undefined, { style: "currency", currency: deal.currency }).format(deal.amountCents / 100);
}

function DealTimeline({ dealId }: { dealId: string }) {
  const t = useTranslations("crm");
  const { data, isLoading } = useQuery(crmQueries.dealInteractions(dealId));

  if (isLoading) return <Skeleton className="h-20 w-full" />;
  if (!data || data.items.length === 0) return <p className="text-sm text-muted-foreground">{t("timeline.empty")}</p>;

  return (
    <ul className="space-y-3">
      {data.items.map((interaction: Interaction) => {
        const Icon = TYPE_ICON[interaction.type] ?? StickyNoteIcon;
        return (
          <li key={interaction.id} className="flex gap-3 rounded-lg border bg-muted/20 p-3">
            <div className="mt-0.5 text-muted-foreground">
              <Icon className="h-4 w-4" aria-hidden="true" />
            </div>
            <div className="flex-1 space-y-1">
              <div className="flex items-center gap-2">
                <Badge variant="outline" className="text-xs">{t(`interactionType.${interaction.type}`)}</Badge>
                <span className="text-xs text-muted-foreground">{new Date(interaction.occurredAt).toLocaleString()}</span>
              </div>
              {interaction.body && <p className="text-sm">{interaction.body}</p>}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

export function DealDetail({ dealId }: { dealId: string }) {
  const t = useTranslations("crm");
  const { data: deal, isLoading } = useQuery(crmQueries.deal(dealId));

  if (isLoading) {
    return <Skeleton className="h-40 w-full" />;
  }

  if (!deal) {
    return <p className="text-sm text-muted-foreground">{t("deals.notFound")}</p>;
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="space-y-2">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle>{deal.title}</CardTitle>
              <CardDescription>{t("deals.detailDescription")}</CardDescription>
            </div>
            <Badge variant={STAGE_VARIANT[deal.stage] ?? "secondary"}>{t(`dealStage.${deal.stage}`)}</Badge>
          </div>
        </CardHeader>
        <CardContent className="grid gap-3 text-sm md:grid-cols-3">
          <div className="rounded-lg border bg-muted/30 p-3">
            <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <CircleDollarSignIcon className="h-3.5 w-3.5" />
              {t("table.amount")}
            </div>
            <div className="mt-1 text-lg font-semibold tabular-nums">{formatAmount(deal)}</div>
          </div>
          <div className="rounded-lg border bg-muted/30 p-3">
            <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <CalendarIcon className="h-3.5 w-3.5" />
              {t("dealForm.expectedCloseAt")}
            </div>
            <div className="mt-1 font-medium">
              {deal.expectedCloseAt ? new Date(deal.expectedCloseAt).toLocaleDateString() : "—"}
            </div>
          </div>
          <div className="rounded-lg border bg-muted/30 p-3">
            <div className="text-xs text-muted-foreground">{t("dealForm.probability")}</div>
            <div className="mt-1 font-medium">{deal.probabilityPercent}%</div>
          </div>
          <div className="rounded-lg border bg-muted/30 p-3">
            <div className="text-xs text-muted-foreground">{t("deals.links")}</div>
            <div className="mt-1 text-xs text-muted-foreground">
              {deal.contactId ? t("deals.hasContact") : t("deals.noContact")} · {deal.companyId ? t("deals.hasCompany") : t("deals.noCompany")}
            </div>
          </div>
          <div className="rounded-lg border bg-muted/30 p-3 md:col-span-2">
            <div className="text-xs text-muted-foreground">{t("dealForm.nextStep")}</div>
            <div className="mt-1 font-medium">{deal.nextStep ?? "—"}</div>
          </div>
          <div className="rounded-lg border bg-muted/30 p-3">
            <div className="text-xs text-muted-foreground">{t("dealForm.leadSource")}</div>
            <div className="mt-1 font-medium">{deal.leadSource ?? "—"}</div>
          </div>
          {deal.notes && <p className="md:col-span-3 text-muted-foreground">{deal.notes}</p>}
        </CardContent>
      </Card>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-muted-foreground">{t("tasks.heading")}</h2>
        <TasksTable contactId={deal.contactId ?? undefined} dealId={deal.id} />
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-muted-foreground">{t("meetings.heading")}</h2>
        <MeetingsTable contactId={deal.contactId ?? undefined} companyId={deal.companyId ?? undefined} dealId={deal.id} />
      </section>

      <section className="space-y-3">
        <h2 className="text-sm font-medium text-muted-foreground">{t("timeline.heading")}</h2>
        <DealTimeline dealId={deal.id} />
      </section>
    </div>
  );
}
