"use client";

import { useEffect } from "react";

/**
 * ClientErrorReporter
 *
 * Best-effort, no-SDK client error reporting. Attaches `window.onerror` and an
 * `unhandledrejection` listener and POSTs a small JSON payload to `/api/log`, where it
 * is logged server-side. Uses `navigator.sendBeacon` when available (survives page
 * unload), else `fetch(..., { keepalive: true })`. Never blocks render and never throws.
 *
 * A per-session flood cap keeps a runaway error loop from hammering the endpoint.
 */

const MAX_REPORTS_PER_SESSION = 10;
let reported = 0;

function send(payload: { type: "error" | "rejection"; message: string; stack?: string; url?: string }): void {
  if (reported >= MAX_REPORTS_PER_SESSION) return;
  reported += 1;

  const body = JSON.stringify({ ...payload, ts: Date.now() });
  try {
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
    /* never let reporting throw */
  }
}

export function ClientErrorReporter(): null {
  useEffect(() => {
    function onError(event: ErrorEvent): void {
      send({
        type: "error",
        message: event.message || "Unknown error",
        stack: event.error instanceof Error ? event.error.stack : undefined,
        url: typeof window !== "undefined" ? window.location.href : undefined,
      });
    }

    function onRejection(event: PromiseRejectionEvent): void {
      const reason = event.reason;
      send({
        type: "rejection",
        message: reason instanceof Error ? reason.message : String(reason),
        stack: reason instanceof Error ? reason.stack : undefined,
        url: typeof window !== "undefined" ? window.location.href : undefined,
      });
    }

    window.addEventListener("error", onError);
    window.addEventListener("unhandledrejection", onRejection);
    return () => {
      window.removeEventListener("error", onError);
      window.removeEventListener("unhandledrejection", onRejection);
    };
  }, []);

  return null;
}
