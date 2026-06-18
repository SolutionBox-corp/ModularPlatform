"use client";

import { useRealtimeStatus } from "@/lib/realtime/realtime-provider";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";

/**
 * Small dot + label reflecting the live SSE connection state.
 * Reads from RealtimeProvider (already mounted in authenticated layouts).
 */
export function RealtimeIndicator() {
  const status = useRealtimeStatus();
  const t = useTranslations("realtime");

  return (
    <span
      className="inline-flex items-center gap-1.5 text-xs text-muted-foreground"
      aria-live="polite"
      aria-label={`Realtime: ${t(status)}`}
    >
      <span
        aria-hidden="true"
        className={cn(
          "h-1.5 w-1.5 rounded-full",
          status === "open" && "bg-success",
          status === "connecting" && "animate-pulse bg-warning",
          status === "closed" && "bg-muted-foreground/50",
        )}
      />
      <span className="hidden sm:inline">{t(status)}</span>
    </span>
  );
}
