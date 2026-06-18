import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";

export interface OperationStatusResponse {
  id: string;
  type: string;
  /** "Pending" | "Running" | "Completed" | "Failed" | "Cancelled" */
  status: string;
  resultJson: string | null;
  errorCode: string | null;
  errorDetail: string | null;
  completedAt: string | null;
}

export const TERMINAL_STATUSES = new Set(["Completed", "Failed", "Cancelled"]);

export function isTerminal(status: string): boolean {
  return TERMINAL_STATUSES.has(status);
}

export const operationQueries = {
  status: (operationId: string) =>
    queryOptions({
      queryKey: [...queryRoots.operations, operationId],
      queryFn: () =>
        apiFetch<OperationStatusResponse>(`operations/${operationId}`),
      staleTime: 0,
    }),
};
