/**
 * Canonical query-key ROOTS, shared so colocated feature factories and the realtime
 * event→invalidate map can never drift. Feature `api.ts` files build their full keys by
 * extending these roots (e.g. `[...queryRoots.billing, "balance"]`); the realtime provider
 * invalidates by the same root prefix. This is the ONE place roots are declared — feature
 * code does NOT invent new top-level roots.
 */
export const queryRoots = {
  identity: ["identity"] as const,
  billing: ["billing"] as const,
  notifications: ["notifications"] as const,
  files: ["files"] as const,
  operations: ["operations"] as const,
  gdpr: ["gdpr"] as const,
  entitlements: ["entitlements"] as const,
  admin: ["admin"] as const,
  marketing: ["marketing"] as const,
} as const;
