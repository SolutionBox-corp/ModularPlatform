"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { SearchIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { AuditTrailTable } from "@/features/identity-admin/components/audit-trail-table";
import { usePlatformUserAudit } from "@/features/platform/hooks";

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface PlatformAuditContentProps {
  /** User id from the ?userId= search param, if present. */
  userId?: string;
}

/**
 * Platform-admin audit viewer. Reuses the identity-admin `AuditTrailTable` and
 * `UserAuditTrailResponse` type, but drives it with the cross-tenant
 * `platformQueries.userAudit` query (gated by audit.read on the backend).
 *
 * When no userId is in the URL, a UUID lookup field is shown; submitting
 * navigates to `?userId=...` so the trail is deep-linkable / refreshable.
 */
export function PlatformAuditContent({ userId }: PlatformAuditContentProps) {
  const t = useTranslations("platform");
  const router = useRouter();
  const [inputValue, setInputValue] = useState(userId ?? "");
  const [inputError, setInputError] = useState<string | null>(null);

  const handleLookup = () => {
    const trimmed = inputValue.trim();
    if (!UUID_RE.test(trimmed)) {
      setInputError(t("audit.lookup.invalid"));
      return;
    }
    setInputError(null);
    router.push(`/platform/audit?userId=${trimmed}`);
  };

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-sm font-medium">
            {t("audit.lookup.heading")}
          </CardTitle>
          <CardDescription className="text-xs">
            {t("audit.lookup.description")}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex gap-2 items-end">
            <div className="flex-1 space-y-1">
              <Label htmlFor="audit-user-id" className="text-xs font-medium">
                {t("audit.lookup.label")}
              </Label>
              <Input
                id="audit-user-id"
                value={inputValue}
                onChange={(e) => {
                  setInputValue(e.target.value);
                  if (inputError) setInputError(null);
                }}
                placeholder={t("audit.lookup.inputHint")}
                className="h-8 text-sm font-mono"
                aria-invalid={!!inputError}
                aria-describedby={inputError ? "audit-user-id-error" : undefined}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault();
                    handleLookup();
                  }
                }}
              />
              {inputError && (
                <p id="audit-user-id-error" className="text-xs text-destructive">
                  {inputError}
                </p>
              )}
            </div>
            <Button
              size="sm"
              variant="outline"
              onClick={handleLookup}
              disabled={inputValue.trim().length === 0}
              className="h-8 gap-1.5 shrink-0"
            >
              <SearchIcon className="h-3.5 w-3.5" />
              {t("audit.lookup.submit")}
            </Button>
          </div>
        </CardContent>
      </Card>

      {userId && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium">
              {t("audit.trail.heading")}
            </CardTitle>
            <CardDescription className="text-xs font-mono">
              {userId}
            </CardDescription>
          </CardHeader>
          <CardContent className="p-0">
            <PlatformAuditTrail userId={userId} />
          </CardContent>
        </Card>
      )}
    </div>
  );
}

/** Fetches the cross-tenant audit trail and renders the shared table. */
function PlatformAuditTrail({ userId }: { userId: string }) {
  const { data, isLoading } = usePlatformUserAudit(userId);
  return <AuditTrailTable entries={data?.entries} isLoading={isLoading} />;
}
