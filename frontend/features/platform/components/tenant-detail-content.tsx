"use client";

import Link from "next/link";
import { ChevronLeftIcon, BuildingIcon } from "lucide-react";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { EntitlementToggles } from "./entitlement-toggles";
import { CreateInviteDialog } from "./create-invite-dialog";
import { PlatformBillingCard } from "./platform-billing-card";

interface TenantDetailContentProps {
  tenantId: string;
}

/**
 * Renders the full detail view for a tenant UUID.
 *
 * LIMITATION: The backend does not expose GET /tenant/admin/tenants/{id} or a
 * cross-tenant entitlement read. This page provides:
 *   - A direct entitlement editor (PUT still works by ID regardless of context).
 *   - The platform billing status card (token-scoped, shows the admin's own tenant).
 *   - Invite creation for the given tenant ID.
 *
 * When the backend adds a GET-by-id endpoint, replace the static header and
 * feed the response into the EntitlementToggles modules prop.
 */
export function TenantDetailContent({ tenantId }: TenantDetailContentProps) {
  return (
    <div className="space-y-6">
      {/* Back link */}
      <Link
        href="/platform/tenants"
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors"
      >
        <ChevronLeftIcon className="h-4 w-4" />
        All tenants
      </Link>

      {/* Header */}
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted text-muted-foreground">
            <BuildingIcon className="h-5 w-5" aria-hidden="true" />
          </span>
          <div>
            <h1 className="text-xl font-semibold tracking-tight">
              Tenant detail
            </h1>
            <p className="text-xs font-mono text-muted-foreground">{tenantId}</p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <CreateInviteDialog tenantId={tenantId} />
        </div>
      </div>

      {/* Content grid */}
      <div className="grid gap-6 lg:grid-cols-2">
        {/* Entitlements card */}
        <Card>
          <CardHeader>
            <CardTitle className="text-sm font-medium">
              Module entitlements
            </CardTitle>
            <CardDescription className="text-xs">
              Toggle individual modules on or off for this tenant. Changes are
              effective immediately (the entitlement guard reads live).
            </CardDescription>
          </CardHeader>
          <CardContent>
            {/* No GET-by-id endpoint exists yet. Render the default module set
                so the admin can toggle entitlements. The PUT works by tenantId
                regardless; current enabled state is unknown (shown as off). */}
            <EntitlementToggles
              tenantId={tenantId}
              modules={undefined}
              isLoading={false}
              fallbackToDefaults
            />
            <p className="mt-3 text-xs text-muted-foreground">
              Current enabled state is unknown — no GET /tenant/admin/tenants/
              {"{id}"} endpoint exists yet. Toggling sends the PUT; the switch
              reflects your action, not the persisted value.
            </p>
          </CardContent>
        </Card>

        {/* Platform billing */}
        <div className="space-y-4">
          <PlatformBillingCard />

          <Card>
            <CardHeader>
              <CardTitle className="text-sm font-medium">Invite</CardTitle>
              <CardDescription className="text-xs">
                Mint a single-use invite so a new member can join this
                workspace.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <CreateInviteDialog tenantId={tenantId} />
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
