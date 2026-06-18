"use client";

import { useQuery } from "@tanstack/react-query";
import { InfoIcon } from "lucide-react";
import { accountQueries } from "@/features/account/api";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Alert } from "@/components/ui/alert";

/**
 * Displays the authenticated user's profile fields.
 *
 * All fields are READ-ONLY because the backend (Identity module) has no
 * profile update endpoint as of this build. When PATCH /v1/identity/users/me
 * is added, replace the plain inputs with a react-hook-form + zod form using
 * the profileSchema and a useMutation calling apiFetch with method:"PATCH".
 */
export function ProfileForm() {
  const { data, isLoading } = useQuery(accountQueries.profile());

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base font-semibold">Profile</CardTitle>
        <CardDescription className="text-sm">
          Your account details.
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-5">
        {/* Read-only notice */}
        <Alert className="flex items-start gap-2 py-3 px-3 text-sm text-muted-foreground">
          <InfoIcon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
          <span>
            Profile editing is not yet available. Contact support to update your
            display name or locale preference.
          </span>
        </Alert>

        <div className="space-y-1.5">
          <Label htmlFor="profile-email">Email</Label>
          {isLoading ? (
            <Skeleton className="h-8 w-full" />
          ) : (
            <Input
              id="profile-email"
              type="email"
              value={data?.email ?? ""}
              readOnly
              disabled
              aria-label="Email address"
            />
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="profile-display-name">Display name</Label>
          {isLoading ? (
            <Skeleton className="h-8 w-full" />
          ) : (
            <Input
              id="profile-display-name"
              type="text"
              value={data?.displayName ?? ""}
              readOnly
              disabled
              placeholder="Not set"
              aria-label="Display name"
            />
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="profile-locale">Locale</Label>
          {isLoading ? (
            <Skeleton className="h-8 w-2/12" />
          ) : (
            <Input
              id="profile-locale"
              type="text"
              value={data?.locale ?? ""}
              readOnly
              disabled
              aria-label="Locale"
            />
          )}
          <p className="text-xs text-muted-foreground">
            Change your language using the locale toggle in the top bar.
          </p>
        </div>
      </CardContent>
    </Card>
  );
}
