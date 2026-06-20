"use client";

import { useState } from "react";
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
import { AuditTrailTable } from "@/features/identity-admin/components/audit-trail-table";
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
  const [inputValue, setInputValue] = useState("");
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const [inputError, setInputError] = useState<string | null>(null);
  const [isLookingUp, setIsLookingUp] = useState(false);

  const handleLookup = () => {
    const trimmed = inputValue.trim();
    if (!UUID_RE.test(trimmed)) {
      setInputError("Enter a valid user UUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).");
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
          <CardTitle className="text-sm font-medium">User lookup</CardTitle>
          <CardDescription className="text-xs">
            Enter a user ID to manage roles and view the audit trail.
            {!hasActions && (
              <span className="block mt-1 text-destructive">
                You do not have any Identity admin permissions.
              </span>
            )}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex gap-2 items-end">
            <div className="flex-1 space-y-1">
              <Label htmlFor="user-id-input" className="text-xs font-medium">
                User ID
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
              Look up
            </Button>
          </div>
          <p className="mt-2 text-xs text-muted-foreground">
            No list-users endpoint exists on the backend — look up by ID only.
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
                  Role management
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
                    Identity audit trail
                  </CardTitle>
                  <CardDescription className="text-xs font-mono">
                    {selectedUserId}
                  </CardDescription>
                </CardHeader>
                <CardContent className="p-0">
                  <AuditTrailTable userId={selectedUserId} />
                </CardContent>
              </Card>
            </>
          )}

          {!canReadAudit && (
            <p className="text-xs text-muted-foreground">
              Audit trail hidden — requires the{" "}
              <span className="font-mono">audit.read</span> permission.
            </p>
          )}
        </>
      )}
    </div>
  );
}
