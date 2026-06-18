"use client";

import { useState } from "react";
import { DownloadIcon, CheckCircle2Icon, LoaderCircleIcon } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { useExportPersonalData } from "@/features/privacy/hooks";

/**
 * ExportDataFlow
 *
 * The backend GET /gdpr/me/export is a synchronous query: it fans out across all
 * modules that hold PII and returns the assembled document in-band (200 + {data}).
 * There is no 202 / operation-based flow for this endpoint — the export runs
 * synchronously within the request. We therefore:
 *   1. Call the endpoint (may take a few seconds for large datasets).
 *   2. Trigger a browser download of the JSON blob when the response arrives.
 */
export function ExportDataFlow() {
  const { mutate, isPending } = useExportPersonalData();
  const [exported, setExported] = useState(false);

  function handleExport() {
    mutate(void 0, {
      onSuccess: (document) => {
        const json = JSON.stringify(document, null, 2);
        const blob = new Blob([json], { type: "application/json" });
        const url = URL.createObjectURL(blob);
        const anchor = window.document.createElement("a");
        anchor.href = url;
        anchor.download = `personal-data-export-${new Date().toISOString().slice(0, 10)}.json`;
        anchor.click();
        URL.revokeObjectURL(url);
        setExported(true);
        toast.success("Your data export is ready and has been downloaded.");
      },
    });
  }

  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">
        Download a copy of all personal data we hold about you. The export
        includes your profile, consents, notifications, billing history, and
        any other data associated with your account.
      </p>

      <div className="flex items-center gap-3">
        <Button
          variant="outline"
          onClick={handleExport}
          disabled={isPending}
          className="gap-1.5"
        >
          {isPending ? (
            <LoaderCircleIcon className="h-4 w-4 animate-spin" aria-hidden />
          ) : (
            <DownloadIcon className="h-4 w-4" aria-hidden />
          )}
          {isPending ? "Preparing export…" : "Download my data"}
        </Button>

        {exported && !isPending && (
          <span className="flex items-center gap-1 text-xs text-success">
            <CheckCircle2Icon className="h-3.5 w-3.5" aria-hidden />
            Downloaded
          </span>
        )}
      </div>
    </div>
  );
}
