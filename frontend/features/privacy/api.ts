import { queryOptions } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api/client";
import { queryRoots } from "@/lib/api/query-keys";
import { PRIVACY_VERSION } from "@/lib/legal-versions";

// ---------------------------------------------------------------------------
// Types matching the backend DTOs (camelCase)
// ---------------------------------------------------------------------------

/** A single append-only consent history entry. The CURRENT state for a type is
 *  the most-recently-recorded row (Granted = true/false). */
export interface ConsentResponse {
  id: string;
  consentType: string;
  granted: boolean;
  recordedAt: string;
  /** Version of the privacy policy in force when this entry was recorded (may be null for legacy rows). */
  policyVersion: string | null;
}

// ---------------------------------------------------------------------------
// Query factories
// ---------------------------------------------------------------------------

export const privacyQueries = {
  /** GET /v1/gdpr/me/consents — full append-only consent history, newest first. */
  consents: () =>
    queryOptions({
      queryKey: [...queryRoots.gdpr, "consents"],
      queryFn: () => apiFetch<ConsentResponse[]>("gdpr/me/consents"),
      staleTime: 30_000,
    }),
};

// ---------------------------------------------------------------------------
// Mutation functions (used by hooks.ts)
// ---------------------------------------------------------------------------

/** POST /v1/gdpr/consents/grant */
export async function grantConsent(
  consentType: string,
  policyVersion: string = PRIVACY_VERSION,
): Promise<{ consentRecordId: string }> {
  return apiFetch<{ consentRecordId: string }>("gdpr/consents/grant", {
    method: "POST",
    body: { consentType, policyVersion },
  });
}

/** POST /v1/gdpr/consents/withdraw */
export async function withdrawConsent(
  consentType: string,
  policyVersion: string = PRIVACY_VERSION,
): Promise<{ consentRecordId: string }> {
  return apiFetch<{ consentRecordId: string }>("gdpr/consents/withdraw", {
    method: "POST",
    body: { consentType, policyVersion },
  });
}

/**
 * GET /v1/gdpr/me/export — synchronous, returns a data-portability document
 * keyed by module name. The caller may download it as JSON.
 */
export async function exportPersonalData(): Promise<Record<string, unknown>> {
  return apiFetch<Record<string, unknown>>("gdpr/me/export");
}

/**
 * POST /v1/gdpr/me/erase — irreversible account erasure. Returns nothing on
 * success (the backend emits an integration event to fan out erasure).
 */
export async function eraseAccount(): Promise<void> {
  await apiFetch<void>("gdpr/me/erase", { method: "POST" });
}
