"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { CheckIcon, PlusIcon, Trash2Icon } from "lucide-react";
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
import { crmQueries, type CrmTask } from "@/features/crm/api";
import { useCompleteTask, useDeleteTask } from "@/features/crm/hooks";
import { TaskFormDialog } from "@/features/crm/components/task-form-dialog";

const PAGE_SIZE = 20;
const OPEN = "open";
const DONE = "done";

const PRIORITY_VARIANT: Record<string, "default" | "secondary" | "outline" | "destructive"> = {
  low: "outline",
  normal: "secondary",
  high: "destructive",
};

interface TasksTableProps {
  contactId?: string;
  dealId?: string;
}

export function TasksTable({ contactId, dealId }: TasksTableProps) {
  const t = useTranslations("crm");
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<string>(OPEN);
  const completeMutation = useCompleteTask();
  const deleteMutation = useDeleteTask();

  const { data, isLoading } = useQuery(
    crmQueries.tasks({ page, pageSize: PAGE_SIZE, contactId, dealId, status }),
  );

  const columns: ColumnDef<CrmTask>[] = [
    { key: "title", header: t("table.taskTitle"), cell: (row) => <span className="font-medium">{row.title}</span> },
    {
      key: "due",
      header: t("table.due"),
      cell: (row) => (
        <span className="text-muted-foreground">{row.dueAt ? new Date(row.dueAt).toLocaleString() : "—"}</span>
      ),
    },
    {
      key: "priority",
      header: t("table.priority"),
      cell: (row) => <Badge variant={PRIORITY_VARIANT[row.priority] ?? "secondary"}>{t(`taskPriority.${row.priority}`)}</Badge>,
    },
    {
      key: "actions",
      header: "",
      className: "text-right",
      cell: (row) => (
        <div className="flex justify-end gap-1">
          {row.status === OPEN && (
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              aria-label={t("tasks.complete")}
              disabled={completeMutation.isPending}
              onClick={() => completeMutation.mutate(row.id)}
            >
              <CheckIcon className="h-3.5 w-3.5" />
            </Button>
          )}
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7 text-destructive"
            aria-label={t("tasks.delete")}
            disabled={deleteMutation.isPending}
            onClick={() => deleteMutation.mutate(row.id)}
          >
            <Trash2Icon className="h-3.5 w-3.5" />
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v ?? OPEN);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-44" aria-label={t("table.status")}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={OPEN}>{t("taskStatus.open")}</SelectItem>
            <SelectItem value={DONE}>{t("taskStatus.done")}</SelectItem>
          </SelectContent>
        </Select>

        <TaskFormDialog
          contactId={contactId}
          dealId={dealId}
          trigger={
            <Button size="sm">
              <PlusIcon className="h-3.5 w-3.5 mr-1.5" />
              {t("tasks.new")}
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
        emptyTitle={t("tasks.emptyTitle")}
        emptyDescription={t("tasks.emptyDescription")}
      />
    </div>
  );
}
