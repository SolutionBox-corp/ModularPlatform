"use client";

import { BanIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useMachineTokens, useRevokeMachineToken } from "@/features/platform/hooks";
import type { MachineTokenItem } from "@/features/platform/api";

interface MachineTokensListProps {
  tenantId: string;
}

export function MachineTokensList({ tenantId }: MachineTokensListProps) {
  const t = useTranslations("platform");
  const locale = useLocale();
  const { data, isLoading } = useMachineTokens(tenantId);
  const revoke = useRevokeMachineToken();

  if (isLoading) {
    return (
      <p className="text-sm text-muted-foreground">
        {t("tenantDetail.machineTokens.loading")}
      </p>
    );
  }

  if (!data?.items.length) {
    return (
      <p className="text-sm text-muted-foreground">
        {t("tenantDetail.machineTokens.empty")}
      </p>
    );
  }

  return (
    <div className="divide-y rounded-md border">
      {data.items.map((token) => (
        <div
          key={token.id}
          className="flex items-center justify-between gap-3 px-3 py-2"
        >
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={badgeVariant(token.status)} className="text-xs">
                {t(`tenantDetail.machineTokens.status.${token.status}`)}
              </Badge>
              <span className="text-sm font-medium">{token.name}</span>
              <span className="text-xs text-muted-foreground">
                {dateLabel(token, locale, {
                  expiresAt: t("tenantDetail.machineTokens.expiresAt"),
                  revokedAt: t("tenantDetail.machineTokens.revokedAt"),
                })}
              </span>
            </div>
            <p className="mt-1 truncate font-mono text-xs text-muted-foreground">
              {token.machineSubjectId}
            </p>
          </div>
          {token.status === "Active" && (
            <Button
              type="button"
              size="icon-sm"
              variant="ghost"
              onClick={() => revoke.mutate({ tenantId, tokenId: token.id })}
              disabled={revoke.isPending}
              aria-label={t("tenantDetail.machineTokens.revoke")}
            >
              <BanIcon className="h-3.5 w-3.5" />
            </Button>
          )}
        </div>
      ))}
    </div>
  );
}

function badgeVariant(status: MachineTokenItem["status"]) {
  if (status === "Active") return "default";
  if (status === "Revoked") return "destructive";
  return "secondary";
}

function dateLabel(
  token: MachineTokenItem,
  locale: string,
  labels: { expiresAt: string; revokedAt: string },
) {
  const key = token.revokedAt ? "revokedAt" : "expiresAt";
  const value = token.revokedAt ?? token.expiresAt;
  return `${labels[key]}: ${new Date(value).toLocaleDateString(locale, {
    month: "short",
    day: "numeric",
    year: "numeric",
  })}`;
}
