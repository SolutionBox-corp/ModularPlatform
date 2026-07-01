"use client";

import Link from "next/link";
import { ChevronLeftIcon, BuildingIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { EntitlementToggles } from "./entitlement-toggles";
import { CreateInviteDialog } from "./create-invite-dialog";
import { EditTenantDialog } from "./edit-tenant-dialog";
import { MachineTokenDialog } from "./machine-token-dialog";
import { MachineTokensList } from "./machine-tokens-list";
import { TenantInvitesList } from "./tenant-invites-list";
import { useTenantDetail } from "@/features/platform/hooks";

interface TenantDetailContentProps {
  tenantId: string;
  canIssueMachineTokens: boolean;
}

/**
 * Full detail view for a tenant UUID. Loads the registry row + PERSISTED entitlements via
 * GET /tenant/admin/tenants/{id} so the entitlement switches reflect the real DB state.
 */
export function TenantDetailContent({
  tenantId,
  canIssueMachineTokens,
}: TenantDetailContentProps) {
  const t = useTranslations("platform");
  const { data: detail, isLoading } = useTenantDetail(tenantId);
  return (
    <div className="space-y-6">
      {/* Back link */}
      <Link
        href="/platform/tenants"
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors"
      >
        <ChevronLeftIcon className="h-4 w-4" />
        {t("tenantDetail.back")}
      </Link>

      {/* Header */}
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted text-muted-foreground">
            <BuildingIcon className="h-5 w-5" aria-hidden="true" />
          </span>
          <div>
            <h1 className="text-xl font-semibold tracking-tight">
              {detail?.name ?? t("tenantDetail.heading")}
            </h1>
            <p className="text-xs font-mono text-muted-foreground">
              {detail ? `${detail.subdomain} · ${tenantId}` : tenantId}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          {detail && (
            <EditTenantDialog
              tenantId={tenantId}
              name={detail.name}
              subdomain={detail.subdomain}
            />
          )}
          {detail && canIssueMachineTokens && (
            <MachineTokenDialog tenantId={tenantId} tenantName={detail.name} />
          )}
          <CreateInviteDialog tenantId={tenantId} />
        </div>
      </div>

      {/* Content grid */}
      <div className="grid gap-6 lg:grid-cols-2">
        {/* Entitlements card */}
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium">
              {t("tenantDetail.entitlements.heading")}
            </CardTitle>
            <CardDescription className="text-xs">
              {t("tenantDetail.entitlements.description")}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <EntitlementToggles
              tenantId={tenantId}
              modules={detail?.modules}
              isLoading={isLoading}
            />
          </CardContent>
        </Card>

        {/* Invites */}
        <div className="space-y-4">
          {canIssueMachineTokens && (
            <Card>
              <CardHeader>
                <CardTitle className="text-sm font-medium">
                  {t("tenantDetail.machineTokens.heading")}
                </CardTitle>
                <CardDescription className="text-xs">
                  {t("tenantDetail.machineTokens.description")}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <MachineTokensList tenantId={tenantId} />
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle className="text-sm font-medium">
                {t("tenantDetail.invite.heading")}
              </CardTitle>
              <CardDescription className="text-xs">
                {t("tenantDetail.invite.description")}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="space-y-4">
                <CreateInviteDialog tenantId={tenantId} />
                <TenantInvitesList tenantId={tenantId} />
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
