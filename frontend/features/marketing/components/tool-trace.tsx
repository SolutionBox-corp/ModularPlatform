"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { ChevronRightIcon, WrenchIcon } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * Collapsible pretty-printer for an assistant message's tool calls. `toolCallsJson`
 * is an opaque JSON string the backend persists (the agent's tool-use trace). We
 * pretty-print it if it parses, otherwise show the raw string. Display-only.
 */
export function ToolTrace({ toolCallsJson }: { toolCallsJson: string }) {
  const t = useTranslations("marketing");
  const [open, setOpen] = useState(false);

  let pretty = toolCallsJson;
  try {
    pretty = JSON.stringify(JSON.parse(toolCallsJson), null, 2);
  } catch {
    // Not valid JSON — fall back to the raw string.
  }

  return (
    <div className="mt-2 rounded-lg border border-border bg-muted/30 text-xs">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="flex w-full items-center gap-1.5 px-2.5 py-1.5 text-left font-medium text-muted-foreground transition-colors hover:text-foreground"
      >
        <ChevronRightIcon
          className={cn("h-3.5 w-3.5 shrink-0 transition-transform", open && "rotate-90")}
          aria-hidden="true"
        />
        <WrenchIcon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        <span>{t("vibeChat.toolTrace")}</span>
      </button>
      {open && (
        <pre className="overflow-x-auto border-t border-border px-2.5 py-2 font-mono text-[0.7rem] leading-relaxed text-muted-foreground">
          {pretty}
        </pre>
      )}
    </div>
  );
}
