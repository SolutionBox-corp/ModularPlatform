"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { SearchIcon, Loader2Icon } from "lucide-react";
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
import { Separator } from "@/components/ui/separator";
import { UserAuditTrailTable } from "@/features/identity-admin/components/audit-trail-table";
import { RoleManager } from "@/features/identity-admin/components/role-manager";

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface IdentityAdminPanelProps {
  /** Whether the logged-in admin has identity.manage_roles permission. */
  canManageRoles: boolean;
  /** Whether the logged-in admin has audit.read permission. */
  canReadAudit: boolean;
}

/**
 * The main Identity Admin client island.
 *
 * The backend has no list-users endpoint, so this panel lets an admin look up
 * a user by their ID (UUID). After a valid ID is entered:
 *  - RoleManager shows and lets the admin assign / revoke roles.
 *  - AuditTrailTable shows the user's Identity audit trail (gated by audit.read).
 *
 * Gap: there is no GET /identity/admin/users/{id} endpoint that returns the
 * user's current roles/profile. RoleManager therefore starts with an empty
 * roles list — the admin can still assign or revoke (the backend is idempotent).
 * If a list-users or get-user admin endpoint is added in the future, wire it here
 * via `identityAdminQueries.user(userId)` and pre-populate `currentRoles`.
 */
export function IdentityAdminPanel({
  canManageRoles,
  canReadAudit,
}: IdentityAdminPanelProps) {
  const t = useTranslations("identityAdmin");
  const [inputValue, setInputValue] = useState("");
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const [inputError, setInputError] = useState<string | null>(null);
  const [isLookingUp, setIsLookingUp] = useState(false);

  const handleLookup = () => {
    const trimmed = inputValue.trim();
    if (!UUID_RE.test(trimmed)) {
      setInputError(t("lookup.invalidId"));
      return;
    }
    setInputError(null);
    setIsLookingUp(true);
    // No network call needed to "look up" — just set the ID so the child queries
    // fire. isLookingUp is reset immediately; the child components show their own
    // loading skeletons via useQuery.
    setSelectedUserId(trimmed);
    setIsLookingUp(false);
  };

  const hasActions = canManageRoles || canReadAudit;

  return (
    <div className="space-y-6">
      {/* User lookup */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-sm font-medium">{t("lookup.title")}</CardTitle>
          <CardDescription className="text-xs">
            {t("lookup.description")}
            {!hasActions && (
              <span className="block mt-1 text-destructive">
                {t("lookup.noPermissions")}
              </span>
            )}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex gap-2 items-end">
            <div className="flex-1 space-y-1">
              <Label htmlFor="user-id-input" className="text-xs font-medium">
                {t("lookup.userIdLabel")}
              </Label>
              <Input
                id="user-id-input"
                value={inputValue}
                onChange={(e) => {
                  setInputValue(e.target.value);
                  if (inputError) setInputError(null);
                }}
                placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                className="h-8 text-sm font-mono"
                aria-invalid={!!inputError}
                aria-describedby={inputError ? "user-id-error" : undefined}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault();
                    handleLookup();
                  }
                }}
              />
              {inputError && (
                <p id="user-id-error" className="text-xs text-destructive">{inputError}</p>
              )}
            </div>
            <Button
              size="sm"
              variant="outline"
              onClick={handleLookup}
              disabled={isLookingUp || inputValue.trim().length === 0}
              className="h-8 gap-1.5 shrink-0"
            >
              {isLookingUp ? (
                <Loader2Icon className="h-3.5 w-3.5 animate-spin" />
              ) : (
                <SearchIcon className="h-3.5 w-3.5" />
              )}
              {t("lookup.lookUp")}
            </Button>
          </div>
          <p className="mt-2 text-xs text-muted-foreground">
            {t("lookup.noListHint")}
          </p>
        </CardContent>
      </Card>

      {/* Results — shown once a valid ID is selected */}
      {selectedUserId && (
        <>
          {/* Role management */}
          {canManageRoles && (
            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="text-sm font-medium">
                  {t("roleManagement.title")}
                </CardTitle>
                <CardDescription className="text-xs font-mono">
                  {selectedUserId}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <RoleManager
                  userId={selectedUserId}
                  currentRoles={[]}
                  canManageRoles={canManageRoles}
                />
              </CardContent>
            </Card>
          )}

          {/* Audit trail */}
          {canReadAudit && (
            <>
              <Separator />
              <Card>
                <CardHeader className="pb-3">
                  <CardTitle className="text-sm font-medium">
                    {t("audit.title")}
                  </CardTitle>
                  <CardDescription className="text-xs font-mono">
                    {selectedUserId}
                  </CardDescription>
                </CardHeader>
                <CardContent className="p-0">
                  <UserAuditTrailTable userId={selectedUserId} />
                </CardContent>
              </Card>
            </>
          )}

          {!canReadAudit && (
            <p className="text-xs text-muted-foreground">
              {t.rich("audit.hiddenHint", {
                code: (chunks) => <span className="font-mono">{chunks}</span>,
              })}
            </p>
          )}
        </>
      )}
    </div>
  );
}
