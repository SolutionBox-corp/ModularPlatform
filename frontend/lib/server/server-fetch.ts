import "server-only";
import { getLocale } from "next-intl/server";
import { backendFetch } from "@/lib/server/backend";
import { handleResponse } from "@/lib/api/client";
import type { ApiFetchOptions } from "@/lib/api/client";

/**
 * Server-side implementation of `apiFetch`, registered on `globalThis` so the
 * isomorphic client (lib/api/client) can delegate to it during RSC render without
 * the client bundle ever importing server-only code.
 *
 * Prefetch is GET-only and READ-ONLY: it never rotates the refresh token (cookies
 * are read-only in RSC render — see backend.ts), so on token expiry the prefetch
 * simply fails and the browser refetches through the BFF. Mutations always run in
 * the browser. Imported once from the root layout to perform the registration.
 *
 * The request locale is forwarded as Accept-Language so SSR-prefetched data is
 * localised consistently with what the browser client would receive.
 */
async function serverApiFetch<T>(path: string, opts: ApiFetchOptions = {}): Promise<T> {
  const locale = await getLocale();
  const res = await backendFetch(`/${path}`, {
    allowRefresh: false,
    signal: opts.signal,
    headers: { "accept-language": locale },
  });
  return handleResponse<T>(res, /* fromBrowser */ false);
}

globalThis.__mpServerApiFetch = serverApiFetch;

export {};
