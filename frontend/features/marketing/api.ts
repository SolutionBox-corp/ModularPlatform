import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";
import { ApiError, type ProblemDetails } from "@/lib/api/types";
import { CSRF_COOKIE, CSRF_HEADER } from "@/lib/auth/csrf";

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

/** GET /v1/marketing/vibe/conversations — ConversationListItem inside PagedResponse. */
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
  messagePage: number;
  messagePageSize: number;
  totalMessageCount: number;
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

  /** GET /v1/marketing/vibe/conversations — paged caller chat threads. */
  vibeConversations: (page = 1, pageSize = 50) =>
    queryOptions({
      queryKey: [...queryRoots.marketing, "conversations", page, pageSize],
      queryFn: () => {
        const sp = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
        return apiFetch<MarketingPaged<ConversationListItem>>(
          `marketing/vibe/conversations?${sp.toString()}`,
        );
      },
      staleTime: 30_000,
    }),

  /** GET /v1/marketing/vibe/conversations/{id} — header + a bounded ordered message window. */
  vibeConversation: (id: string, messagePage = 1, messagePageSize = 50) =>
    queryOptions({
      queryKey: [
        ...queryRoots.marketing,
        "conversations",
        id,
        messagePage,
        messagePageSize,
      ],
      queryFn: () => {
        const sp = new URLSearchParams({
          messagePage: String(messagePage),
          messagePageSize: String(messagePageSize),
        });
        return apiFetch<ConversationDetail>(
          `marketing/vibe/conversations/${id}?${sp.toString()}`,
        );
      },
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

// ---------------------------------------------------------------------------
// Interactive streaming send (token-by-token)
// ---------------------------------------------------------------------------

/**
 * POST /v1/marketing/vibe/conversations/{id}/messages/stream {content}
 * → `text/event-stream` of `delta` events (data = a text chunk) then a final
 * `done` event (data = `[DONE]`). The backend persists BOTH the user turn (before
 * the stream opens) and the assistant turn (after it completes), so the caller
 * should invalidate the conversation query on `done` to swap the streamed text for
 * the persisted message.
 *
 * EventSource can't POST a body, so we use `fetch` + a ReadableStream reader and
 * parse SSE frames by hand. The request goes through the BFF (`/api/bff/...`),
 * which injects the bearer and streams the SSE body straight through.
 *
 * Errors (404 / validation) surface BEFORE the stream opens as a normal RFC 9457
 * response — parsed here into an {@link ApiError}, exactly like `apiFetch`.
 */
export interface StreamMessageCallbacks {
  /** Called for every text delta as it arrives (append to the live assistant bubble). */
  onDelta: (chunk: string) => void;
  /** Called once when the `done` event arrives (the assistant turn is now persisted). */
  onDone?: () => void;
}

export async function streamMessage(
  conversationId: string,
  content: string,
  callbacks: StreamMessageCallbacks,
  signal?: AbortSignal,
): Promise<void> {
  const headers = new Headers();
  headers.set("accept", "text/event-stream");
  headers.set("content-type", "application/json");
  headers.set("accept-language", document.documentElement.lang || "en");
  const csrf = readCsrfCookie();
  if (csrf) headers.set(CSRF_HEADER, csrf);

  const res = await fetch(
    `/api/bff/marketing/vibe/conversations/${conversationId}/messages/stream`,
    {
      method: "POST",
      headers,
      body: JSON.stringify({ content }),
      credentials: "same-origin",
      signal,
    },
  );

  if (!res.ok || !res.body) {
    throw await problemToApiError(res);
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      // SSE frames are separated by a blank line. Process every complete frame in
      // the buffer, keeping the trailing partial frame for the next chunk.
      let sep: number;
      while ((sep = indexOfFrameBoundary(buffer)) !== -1) {
        const frame = buffer.slice(0, sep);
        buffer = buffer.slice(sep).replace(/^(\r?\n)+/, "");
        dispatchFrame(frame, callbacks);
      }
    }
    // Flush any trailing frame that wasn't terminated by a blank line.
    if (buffer.trim().length > 0) {
      dispatchFrame(buffer, callbacks);
    }
  } finally {
    reader.releaseLock();
  }
}

/** Index just past the blank-line frame boundary (`\n\n` or `\r\n\r\n`), or -1. */
function indexOfFrameBoundary(buffer: string): number {
  const lf = buffer.indexOf("\n\n");
  const crlf = buffer.indexOf("\r\n\r\n");
  if (lf === -1) return crlf === -1 ? -1 : crlf + 4;
  if (crlf === -1) return lf + 2;
  return Math.min(lf + 2, crlf + 4);
}

/**
 * Parse one SSE frame (its `event:` + possibly multi-line `data:`) and route it.
 * Per the SSE spec, multiple `data:` lines join with `\n`; a leading space after
 * the colon is stripped. The backend's final `done` event carries `[DONE]`, which
 * is a marker (not assistant text) and must NOT be appended.
 */
function dispatchFrame(frame: string, callbacks: StreamMessageCallbacks): void {
  let event = "message";
  const dataLines: string[] = [];
  for (const rawLine of frame.split("\n")) {
    const line = rawLine.replace(/\r$/, "");
    if (line.startsWith(":")) continue; // comment / heartbeat
    if (line.startsWith("event:")) {
      event = line.slice(6).replace(/^ /, "");
    } else if (line.startsWith("data:")) {
      dataLines.push(line.slice(5).replace(/^ /, ""));
    }
  }
  const data = dataLines.join("\n");

  if (event === "done") {
    callbacks.onDone?.();
  } else if (event === "delta") {
    if (data.length > 0) callbacks.onDelta(data);
  }
}

/** Read the JS-readable CSRF cookie to echo it in `x-csrf-token` (browser-only). */
function readCsrfCookie(): string | undefined {
  const match = document.cookie.match(new RegExp(`(?:^|; )${CSRF_COOKIE}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : undefined;
}

/** Turn a non-OK BFF response into the same {@link ApiError} shape `apiFetch` produces. */
async function problemToApiError(res: Response): Promise<ApiError> {
  let problem: ProblemDetails = {};
  try {
    const contentType = res.headers.get("content-type") ?? "";
    if (contentType.includes("json")) {
      problem = (await res.json()) as ProblemDetails;
    } else {
      problem = { detail: (await res.text()) || undefined };
    }
  } catch {
    problem = {};
  }
  return new ApiError({
    status: res.status,
    errorCode: problem.errorCode,
    detail: problem.detail ?? problem.title,
    fieldErrors: problem.errors,
  });
}
