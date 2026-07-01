"use client";

import { BanIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useRevokeTenantInvite, useTenantInvites } from "@/features/platform/hooks";
import type { TenantInviteItem } from "@/features/platform/api";

interface TenantInvitesListProps {
  tenantId: string;
}

export function TenantInvitesList({ tenantId }: TenantInvitesListProps) {
  const t = useTranslations("platform");
  const locale = useLocale();
  const { data, isLoading } = useTenantInvites({ tenantId, limit: 10 });
  const revoke = useRevokeTenantInvite();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">{t("tenantDetail.invites.loading")}</p>;
  }

  if (!data?.items.length) {
    return <p className="text-sm text-muted-foreground">{t("tenantDetail.invites.empty")}</p>;
  }

  return (
    <div className="divide-y rounded-md border">
      {data.items.map((invite) => (
        <div
          key={invite.inviteId}
          className="flex items-center justify-between gap-3 px-3 py-2"
        >
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <Badge variant={badgeVariant(invite.status)} className="text-xs">
                {t(`tenantDetail.invites.status.${invite.status}`)}
              </Badge>
              <span className="text-xs text-muted-foreground">
                {dateLabel(invite, locale)}
              </span>
            </div>
            <p className="mt-1 truncate font-mono text-xs text-muted-foreground">
              {invite.inviteId}
            </p>
          </div>
          {invite.status === "Pending" && (
            <Button
              type="button"
              size="icon-sm"
              variant="ghost"
              onClick={() => revoke.mutate({ tenantId, inviteId: invite.inviteId })}
              disabled={revoke.isPending}
              aria-label={t("tenantDetail.invites.revoke")}
            >
              <BanIcon className="h-3.5 w-3.5" />
            </Button>
          )}
        </div>
      ))}
    </div>
  );
}

function badgeVariant(status: TenantInviteItem["status"]) {
  return status === "Pending" ? "default" : "secondary";
}

function dateLabel(invite: TenantInviteItem, locale: string) {
  const value = invite.revokedAt ?? invite.consumedAt ?? invite.expiresAt;
  return new Date(value).toLocaleDateString(locale, {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}
