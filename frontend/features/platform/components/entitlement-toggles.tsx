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

/**
 * Default-entitled modules seeded on every fresh tenant (mirrors TenantProvisioning.DefaultEntitledModules).
 * Used as a static fallback when no GET-by-tenant-id endpoint is available.
 */
const DEFAULT_MODULE_KEYS = [
  "billing",
  "notifications",
  "files",
  "operations",
  "gdpr",
];

interface EntitlementTogglesProps {
  tenantId: string;
  modules: PlatformBillingModuleView[] | undefined;
  isLoading?: boolean;
  /**
   * When true and modules is undefined, renders the default module set with
   * unknown enabled state (displayed as off). Admin can toggle to set them
   * explicitly. Switch state is optimistic (reflects last action, not DB value).
   */
  fallbackToDefaults?: boolean;
}

interface ModuleToggleState {
  key: string;
  enabled: boolean;
  tier: string | null;
}

/**
 * Renders a Switch per module key. Toggles call PUT /tenant/admin/tenants/{id}/entitlements/{key}.
 * Maintains optimistic local state so the switch does not snap back.
 */
export function EntitlementToggles({
  tenantId,
  modules,
  isLoading = false,
  fallbackToDefaults = false,
}: EntitlementTogglesProps) {
  const t = useTranslations("platform");
  const mutation = useSetEntitlement();

  const moduleLabel = (key: string): string =>
    (KNOWN_MODULE_KEYS as readonly string[]).includes(key)
      ? t(`entitlements.modules.${key}`)
      : key;

  // Build initial state from props or fallback defaults.
  const initialModules: ModuleToggleState[] =
    modules?.map((m) => ({ key: m.key, enabled: m.enabled, tier: m.tier })) ??
    (fallbackToDefaults
      ? DEFAULT_MODULE_KEYS.map((key) => ({ key, enabled: false, tier: null }))
      : []);

  const [localState, setLocalState] = useState<ModuleToggleState[]>(initialModules);

  if (isLoading) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="flex items-center justify-between py-2">
            <Skeleton className="h-4 w-28" />
            <Skeleton className="h-5 w-8 rounded-full" />
          </div>
        ))}
      </div>
    );
  }

  if (localState.length === 0) {
    return (
      <p className="text-sm text-muted-foreground py-4">
        {t("entitlements.empty")}
      </p>
    );
  }

  function handleToggle(key: string, checked: boolean, tier: string | null) {
    // Optimistic update.
    setLocalState((prev) =>
      prev.map((m) => (m.key === key ? { ...m, enabled: checked } : m)),
    );

    void mutation.mutateAsync({ tenantId, moduleKey: key, enabled: checked, tier }).catch(() => {
      // Revert on error.
      setLocalState((prev) =>
        prev.map((m) => (m.key === key ? { ...m, enabled: !checked } : m)),
      );
    });
  }

  return (
    <ul className="space-y-1" role="list">
      {localState.map((mod) => {
        const label = moduleLabel(mod.key);
        const isPending =
          mutation.isPending && mutation.variables?.moduleKey === mod.key;

        return (
          <li
            key={mod.key}
            className="flex items-center justify-between rounded-lg px-3 py-2.5 hover:bg-muted/40 transition-colors"
          >
            <div className="flex items-center gap-2.5 min-w-0">
              <span className="text-sm font-medium truncate">{label}</span>
              {mod.tier && (
                <Badge variant="secondary" className="text-xs shrink-0">
                  {mod.tier}
                </Badge>
              )}
            </div>

            <Switch
              checked={mod.enabled}
              disabled={isPending}
              aria-label={
                mod.enabled
                  ? t("entitlements.disableLabel", { module: label })
                  : t("entitlements.enableLabel", { module: label })
              }
              onCheckedChange={(checked) => {
                handleToggle(mod.key, checked, mod.tier);
              }}
            />
          </li>
        );
      })}
    </ul>
  );
}
