"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Switch } from "@/components/ui/switch";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { useSetEntitlement } from "@/features/platform/hooks";
import type { PlatformBillingModuleView } from "@/features/platform/api";

/** Known product module keys in display order. Keys must match backend RequireModule keys. */
const KNOWN_MODULE_KEYS = [
  "billing",
  "notifications",
  "files",
  "operations",
  "gdpr",
  "marketing",
] as const;

interface EntitlementTogglesProps {
  tenantId: string;
  /** The tenant's PERSISTED entitlements (from GET /tenant/admin/tenants/{id}). Undefined while loading. */
  modules: PlatformBillingModuleView[] | undefined;
  isLoading?: boolean;
}

/**
 * One Switch per known module. The base position is the PERSISTED entitlement; a toggle applies an optimistic
 * override and fires PUT /tenant/admin/tenants/{id}/entitlements/{key}, reverting the override on error. Overrides
 * are cleared whenever the selected tenant changes so they never bleed across tenants.
 */
export function EntitlementToggles({
  tenantId,
  modules,
  isLoading = false,
}: EntitlementTogglesProps) {
  const t = useTranslations("platform");
  const mutation = useSetEntitlement();
  const [overrideState, setOverrideState] = useState<{
    tenantId: string;
    values: Record<string, boolean>;
  }>({ tenantId, values: {} });
  const overrides = overrideState.tenantId === tenantId ? overrideState.values : {};

  const persisted = new Map((modules ?? []).map((m) => [m.key, m]));

  const moduleLabel = (key: string): string =>
    (KNOWN_MODULE_KEYS as readonly string[]).includes(key)
      ? t(`entitlements.modules.${key}`)
      : key;

  if (isLoading) {
    return (
      <div className="space-y-3">
        {Array.from({ length: KNOWN_MODULE_KEYS.length }).map((_, i) => (
          <div key={i} className="flex items-center justify-between py-2">
            <Skeleton className="h-4 w-28" />
            <Skeleton className="h-5 w-8 rounded-full" />
          </div>
        ))}
      </div>
    );
  }

  function handleToggle(key: string, checked: boolean, tier: string | null) {
    setOverrideState((prev) => ({
      tenantId,
      values: {
        ...(prev.tenantId === tenantId ? prev.values : {}),
        [key]: checked,
      },
    }));
    void mutation
      .mutateAsync({ tenantId, moduleKey: key, enabled: checked, tier })
      .catch(() => {
        setOverrideState((prev) => {
          const next = { ...(prev.tenantId === tenantId ? prev.values : {}) };
          delete next[key];
          return { tenantId, values: next };
        });
      });
  }

  return (
    <ul className="space-y-1" role="list">
      {KNOWN_MODULE_KEYS.map((key) => {
        const row = persisted.get(key);
        const enabled = overrides[key] ?? row?.enabled ?? false;
        const tier = row?.tier ?? null;
        const label = moduleLabel(key);
        const isPending =
          mutation.isPending && mutation.variables?.moduleKey === key;

        return (
          <li
            key={key}
            className="flex items-center justify-between rounded-lg px-3 py-2.5 hover:bg-muted/40 transition-colors"
          >
            <div className="flex items-center gap-2.5 min-w-0">
              <span className="text-sm font-medium truncate">{label}</span>
              {tier && (
                <Badge variant="secondary" className="text-xs shrink-0">
                  {tier}
                </Badge>
              )}
            </div>

            <Switch
              checked={enabled}
              disabled={isPending}
              aria-label={
                enabled
                  ? t("entitlements.disableLabel", { module: label })
                  : t("entitlements.enableLabel", { module: label })
              }
              onCheckedChange={(checked) => {
                handleToggle(key, checked, tier);
              }}
            />
          </li>
        );
      })}
    </ul>
  );
}
