/**
 * Cross-cutting API types shared by every feature. Domain DTOs live in each
 * feature's `schema.ts` (zod) — this file only holds the envelope + error shape
 * that the single `apiFetch` client produces.
 */

/** Success envelope from the backend: `{ data: ... }`. */
export interface ApiResponse<T> {
  data: T;
}

/** RFC 9457 problem+json body the backend returns on error. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** Stable machine code, e.g. `user.email_taken`. The one thing we map to i18n. */
  errorCode?: string;
  /** FluentValidation field errors, when present. */
  errors?: Record<string, string[]>;
}

/**
 * The only error type the UI ever sees. `apiFetch` parses every failure into this;
 * internals (stack traces, backend messages we don't trust) never reach a component.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly errorCode: string | undefined;
  /** Localized message from the backend (Accept-Language), used as a fallback. */
  readonly detail: string | undefined;
  /** Seconds to wait, parsed from `Retry-After` on a 429. */
  readonly retryAfter: number | undefined;
  readonly fieldErrors: Record<string, string[]> | undefined;

  constructor(init: {
    status: number;
    errorCode?: string;
    detail?: string;
    retryAfter?: number;
    fieldErrors?: Record<string, string[]>;
  }) {
    super(init.detail ?? init.errorCode ?? `Request failed (${init.status})`);
    this.name = "ApiError";
    this.status = init.status;
    this.errorCode = init.errorCode;
    this.detail = init.detail;
    this.retryAfter = init.retryAfter;
    this.fieldErrors = init.fieldErrors;
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }
  get isForbidden(): boolean {
    return this.status === 403;
  }
  get isRateLimited(): boolean {
    return this.status === 429;
  }
}

/** Standard paged list shape used by feed/list endpoints. */
export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}
