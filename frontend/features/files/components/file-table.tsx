"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { FileIcon, SearchIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { fileQueries, type FileListItem } from "@/features/files/api";
import { FileRowActions } from "@/features/files/components/file-row-actions";

const PAGE_SIZE = 20;

/** Formats bytes as a human-readable string, e.g. "1.2 MB". */
function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Returns a short label for the MIME type, e.g. "image/png" → "PNG". */
function mimeLabel(contentType: string): string {
  const map: Record<string, string> = {
    "image/png": "PNG",
    "image/jpeg": "JPEG",
    "application/pdf": "PDF",
    "text/plain": "TXT",
  };
  return map[contentType] ?? contentType.split("/")[1]?.toUpperCase() ?? contentType;
}

type Translate = ReturnType<typeof useTranslations>;

function buildColumns(t: Translate): ColumnDef<FileListItem>[] {
  return [
  {
    key: "name",
    header: t("table.name"),
    cell: (row) => (
      <div className="flex items-center gap-2 min-w-0">
        <FileIcon className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <span className="truncate text-sm">{row.fileName}</span>
      </div>
    ),
  },
  {
    key: "type",
    header: t("table.type"),
    className: "hidden sm:table-cell",
    cell: (row) => (
      <Badge variant="secondary" className="text-xs font-normal">
        {mimeLabel(row.contentType)}
      </Badge>
    ),
  },
  {
    key: "size",
    header: t("table.size"),
    className: "hidden sm:table-cell tabular-nums text-right",
    cell: (row) => (
      <span className="text-sm text-muted-foreground tabular-nums">
        {formatBytes(row.size)}
      </span>
    ),
  },
  {
    key: "date",
    header: t("table.uploaded"),
    className: "hidden md:table-cell",
    cell: (row) => (
      <span className="text-sm text-muted-foreground">
        {new Date(row.createdAt).toLocaleDateString("en", {
          year: "numeric",
          month: "short",
          day: "numeric",
        })}
      </span>
    ),
  },
  {
    key: "actions",
    header: "",
    className: "text-right w-10",
    cell: (row) => <FileRowActions file={row} />,
  },
  ];
}

export function FileTable() {
  const t = useTranslations("files");
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const { data, isLoading } = useQuery(fileQueries.list(page, PAGE_SIZE, search));
  const columns = buildColumns(t);

  function handleSearchChange(value: string) {
    setSearch(value);
    setPage(1); // a new filter resets to the first page
  }

  return (
    <div className="space-y-3">
      <div className="relative max-w-xs">
        <SearchIcon
          className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          type="search"
          value={search}
          onChange={(e) => handleSearchChange(e.target.value)}
          placeholder={t("table.searchPlaceholder")}
          aria-label={t("table.searchAria")}
          className="pl-8"
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
        emptyTitle={t(search ? "table.noMatchTitle" : "table.emptyTitle")}
        emptyDescription={t(
          search ? "table.noMatchDescription" : "table.emptyDescription",
        )}
      />
    </div>
  );
}
