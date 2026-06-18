import { redirect } from "next/navigation";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import type { Metadata } from "next";
import { getQueryClient } from "@/lib/api/query-client";
import { getSession, isAuthenticated } from "@/lib/auth/session";
import { IdentityAdminPanel } from "@/features/identity-admin/components/identity-admin-panel";

export const metadata: Metadata = {
  title: "Identity Admin — ModularPlatform",
};

/**
 * Permission-gated Identity admin page.
 *
 * The (tenant) layout already redirects unauthenticated users to /login.
 * Here we additionally require at least one Identity admin permission:
 *   - identity.manage_roles  → role assign / revoke
 *   - audit.read             → user audit trail
 *
 * The backend is the real enforcement gate (.RequirePermission). The server
 * component reads the session snapshot (set at login/refresh) to decide what
 * UI sections to render and to redirect users who have neither permission.
 *
 * No data is prefetched server-side here: the IdentityAdminPanel is look-up
 * driven (the admin enters a user ID first), so there is nothing to pre-warm.
 */
export default async function IdentityAdminPage() {
  const session = await getSession();

  // The layout guards auth; this is a defence-in-depth check.
  if (!isAuthenticated(session)) {
    redirect("/login");
  }

  const permissions = session.user!.permissions;
  const canManageRoles = permissions.includes("identity.manage_roles");
  const canReadAudit = permissions.includes("audit.read");

  // If the user holds neither permission, redirect to home rather than showing
  // an empty page. The real gate is the backend; this just avoids an unhelpful
  // blank admin page.
  if (!canManageRoles && !canReadAudit) {
    redirect("/");
  }

  const queryClient = getQueryClient();

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6 max-w-3xl">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            Identity admin
          </h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Manage user roles and inspect the Identity audit trail.
          </p>
        </div>

        <IdentityAdminPanel
          canManageRoles={canManageRoles}
          canReadAudit={canReadAudit}
        />
      </div>
    </HydrationBoundary>
  );
}
