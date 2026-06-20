import type { QueryKey } from "@tanstack/react-query";
import { queryRoots } from "@/lib/api/query-keys";

/**
 * The ONE mapping from a backend SSE `eventType` to the query-key prefixes to invalidate.
 * The realtime provider holds NO domain state — it just looks up the event here and calls
 * `invalidateQueries`. Invalidation is idempotent, so replayed/duplicate events are harmless.
 *
 * Add a row when a new server event should refresh a screen. The `json` payload is ignored
 * for invalidation (we refetch the source of truth); use `setQueryData` only for hot-append.
 */
export const eventTypeToQueryKeys: Record<string, QueryKey[]> = {
  // Billing — credit ledger / balance changes.
  "billing.credits_changed": [[...queryRoots.billing, "balance"], [...queryRoots.billing]],
  "billing.subscription_changed": [[...queryRoots.billing, "subscription"], [...queryRoots.billing, "balance"]],
  // Notifications — the backend (SendNotificationHandler) publishes exactly "notification".
  "notification": [[...queryRoots.notifications]],
  // Operations — aspirational; backend currently uses client polling (202 + GET /operations/{id}).
  // These entries are ready for when the backend starts publishing operation SSE events.
  "operation.updated": [[...queryRoots.operations]],
  "operation.completed": [[...queryRoots.operations]],
};

/** Keys to invalidate for an event, or null if we don't care about this type. */
export function keysForEvent(eventType: string): QueryKey[] | null {
  return eventTypeToQueryKeys[eventType] ?? null;
}
