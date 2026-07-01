"use client";

import { useLocale, useTranslations } from "next-intl";
import { CheckCircle2Icon, XCircleIcon } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/app/empty-state";
import { HistoryIcon } from "lucide-react";
import { useConsents } from "@/features/privacy/hooks";

/**
 * Renders the append-only consent change history (grant/withdraw events over time)
 * the backend already returns from GET /gdpr/me/consents — newest first, with the
 * policy version in force at the time. This is the auditable proof a user can see
 * of every consent decision they made; the toggles above only show the CURRENT state.
 */
export function ConsentHistory() {
  const t = useTranslations("privacy");
  const locale = useLocale();
  const { data, isLoading } = useConsents();

  if (isLoading) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-12 w-full" />
        ))}
      </div>
    );
  }

  if (!data || data.length === 0) {
    return (
      <EmptyState
        icon={HistoryIcon}
        title={t("consents.history.emptyTitle")}
        description={t("consents.history.emptyDescription")}
      />
    );
  }

  return (
    <ul className="divide-y divide-border rounded-lg border border-border">
      {data.map((entry) => (
        <li key={entry.id} className="flex items-start gap-3 px-4 py-3">
          {entry.granted ? (
            <CheckCircle2Icon
              className="mt-0.5 h-4 w-4 shrink-0 text-success"
              aria-hidden
            />
          ) : (
            <XCircleIcon
              className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground"
              aria-hidden
            />
          )}
          <div className="min-w-0 flex-1">
            <p className="text-sm">
              <span className="font-medium">
                {consentTypeLabel(t, entry.consentType)}
              </span>{" "}
              <span className="text-muted-foreground">
                {entry.granted
                  ? t("consents.history.granted")
                  : t("consents.history.withdrawn")}
              </span>
            </p>
            <p className="text-xs text-muted-foreground">
              {new Date(entry.recordedAt).toLocaleString(locale, {
                dateStyle: "medium",
                timeStyle: "short",
              })}
              {entry.policyVersion
                ? ` · ${t("consents.history.policyVersion", { version: entry.policyVersion })}`
                : ""}
            </p>
          </div>
        </li>
      ))}
    </ul>
  );
}

/** Map a known consent type to its localized label; fall back to the raw key. */
function consentTypeLabel(
  t: ReturnType<typeof useTranslations<"privacy">>,
  consentType: string,
): string {
  if (consentType === "marketing_emails") {
    return t("consents.types.marketingEmails.label");
  }
  if (consentType === "cookie_analytics") {
    return t("consents.types.cookieAnalytics.label");
  }
  if (consentType === "cookie_marketing") {
    return t("consents.types.cookieMarketing.label");
  }
  return consentType;
}
