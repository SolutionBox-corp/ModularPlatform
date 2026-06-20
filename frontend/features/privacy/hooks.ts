"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryRoots } from "@/lib/api/query-keys";
import { PRIVACY_VERSION } from "@/lib/legal-versions";
import {
  privacyQueries,
  grantConsent,
  withdrawConsent,
  exportPersonalData,
  eraseAccount,
} from "./api";

// ---------------------------------------------------------------------------
// Read
// ---------------------------------------------------------------------------

export function useConsents() {
  return useQuery(privacyQueries.consents());
}

// ---------------------------------------------------------------------------
// Mutations
// ---------------------------------------------------------------------------

export function useGrantConsent() {
  const qc = useQueryClient();
  return useMutation({
    // policyVersion defaults to the active PRIVACY_VERSION so callers (the toggles)
    // only pass the consent type.
    mutationFn: (consentType: string) => grantConsent(consentType, PRIVACY_VERSION),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: [...queryRoots.gdpr, "consents"] });
    },
  });
}

export function useWithdrawConsent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (consentType: string) => withdrawConsent(consentType, PRIVACY_VERSION),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: [...queryRoots.gdpr, "consents"] });
    },
  });
}

export function useExportPersonalData() {
  return useMutation({
    mutationFn: () => exportPersonalData(),
  });
}

export function useEraseAccount() {
  return useMutation({
    mutationFn: () => eraseAccount(),
  });
}
