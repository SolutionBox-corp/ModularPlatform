import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";

// ---------------------------------------------------------------------------
// Response shapes (mirrored from backend C# records — camelCase JSON)
//   Pulls:     Features/Pulls/{TriggerPull,GetPullStatus}
//   Snapshots: Features/Snapshots/ListSnapshots (PagedResponse<SnapshotListItem>)
//   Analyses:  Features/Analyses/{ListAnalyses,GetAnalysis}
//   Vibe:      Features/Vibe/{ListConversations,GetConversation,...}
// PagedResponse<T> = { items, page, pageSize, totalCount } (Cqrs/Paging.cs).
// ---------------------------------------------------------------------------

/** Backend `PagedResponse<T>` — NOTE: `totalCount` (not `total`). */
export interface MarketingPaged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/** GET /v1/marketing/pulls/{id} — PullStatusResponse. */
export interface PullStatusResponse {
  id: string;
  source: string;
  /** "Pending" | "Running" | "Completed" | "Failed" (wire string). */
  status: string;
  errorCode: string | null;
  completedAt: string | null;
}

/** GET /v1/marketing/snapshots — SnapshotListItem. */
export interface SnapshotListItem {
  id: string;
  source: string;
  metricName: string;
  dimension: string | null;
  value: number;
  detailJson: string | null;
  recordedAt: string;
}

/** GET /v1/marketing/analyses — AnalysisListItem. */
export interface AnalysisListItem {
  id: string;
  source: string;
  summary: string;
  analyzedAt: string;
}

/** GET /v1/marketing/analyses/{id} — AnalysisDetail. */
export interface AnalysisDetail {
  id: string;
  source: string;
  summary: string;
  insightsJson: string | null;
  dataPullId: string | null;
  analyzedAt: string;
}

/** GET /v1/marketing/vibe/conversations — ConversationListItem. */
export interface ConversationListItem {
  id: string;
  title: string;
  createdAt: string;
}

/** ConversationMessage — one row in a thread. */
export interface ConversationMessage {
  id: string;
  /** "user" | "assistant" (wire string). */
  role: string;
  content: string;
  toolCallsJson: string | null;
  createdAt: string;
}

/** GET /v1/marketing/vibe/conversations/{id} — ConversationDetail (header + messages). */
export interface ConversationDetail {
  id: string;
  title: string;
  createdAt: string;
  messages: ConversationMessage[];
}

/** POST /v1/marketing/pulls → 202 — TriggerPullResponse. */
export interface TriggerPullResponse {
  dataPullId: string;
}

/** POST /v1/marketing/vibe/conversations → StartConversationResponse. */
export interface StartConversationResponse {
  conversationId: string;
}

/** POST /v1/marketing/vibe/conversations/{id}/messages → 202 — SendMessageResponse. */
export interface SendMessageResponse {
  conversationId: string;
  messageId: string;
}

/** Filter params for the snapshots list. */
export interface SnapshotsParams {
  source?: string;
  pullId?: string;
  page?: number;
  pageSize?: number;
}

// ---------------------------------------------------------------------------
// Query factories — all keys extend queryRoots.marketing
// ---------------------------------------------------------------------------

export const marketingQueries = {
  /** GET /v1/marketing/pulls — paged list of the caller's data pulls. */
  pulls: (page = 1, pageSize = 20) =>
    queryOptions({
      queryKey: [...queryRoots.marketing, "pulls", page, pageSize],
      queryFn: () => {
        const sp = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        return apiFetch<MarketingPaged<PullStatusResponse>>(`marketing/pulls?${sp.toString()}`);
      },
      staleTime: 30_000,
    }),

  /** GET /v1/marketing/pulls/{id} — status of one pull (202 poll target). */
  pullStatus: (id: string) =>
    queryOptions({
      queryKey: [...queryRoots.marketing, "pulls", id],
      queryFn: () => apiFetch<PullStatusResponse>(`marketing/pulls/${id}`),
      staleTime: 10_000,
    }),

  /** GET /v1/marketing/snapshots?source=&pullId= — paged metric snapshots. */
  snapshots: (params: SnapshotsParams = {}) =>
    queryOptions({
      queryKey: [
        ...queryRoots.marketing,
        "snapshots",
        params.source ?? null,
        params.pullId ?? null,
        params.page ?? 1,
        params.pageSize ?? 20,
      ],
      queryFn: () => {
        const sp = new URLSearchParams({
          page: String(params.page ?? 1),
          pageSize: String(params.pageSize ?? 20),
        });
        if (params.source) sp.set("source", params.source);
        if (params.pullId) sp.set("pullId", params.pullId);
        return apiFetch<MarketingPaged<SnapshotListItem>>(`marketing/snapshots?${sp.toString()}`);
      },
      staleTime: 30_000,
    }),

  /** GET /v1/marketing/analyses — paged AI analyses, newest first. */
  analyses: (page = 1, pageSize = 20) =>
    queryOptions({
      queryKey: [...queryRoots.marketing, "analyses", page, pageSize],
      queryFn: () => {
        const sp = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        return apiFetch<MarketingPaged<AnalysisListItem>>(`marketing/analyses?${sp.toString()}`);
      },
      staleTime: 60_000,
    }),

  /** GET /v1/marketing/analyses/{id} — one analysis detail. */
  analysis: (id: string) =>
    queryOptions({
      queryKey: [...queryRoots.marketing, "analyses", id],
      queryFn: () => apiFetch<AnalysisDetail>(`marketing/analyses/${id}`),
      staleTime: 60_000,
    }),

  /** GET /v1/marketing/vibe/conversations — the caller's chat threads. */
  vibeConversations: () =>
    queryOptions({
      queryKey: [...queryRoots.marketing, "conversations"],
      queryFn: () =>
        apiFetch<ConversationListItem[]>("marketing/vibe/conversations"),
      staleTime: 30_000,
    }),

  /** GET /v1/marketing/vibe/conversations/{id} — header + ordered messages. */
  vibeConversation: (id: string) =>
    queryOptions({
      queryKey: [...queryRoots.marketing, "conversations", id],
      queryFn: () =>
        apiFetch<ConversationDetail>(`marketing/vibe/conversations/${id}`),
      staleTime: 10_000,
    }),
};

// ---------------------------------------------------------------------------
// Mutation functions (called from hooks.ts)
// ---------------------------------------------------------------------------

/** POST /v1/marketing/pulls {source} → 202 — kicks off a durable data pull. */
export function triggerPull(source: string): Promise<TriggerPullResponse> {
  return apiFetch<TriggerPullResponse>("marketing/pulls", {
    method: "POST",
    body: { source },
  });
}

/** POST /v1/marketing/vibe/conversations {title?} — starts a new chat thread. */
export function startConversation(
  title?: string,
): Promise<StartConversationResponse> {
  return apiFetch<StartConversationResponse>("marketing/vibe/conversations", {
    method: "POST",
    body: { title },
  });
}

/** POST /v1/marketing/vibe/conversations/{id}/messages {content} → 202. */
export function sendMessage(
  conversationId: string,
  content: string,
): Promise<SendMessageResponse> {
  return apiFetch<SendMessageResponse>(
    `marketing/vibe/conversations/${conversationId}/messages`,
    { method: "POST", body: { content } },
  );
}

/** DELETE /v1/marketing/vibe/conversations/{id} — soft-deletes a thread. */
export function deleteConversation(conversationId: string): Promise<void> {
  return apiFetch<void>(`marketing/vibe/conversations/${conversationId}`, {
    method: "DELETE",
  });
}
