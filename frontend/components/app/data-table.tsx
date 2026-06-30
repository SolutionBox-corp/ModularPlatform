"use client";

import type { ReactNode } from "react";
import { useTranslations } from "next-intl";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/app/empty-state";
import { useHasHydrated } from "@/hooks/use-has-hydrated";

export interface ColumnDef<TData> {
  /** Unique key, used as React key. */
  key: string;
  /** Column header label. */
  header: string;
  /** Render function for a cell. */
  cell: (row: TData) => ReactNode;
  /** Optional additional class on <th>/<td>. */
  className?: string;
}

interface DataTableProps<TData> {
  columns: ColumnDef<TData>[];
  data: TData[] | undefined;
  /** Row identity — must be stable. */
  rowKey: (row: TData) => string;
  isLoading?: boolean;
  /** Total item count for pagination. When absent, pagination is hidden. */
  total?: number;
  page?: number;
  pageSize?: number;
  onPageChange?: (page: number) => void;
  emptyTitle?: string;
  emptyDescription?: string;
}

const SKELETON_ROWS = 5;

/**
 * Generic, typed, accessible data table. Handles loading skeletons, empty state,
 * and optional pagination via the @/components/ui/pagination primitives.
 */
export function DataTable<TData>({
  columns,
  data,
  rowKey,
  isLoading = false,
  total,
  page = 1,
  pageSize = 20,
  onPageChange,
  emptyTitle,
  emptyDescription,
}: DataTableProps<TData>) {
  const t = useTranslations("shell");
  const resolvedEmptyTitle = emptyTitle ?? t("dataTable.emptyTitle");
  const resolvedEmptyDescription = emptyDescription ?? t("dataTable.emptyDescription");
  const totalPages = total !== undefined ? Math.ceil(total / pageSize) : 1;
  const showPagination =
    total !== undefined && totalPages > 1 && onPageChange !== undefined;
  const hasHydrated = useHasHydrated();
  const showSkeleton = !hasHydrated || (isLoading && data === undefined);

  return (
    <div className="space-y-3">
      <div className="rounded-lg border border-border overflow-hidden">
        <Table>
          <TableHeader>
            <TableRow>
              {columns.map((col) => (
                <TableHead key={col.key} className={col.className}>
                  {col.header}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {showSkeleton ? (
              Array.from({ length: SKELETON_ROWS }).map((_, i) => (
                <TableRow key={i} aria-busy="true">
                  {columns.map((col) => (
                    <TableCell key={col.key} className={col.className}>
                      <Skeleton className="h-4 w-full rounded" />
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : !data || data.length === 0 ? (
              <TableRow>
                <TableCell colSpan={columns.length} className="p-0">
                  <EmptyState
                    title={resolvedEmptyTitle}
                    description={resolvedEmptyDescription}
                  />
                </TableCell>
              </TableRow>
            ) : (
              data.map((row) => (
                <TableRow key={rowKey(row)}>
                  {columns.map((col) => (
                    <TableCell key={col.key} className={col.className}>
                      {col.cell(row)}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {showPagination && (
        <Pagination>
          <PaginationContent>
            <PaginationItem>
              <PaginationPrevious
                onClick={page > 1 ? () => onPageChange(page - 1) : (e) => e.preventDefault()}
                aria-disabled={page <= 1}
                tabIndex={page <= 1 ? -1 : undefined}
                className={page <= 1 ? "pointer-events-none opacity-50" : ""}
              />
            </PaginationItem>

            {buildPageNumbers(page, totalPages).map((p, i) =>
              p === "ellipsis" ? (
                <PaginationItem key={`ellipsis-${i}`}>
                  <PaginationEllipsis />
                </PaginationItem>
              ) : (
                <PaginationItem key={p}>
                  <PaginationLink
                    isActive={p === page}
                    onClick={() => onPageChange(p)}
                  >
                    {p}
                  </PaginationLink>
                </PaginationItem>
              ),
            )}

            <PaginationItem>
              <PaginationNext
                onClick={
                  page < totalPages
                    ? () => onPageChange(page + 1)
                    : (e) => e.preventDefault()
                }
                aria-disabled={page >= totalPages}
                tabIndex={page >= totalPages ? -1 : undefined}
                className={
                  page >= totalPages ? "pointer-events-none opacity-50" : ""
                }
              />
            </PaginationItem>
          </PaginationContent>
        </Pagination>
      )}
    </div>
  );
}

/** Builds a windowed page list with ellipses. */
function buildPageNumbers(
  current: number,
  total: number,
): (number | "ellipsis")[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages: (number | "ellipsis")[] = [1];
  if (current > 3) pages.push("ellipsis");
  for (
    let p = Math.max(2, current - 1);
    p <= Math.min(total - 1, current + 1);
    p++
  ) {
    pages.push(p);
  }
  if (current < total - 2) pages.push("ellipsis");
  pages.push(total);
  return pages;
}
