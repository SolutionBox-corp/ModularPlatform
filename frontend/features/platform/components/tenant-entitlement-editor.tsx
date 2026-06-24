"use client";

import { useState } from "react";
import { SearchIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { EntitlementToggles } from "./entitlement-toggles";
import { CreateInviteDialog } from "./create-invite-dialog";
import { useTenantDetail } from "@/features/platform/hooks";

interface TenantEntitlementEditorProps {
  /** Controlled active tenant id (e.g. the row selected in the tenants table). */
  tenantId?: string | null;
  onTenantChange?: (id: string) => void;
}

/**
 * Entitlement editor for one tenant. The active tenant comes EITHER from the controlled `tenantId` prop (selected
 * in the table) OR from the manual UUID input. It loads the tenant's PERSISTED entitlements via
 * GET /tenant/admin/tenants/{id} so the switches reflect the real DB state (no longer write-blind).
 */
export function TenantEntitlementEditor({
  tenantId: controlledId,
  onTenantChange,
}: TenantEntitlementEditorProps = {}) {
  const t = useTranslations("platform");
  const [inputId, setInputId] = useState("");
  const [internalId, setInternalId] = useState<string | null>(null);
  const activeTenantId = controlledId ?? internalId;
  const { data: detail, isLoading } = useTenantDetail(activeTenantId ?? "");

  function handleLookup() {
    const id = inputId.trim();
    if (!id) return;
    setInternalId(id);
    onTenantChange?.(id);
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm font-medium">
          {t("entitlementEditor.heading")}
        </CardTitle>
        <CardDescription className="text-xs">
          {t("entitlementEditor.description")}
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="flex items-end gap-2">
          <div className="flex-1 space-y-1.5">
            <Label htmlFor="tenant-id-input">
              {t("entitlementEditor.idLabel")}
            </Label>
            <Input
              id="tenant-id-input"
              value={inputId}
              onChange={(e) => setInputId(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") handleLookup();
              }}
              placeholder={t("entitlementEditor.inputHint")}
              className="font-mono text-xs"
              autoComplete="off"
            />
          </div>
          <Button
            size="sm"
            variant="outline"
            onClick={handleLookup}
            disabled={!inputId.trim()}
          >
            <SearchIcon className="h-3.5 w-3.5 mr-1.5" />
            {t("entitlementEditor.open")}
          </Button>
        </div>

        {activeTenantId && (
          <>
            <Separator />

            <div className="flex items-center justify-between">
              <div className="min-w-0">
                <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-0.5">
                  {t("entitlementEditor.tenantIdLabel")}
                </p>
                <p className="text-sm font-medium truncate">
                  {detail?.name ?? activeTenantId}
                </p>
                <p className="text-xs font-mono text-muted-foreground break-all">
                  {activeTenantId}
                </p>
              </div>
              <CreateInviteDialog tenantId={activeTenantId} />
            </div>

            <Separator />

            <div>
              <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-2">
                {t("entitlementEditor.moduleEntitlements")}
              </p>
              <EntitlementToggles
                tenantId={activeTenantId}
                modules={detail?.modules}
                isLoading={isLoading}
              />
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
