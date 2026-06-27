"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { PlusIcon, Trash2Icon, PencilIcon } from "lucide-react";
import { Button, buttonVariants } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { crmQueries, CONTACT_STATUSES, type ContactListItem } from "@/features/crm/api";
import { useDeleteContact } from "@/features/crm/hooks";
import { ContactFormDialog } from "@/features/crm/components/contact-form-dialog";

const PAGE_SIZE = 20;
const ALL = "all";

const STATUS_VARIANT: Record<string, "default" | "secondary" | "outline"> = {
  lead: "secondary",
  active: "default",
  customer: "default",
  archived: "outline",
};

export function ContactsTable() {
  const t = useTranslations("crm");
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<string>(ALL);

  const { data, isLoading } = useQuery(
    crmQueries.contacts({ page, pageSize: PAGE_SIZE, status: status === ALL ? undefined : status }),
  );
  const deleteMutation = useDeleteContact();

  const columns: ColumnDef<ContactListItem>[] = [
    {
      key: "name",
      header: t("table.name"),
      cell: (row) => (
        <Link href={`/crm/contacts/${row.id}`} className="font-medium hover:underline">
          {row.fullName}
        </Link>
      ),
    },
    {
      key: "email",
      header: t("table.email"),
      cell: (row) => <span className="text-muted-foreground">{row.email ?? "—"}</span>,
    },
    {
      key: "company",
      header: t("table.company"),
      cell: (row) => row.company ?? "—",
    },
    {
      key: "status",
      header: t("table.status"),
      cell: (row) => (
        <Badge variant={STATUS_VARIANT[row.status] ?? "secondary"}>{t(`status.${row.status}`)}</Badge>
      ),
    },
    {
      key: "actions",
      header: "",
      className: "text-right",
      cell: (row) => (
        <div className="flex justify-end gap-1">
          <Link
            href={`/crm/contacts/${row.id}`}
            className={cn(buttonVariants({ variant: "ghost", size: "icon" }), "h-7 w-7")}
            aria-label={t("table.open")}
          >
            <PencilIcon className="h-3.5 w-3.5" />
          </Link>
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7 text-destructive"
            aria-label={t("table.delete")}
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
            setStatus(v ?? ALL);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-44" aria-label={t("filter.status")}>
            <SelectValue placeholder={t("filter.allStatuses")} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>{t("filter.allStatuses")}</SelectItem>
            {CONTACT_STATUSES.map((s) => (
              <SelectItem key={s} value={s}>
                {t(`status.${s}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <ContactFormDialog
          trigger={
            <Button size="sm">
              <PlusIcon className="h-3.5 w-3.5 mr-1.5" />
              {t("contacts.new")}
            </Button>
          }
        />
      </div>

      <DataTable
        columns={columns}
        data={data?.items}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        total={data?.total}
        page={page}
        pageSize={PAGE_SIZE}
        onPageChange={setPage}
        emptyTitle={t("contacts.emptyTitle")}
        emptyDescription={t("contacts.emptyDescription")}
      />
    </div>
  );
}
