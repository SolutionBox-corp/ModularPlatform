"use client";

import "./globals.css";
import { useEffect } from "react";
import { AlertCircleIcon } from "lucide-react";
import { Button } from "@/components/ui/button";

interface GlobalErrorProps {
  error: Error & { digest?: string };
  reset: () => void;
}

/**
 * global-error.tsx renders OUTSIDE the next-intl provider (it replaces the root layout that
 * threw), so it cannot use useTranslations. We keep a tiny inline EN+CS string table and pick
 * the locale from the NEXT_LOCALE cookie — the same cookie next-intl reads on a normal request.
 */
const GLOBAL_ERROR_STRINGS = {
  en: {
    heading: "Something went wrong",
    description: "An unexpected error occurred. Please reload the page.",
    tryAgain: "Try again",
  },
  cs: {
    heading: "Něco se pokazilo",
    description: "Došlo k neočekávané chybě. Obnovte prosím stránku.",
    tryAgain: "Zkusit znovu",
  },
} as const;

function readLocale(): "en" | "cs" {
  if (typeof document === "undefined") return "en";
  const match = document.cookie.match(/(?:^|;\s*)NEXT_LOCALE=(\w+)/);
  return match?.[1] === "cs" ? "cs" : "en";
}

/**
 * Global error boundary — catches errors in the root layout. Cannot use next-intl
 * here (the intl provider is inside the root layout that threw). Kept intentionally
 * minimal: a centered message and a hard-reset button. Best-effort reports the error
 * (message + digest) to /api/log on mount — the ClientErrorReporter lives inside the
 * providers this boundary replaced, so we report directly here.
 */
export default function GlobalError({ error, reset }: GlobalErrorProps) {
  useEffect(() => {
    try {
      const body = JSON.stringify({
        type: "error",
        message: error.message || "Global error",
        stack: error.stack,
        digest: error.digest,
        url: typeof window !== "undefined" ? window.location.href : undefined,
        ts: Date.now(),
      });
      if (typeof navigator !== "undefined" && typeof navigator.sendBeacon === "function") {
        const blob = new Blob([body], { type: "application/json" });
        if (navigator.sendBeacon("/api/log", blob)) return;
      }
      void fetch("/api/log", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body,
        keepalive: true,
      }).catch(() => {
        /* best-effort */
      });
    } catch {
      /* never let the error boundary throw */
    }
  }, [error]);

  const locale = readLocale();
  const s = GLOBAL_ERROR_STRINGS[locale];

  return (
    <html lang={locale} suppressHydrationWarning>
      <body className="bg-background text-foreground min-h-screen flex items-center justify-center font-sans">
        <div className="text-center max-w-sm px-8 py-8 space-y-4">
          <AlertCircleIcon className="h-10 w-10 mx-auto text-destructive" />
          <h1 className="text-lg font-semibold">{s.heading}</h1>
          <p className="text-sm text-muted-foreground">{s.description}</p>
          <Button onClick={reset}>{s.tryAgain}</Button>
        </div>
      </body>
    </html>
  );
}
