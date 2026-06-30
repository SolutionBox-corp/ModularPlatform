import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";
import type { Paged } from "@/lib/api/types";

export interface OperationStatusResponse {
  id: string;
  type: string;
  /** Backend OperationStatus enum: "Pending" | "Running" | "Succeeded" | "Failed" */
  status: string;
  resultJson: string | null;
  errorCode: string | null;
  errorDetail: string | null;
  completedAt: string | null;
}

export interface OperationListItem {
  id: string;
  type: string;
  /** Backend OperationStatus enum: "Pending" | "Running" | "Succeeded" | "Failed" */
  status: string;
  errorCode: string | null;
  completedAt: string | null;
  createdAt: string;
}

export const TERMINAL_STATUSES = new Set(["Succeeded", "Failed"]);

export function isTerminal(status: string): boolean {
  return TERMINAL_STATUSES.has(status);
}

export const operationQueries = {
  list: (page = 1, pageSize = 20) =>
    queryOptions({
      queryKey: [...queryRoots.operations, "list", page, pageSize],
      queryFn: () => {
        const sp = new URLSearchParams({
          page: String(page),
          pageSize: String(pageSize),
        });
        return apiFetch<Paged<OperationListItem>>(
          `operations?${sp.toString()}`,
        );
      },
      staleTime: 10_000,
    }),
  status: (operationId: string) =>
    queryOptions({
      queryKey: [...queryRoots.operations, operationId],
      queryFn: () =>
        apiFetch<OperationStatusResponse>(`operations/${operationId}`),
      staleTime: 0,
    }),
};
