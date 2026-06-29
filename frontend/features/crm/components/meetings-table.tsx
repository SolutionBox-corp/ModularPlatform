"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { CalendarPlusIcon, CheckIcon, XIcon } from "lucide-react";
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
import { crmQueries, type Meeting } from "@/features/crm/api";
import { useCancelMeeting, useCompleteMeeting } from "@/features/crm/hooks";
import { MeetingFormDialog } from "@/features/crm/components/meeting-form-dialog";

const PAGE_SIZE = 20;
const ALL = "all";
const MEETING_STATUSES = ["planned", "done", "canceled", "no_show"] as const;

const STATUS_VARIANT: Record<string, "default" | "secondary" | "outline" | "destructive"> = {
  planned: "secondary",
  done: "default",
  canceled: "destructive",
  no_show: "outline",
};

interface MeetingsTableProps {
  /** When set, only this contact's meetings are shown and new meetings are pre-linked to it. */
  contactId?: string;
}

export function MeetingsTable({ contactId }: MeetingsTableProps) {
  const t = useTranslations("crm");
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<string>(ALL);
  const cancelMutation = useCancelMeeting();
  const completeMutation = useCompleteMeeting();

  const { data, isLoading } = useQuery(
    crmQueries.meetings({
      page,
      pageSize: PAGE_SIZE,
      contactId,
      status: status === ALL ? undefined : status,
    }),
  );

  const columns: ColumnDef<Meeting>[] = [
    {
      key: "title",
      header: t("table.meetingTitle"),
      cell: (row) => <span className="font-medium">{row.title}</span>,
    },
    {
      key: "when",
      header: t("table.when"),
      cell: (row) => <span className="text-muted-foreground">{new Date(row.scheduledAt).toLocaleString()}</span>,
    },
    {
      key: "duration",
      header: t("table.duration"),
      cell: (row) => t("table.minutes", { count: row.durationMinutes }),
    },
    {
      key: "status",
      header: t("table.status"),
      cell: (row) => (
        <Badge variant={STATUS_VARIANT[row.status] ?? "secondary"}>{t(`meetingStatus.${row.status}`)}</Badge>
      ),
    },
    {
      key: "actions",
      header: "",
      className: "text-right",
      cell: (row) =>
        row.status === "planned" ? (
          <div className="flex justify-end gap-1">
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              aria-label={t("meetings.complete")}
              disabled={completeMutation.isPending}
              onClick={() => completeMutation.mutate({ id: row.id, outcome: null })}
            >
              <CheckIcon className="h-3.5 w-3.5" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7 text-destructive"
              aria-label={t("meetings.cancel")}
              disabled={cancelMutation.isPending}
              onClick={() => cancelMutation.mutate(row.id)}
            >
              <XIcon className="h-3.5 w-3.5" />
            </Button>
          </div>
        ) : (
          <span className="text-xs text-muted-foreground">{row.outcome ?? "—"}</span>
        ),
    },
  ];

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v ?? ALL);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-44" aria-label={t("filter.status")}>
            <SelectValue placeholder={t("filter.allStatuses")} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>{t("filter.allStatuses")}</SelectItem>
            {MEETING_STATUSES.map((s) => (
              <SelectItem key={s} value={s}>
                {t(`meetingStatus.${s}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <MeetingFormDialog
          contactId={contactId}
          trigger={
            <Button size="sm">
              <CalendarPlusIcon className="h-3.5 w-3.5 mr-1.5" />
              {t("meetings.new")}
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
        emptyTitle={t("meetings.emptyTitle")}
        emptyDescription={t("meetings.emptyDescription")}
      />
    </div>
  );
}
