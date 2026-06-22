"use client";

import { useMemo } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Switch } from "@/components/ui/switch";
import { Skeleton } from "@/components/ui/skeleton";
import { Label } from "@/components/ui/label";
import { useConsents, useGrantConsent, useWithdrawConsent } from "@/features/privacy/hooks";
import { type ConsentResponse } from "@/features/privacy/api";

/** The consent types the platform recognises. The backend stores any string;
 *  we enumerate the ones we surface in the UI so the i18n label key stays in one place.
 *  `labelKey`/`descriptionKey` are resolved against the `privacy` namespace at render time. */
const CONSENT_TYPES: { key: string; labelKey: string; descriptionKey: string }[] = [
  {
    key: "marketing_emails",
    labelKey: "consents.types.marketingEmails.label",
    descriptionKey: "consents.types.marketingEmails.description",
  },
  // `analytics` and `third_party_sharing` were removed (audit follow-up #5): nothing in the app reads
  // them yet, so they were orphaned consent toggles. Re-add a type here once a feature actually gates on it.
];

/** Derive the current boolean state per consent type from the append-only history. */
function currentStateMap(consents: ConsentResponse[]): Record<string, boolean> {
  const map: Record<string, boolean> = {};
  // History is already ordered newest-first from the backend.
  for (const c of consents) {
    if (!(c.consentType in map)) {
      map[c.consentType] = c.granted;
    }
  }
  return map;
}

export function ConsentToggles() {
  const t = useTranslations("privacy");
  const { data, isLoading } = useConsents();
  const grantMutation = useGrantConsent();
  const withdrawMutation = useWithdrawConsent();

  const stateMap = useMemo(() => currentStateMap(data ?? []), [data]);

  function handleToggle(consentType: string, checked: boolean) {
    const mutation = checked ? grantMutation : withdrawMutation;
    mutation.mutate(consentType, {
      onSuccess: () => {
        toast.success(
          checked
            ? t("consents.toast.granted")
            : t("consents.toast.withdrawn"),
        );
      },
    });
  }

  if (isLoading) {
    return (
      <div className="space-y-4">
        {CONSENT_TYPES.map((ct) => (
          <Skeleton key={ct.key} className="h-14 w-full" />
        ))}
      </div>
    );
  }

  return (
    <div className="divide-y divide-border rounded-lg border border-border">
      {CONSENT_TYPES.map((ct) => {
        const isGranted = stateMap[ct.key] ?? false;
        const isBusy =
          (grantMutation.isPending && grantMutation.variables === ct.key) ||
          (withdrawMutation.isPending && withdrawMutation.variables === ct.key);

        return (
          <div
            key={ct.key}
            className="flex items-start justify-between gap-4 px-4 py-3"
          >
            <div className="flex-1 min-w-0 space-y-0.5">
              <Label
                htmlFor={`consent-${ct.key}`}
                className="text-sm font-medium cursor-pointer"
              >
                {t(ct.labelKey)}
              </Label>
              <p className="text-xs text-muted-foreground">{t(ct.descriptionKey)}</p>
            </div>
            <Switch
              id={`consent-${ct.key}`}
              checked={isGranted}
              onCheckedChange={(checked) => handleToggle(ct.key, checked)}
              disabled={isBusy}
              aria-label={t(ct.labelKey)}
            />
          </div>
        );
      })}
    </div>
  );
}
