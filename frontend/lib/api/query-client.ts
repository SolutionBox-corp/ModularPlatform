import { QueryClient, QueryCache, MutationCache, defaultShouldDehydrateQuery, isServer } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";

/**
 * One QueryClient factory. Per-request on the server (a singleton would leak data
 * across users); a module singleton in the browser. Centralized error surfacing lives
 * here via QueryCache/MutationCache onError → one sonner toast. Nothing is swallowed.
 */

function notify(error: unknown): void {
  // Only toast in the browser; 401 already triggers a hard redirect in apiFetch.
  if (typeof window === "undefined") return;
  if (error instanceof ApiError && error.isUnauthorized) return;
  toast.error(toDisplayMessage(error, currentLocale()));
}

function makeQueryClient(): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({ onError: notify }),
    mutationCache: new MutationCache({ onError: notify }),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: (failureCount, error) => {
          // Don't retry 4xx (client/auth/validation). Retry transient 5xx a couple times.
          if (error instanceof ApiError && error.status < 500) return false;
          return failureCount < 2;
        },
        refetchOnWindowFocus: false,
      },
      dehydrate: {
        // Include pending queries so streamed server prefetches hydrate without a flash.
        shouldDehydrateQuery: (query) =>
          defaultShouldDehydrateQuery(query) || query.state.status === "pending",
      },
    },
  });
}

let browserQueryClient: QueryClient | undefined;

export function getQueryClient(): QueryClient {
  if (isServer) {
    // Server: always a fresh client per request.
    return makeQueryClient();
  }
  // Browser: reuse one client across the app.
  browserQueryClient ??= makeQueryClient();
  return browserQueryClient;
}
