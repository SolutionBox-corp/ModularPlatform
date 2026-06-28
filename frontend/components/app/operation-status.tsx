"use client";

import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { CheckCircle2Icon, XCircleIcon, LoaderCircleIcon } from "lucide-react";
import { Progress } from "@/components/ui/progress";
import { cn } from "@/lib/utils";
import {
  operationQueries,
  isTerminal,
} from "@/features/operations/api";

interface OperationStatusProps {
  operationId: string;
  /** Called once when the operation reaches a terminal state. */
  onDone?: (status: string) => void;
  className?: string;
}

/**
 * Polls GET /operations/{id} at 2-second intervals until the operation reaches a
 * terminal status (Succeeded | Failed), then calls onDone. Driven entirely by
 * TanStack Query refetchInterval; no manual timers.
 */
export function OperationStatus({
  operationId,
  onDone,
  className,
}: OperationStatusProps) {
  const t = useTranslations("shell");
  const { data } = useQuery({
    ...operationQueries.status(operationId),
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (!status || isTerminal(status)) {
        if (status && onDone) onDone(status);
        return false;
      }
      return 2_000;
    },
  });

  const status = data?.status ?? "Pending";
  const terminal = isTerminal(status);
  // Localized display label for the API status enum; unknown values fall back to the raw status.
  const STATUS_KEYS: Record<string, string> = {
    Pending: "operationStatus.status.pending",
    Running: "operationStatus.status.running",
    Succeeded: "operationStatus.status.succeeded",
    Failed: "operationStatus.status.failed",
  };
  const statusLabel = STATUS_KEYS[status] ? t(STATUS_KEYS[status]) : status;

  return (
    <div className={cn("flex flex-col gap-2", className)} aria-live="polite">
      <div className="flex items-center gap-2 text-sm">
        {terminal ? (
          status === "Succeeded" ? (
            <CheckCircle2Icon className="h-4 w-4 text-success" aria-hidden />
          ) : (
            <XCircleIcon className="h-4 w-4 text-destructive" aria-hidden />
          )
        ) : (
          <LoaderCircleIcon
            className="h-4 w-4 animate-spin text-muted-foreground"
            aria-hidden
          />
        )}
        <span
          className={cn(
            "font-medium",
            status === "Succeeded" && "text-success",
            status === "Failed" && "text-destructive",
            !terminal && "text-muted-foreground",
          )}
        >
          {statusLabel}
        </span>
      </div>

      {!terminal && (
        <Progress
          value={null}
          className="h-1 animate-pulse"
          aria-label={t("operationStatus.inProgress")}
          aria-valuetext={t("operationStatus.inProgress")}
        />
      )}

      {status === "Failed" && data?.errorDetail && (
        <p className="text-xs text-destructive">{data.errorDetail}</p>
      )}
    </div>
  );
}
