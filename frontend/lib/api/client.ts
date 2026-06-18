import { ApiError, type ApiResponse, type ProblemDetails } from "@/lib/api/types";
import { CSRF_COOKIE, CSRF_HEADER } from "@/lib/auth/csrf";

/**
 * The ONE thing that talks to `/v1` (via the BFF). Isomorphic:
 *  - in the browser it calls the relative `/api/bff/...` proxy (cookies authenticate);
 *  - during RSC render it delegates to a server-only fetch registered on `globalThis`
 *    (no static import of server code into the client bundle).
 * It unwraps the `{data}` envelope and turns every failure into a typed {@link ApiError}.
 */

export interface ApiFetchOptions {
  method?: string;
  /** JSON-serializable body, or a FormData/Blob for uploads. */
  body?: unknown;
  headers?: Record<string, string>;
  signal?: AbortSignal;
  /** Override Accept-Language (server prefetch passes the request locale). */
  locale?: string;
}

type ServerApiFetch = <T>(path: string, opts?: ApiFetchOptions) => Promise<T>;

declare global {
  var __mpServerApiFetch: ServerApiFetch | undefined;
}

export async function apiFetch<T>(path: string, opts: ApiFetchOptions = {}): Promise<T> {
  if (typeof window === "undefined") {
    const serverFetch = globalThis.__mpServerApiFetch;
    if (!serverFetch) {
      throw new Error("Server apiFetch not registered — import lib/server/server-fetch in a server module.");
    }
    return serverFetch<T>(path, opts);
  }
  return browserApiFetch<T>(path, opts);
}

async function browserApiFetch<T>(path: string, opts: ApiFetchOptions): Promise<T> {
  const method = (opts.method ?? "GET").toUpperCase();
  const headers = new Headers(opts.headers);
  headers.set("accept", "application/json");
  headers.set("accept-language", opts.locale ?? document.documentElement.lang ?? "en");

  let body: BodyInit | undefined;
  if (opts.body instanceof FormData || opts.body instanceof Blob) {
    body = opts.body;
  } else if (opts.body !== undefined && method !== "GET" && method !== "HEAD") {
    headers.set("content-type", "application/json");
    body = JSON.stringify(opts.body);
  }

  if (method !== "GET" && method !== "HEAD") {
    const csrf = readCookie(CSRF_COOKIE);
    if (csrf) headers.set(CSRF_HEADER, csrf);
  }

  const res = await fetch(`/api/bff/${path}`, {
    method,
    headers,
    body,
    signal: opts.signal,
    credentials: "same-origin",
  });

  return handleResponse<T>(res, /* fromBrowser */ true);
}

export async function handleResponse<T>(res: Response, fromBrowser: boolean): Promise<T> {
  if (res.status === 204) return undefined as T;

  if (res.ok) {
    const contentType = res.headers.get("content-type") ?? "";
    if (!contentType.includes("application/json")) {
      return (await res.text()) as unknown as T;
    }
    const json = (await res.json()) as ApiResponse<T> | T;
    return json && typeof json === "object" && "data" in (json as object)
      ? (json as ApiResponse<T>).data
      : (json as T);
  }

  const problem = await parseProblem(res);

  // A 401 from the BFF means the session is gone / refresh failed → hard logout.
  if (res.status === 401 && fromBrowser) {
    redirectToLogin();
  }

  throw new ApiError({
    status: res.status,
    errorCode: problem.errorCode,
    detail: problem.detail ?? problem.title,
    retryAfter: parseRetryAfter(res.headers.get("retry-after")),
    fieldErrors: problem.errors,
  });
}

async function parseProblem(res: Response): Promise<ProblemDetails> {
  try {
    const contentType = res.headers.get("content-type") ?? "";
    if (contentType.includes("json")) {
      return (await res.json()) as ProblemDetails;
    }
    const text = await res.text();
    return { detail: text || undefined };
  } catch {
    return {};
  }
}

function parseRetryAfter(value: string | null): number | undefined {
  if (!value) return undefined;
  const seconds = Number(value);
  if (Number.isFinite(seconds)) return seconds;
  const date = Date.parse(value);
  return Number.isNaN(date) ? undefined : Math.max(0, Math.round((date - Date.now()) / 1000));
}

let redirecting = false;
function redirectToLogin(): void {
  if (redirecting) return;
  redirecting = true;
  const next = encodeURIComponent(window.location.pathname + window.location.search);
  window.location.assign(`/login?reason=expired&next=${next}`);
}

function readCookie(name: string): string | undefined {
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : undefined;
}
