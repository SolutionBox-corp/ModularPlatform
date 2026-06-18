"use client";

import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { EventSourcePlus, type EventSourceController } from "event-source-plus";
import { useQueryClient } from "@tanstack/react-query";
import { keysForEvent } from "@/lib/realtime/event-map";

/**
 * The ONE realtime provider. A single SSE stream to the BFF (`/api/bff/realtime/stream`);
 * the BFF injects the bearer from the session, so no token is ever in the browser. The
 * library owns reconnect/backoff and Last-Event-ID resume. The provider holds NO domain
 * state — each event maps to query keys via {@link keysForEvent} and triggers invalidation.
 */

type ConnectionStatus = "connecting" | "open" | "closed";

const RealtimeContext = createContext<ConnectionStatus>("closed");

export function useRealtimeStatus(): ConnectionStatus {
  return useContext(RealtimeContext);
}

export function RealtimeProvider({ children, enabled = true }: { children: ReactNode; enabled?: boolean }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<ConnectionStatus>("closed");
  const controllerRef = useRef<EventSourceController | null>(null);

  useEffect(() => {
    if (!enabled) return;

    const source = new EventSourcePlus("/api/bff/realtime/stream", {
      retryStrategy: "always",
      maxRetryInterval: 30_000,
      credentials: "same-origin",
    });

    const connect = () => {
      setStatus("connecting");
      controllerRef.current = source.listen({
        onMessage(message) {
          setStatus("open");
          const keys = keysForEvent(message.event);
          if (!keys) return;
          for (const queryKey of keys) {
            void queryClient.invalidateQueries({ queryKey });
          }
        },
        onResponseError({ response }) {
          // A 401 means the BFF could not refresh → hard logout. Other statuses: let the
          // library keep retrying with backoff.
          if (response.status === 401) {
            controllerRef.current?.abort("unauthorized");
            window.location.assign("/login?reason=expired");
          }
        },
        onRequestError() {
          setStatus("connecting");
        },
      });
      // Fetch-based SSE has no reliable "opened" callback (ofetch defers onResponse until
      // the streamed body settles, which never happens for an open stream). The library
      // maintains + auto-retries the connection, so once we're listening we treat it as
      // live; onRequestError/onResponseError downgrade it on a real failure.
      setStatus("open");
      return controllerRef.current;
    };

    connect();

    // Pause the stream when the tab is hidden, resume when visible — saves a connection
    // and a flurry of catch-up invalidations are handled by Last-Event-ID on resume.
    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        controllerRef.current?.abort("hidden");
        setStatus("closed");
      } else if (!controllerRef.current || status === "closed") {
        connect();
      }
    };
    document.addEventListener("visibilitychange", onVisibility);

    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      controllerRef.current?.abort("unmount");
      controllerRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled, queryClient]);

  return <RealtimeContext.Provider value={status}>{children}</RealtimeContext.Provider>;
}
