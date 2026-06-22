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

/**
 * Entitlement editor that operates on an admin-entered tenant UUID.
 *
 * LIMITATION: No GET /tenant/admin/tenants/{id} or cross-tenant entitlement
 * read endpoint exists on the backend. This component therefore shows the
 * default module set with unknown enabled state (displayed as off) and lets
 * the admin fire PUT /tenant/admin/tenants/{id}/entitlements/{key} directly.
 * The switch position reflects the last action taken in this session, not the
 * persisted value.
 *
 * When a backend read endpoint is added, replace the `fallbackToDefaults` prop
 * with a `useQuery` that fetches the live entitlement snapshot.
 */
export function TenantEntitlementEditor() {
  const t = useTranslations("platform");
  const [inputId, setInputId] = useState("");
  const [activeTenantId, setActiveTenantId] = useState<string | null>(null);

  function handleLookup() {
    const id = inputId.trim();
    if (!id) return;
    setActiveTenantId(id);
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm font-medium">
          {t("entitlementEditor.heading")}
        </CardTitle>
        <CardDescription className="text-xs">
          {t("entitlementEditor.description")}
          <br />
          <span className="text-muted-foreground/70">
            {t("entitlementEditor.note")}
          </span>
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
              <div>
                <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide mb-0.5">
                  {t("entitlementEditor.tenantIdLabel")}
                </p>
                <p className="text-xs font-mono text-foreground break-all">
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
                modules={undefined}
                isLoading={false}
                fallbackToDefaults
              />
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
