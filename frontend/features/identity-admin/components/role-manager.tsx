"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { PlusIcon, XIcon, Loader2Icon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { useAssignRole, useRevokeRole } from "@/features/identity-admin/hooks";

interface RoleManagerProps {
  /** Target user ID. The current roles are passed in as a server-side snapshot
   *  (the backend has no list-user endpoint, so the caller supplies them). */
  userId: string;
  /** Initial roles snapshot. Can be empty when looking up a new user. */
  currentRoles: string[];
  /** Whether role mutation is permitted for the logged-in admin. */
  canManageRoles: boolean;
}

/**
 * Assign / revoke roles on a specific user.
 * - Shows the current role list as removable badges.
 * - An inline text field lets the admin type a role name and assign it.
 * - Both actions are disabled when `canManageRoles` is false (UI mirrors the
 *   backend permission gate; the real gate is `identity.manage_roles`).
 */
export function RoleManager({
  userId,
  currentRoles,
  canManageRoles,
}: RoleManagerProps) {
  const t = useTranslations("identityAdmin");
  const [newRole, setNewRole] = useState("");
  const assign = useAssignRole();
  const revoke = useRevokeRole();

  const handleAssign = () => {
    const trimmed = newRole.trim();
    if (!trimmed) return;
    assign.mutate(
      { userId, role: trimmed },
      { onSuccess: () => setNewRole("") },
    );
  };

  const handleRevoke = (role: string) => {
    revoke.mutate({ userId, role });
  };

  const isBusy = assign.isPending || revoke.isPending;

  return (
    <div className="space-y-4">
      {/* Current roles */}
      <div>
        <p className="text-xs font-medium text-muted-foreground mb-2">
          {t("roleManager.currentRoles")}
        </p>
        {currentRoles.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("roleManager.noRoles")}</p>
        ) : (
          <div className="flex flex-wrap gap-2">
            {currentRoles.map((role) => (
              <Badge
                key={role}
                variant="secondary"
                className="flex items-center gap-1 pr-1"
              >
                {role}
                {canManageRoles && (
                  <button
                    type="button"
                    aria-label={t("roleManager.revokeAria", { role })}
                    className="ml-0.5 rounded hover:text-destructive transition-colors disabled:opacity-50"
                    onClick={() => handleRevoke(role)}
                    disabled={isBusy}
                  >
                    <XIcon className="h-3 w-3" />
                  </button>
                )}
              </Badge>
            ))}
          </div>
        )}
      </div>

      {/* Assign new role */}
      {canManageRoles && (
        <div className="space-y-1.5">
          <Label htmlFor="new-role" className="text-xs font-medium">
            {t("roleManager.assignLabel")}
          </Label>
          <div className="flex gap-2">
            <Input
              id="new-role"
              value={newRole}
              onChange={(e) => setNewRole(e.target.value)}
              placeholder={t("roleManager.assignPlaceholder")}
              className="h-8 text-sm"
              disabled={isBusy}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  handleAssign();
                }
              }}
            />
            <Button
              size="sm"
              variant="outline"
              onClick={handleAssign}
              disabled={isBusy || newRole.trim().length === 0}
              className="h-8 gap-1.5"
            >
              {assign.isPending ? (
                <Loader2Icon className="h-3.5 w-3.5 animate-spin" />
              ) : (
                <PlusIcon className="h-3.5 w-3.5" />
              )}
              {t("roleManager.assign")}
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            {t("roleManager.assignHint")}
          </p>
        </div>
      )}

      {!canManageRoles && (
        <p className="text-xs text-muted-foreground">
          {t.rich("roleManager.noPermission", {
            code: (chunks) => <span className="font-mono">{chunks}</span>,
          })}
        </p>
      )}
    </div>
  );
}
