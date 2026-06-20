import { redirect } from "next/navigation";
import { headers } from "next/headers";
import { getSession, isAuthenticated } from "@/lib/auth/session";
import { RealtimeProvider } from "@/lib/realtime/realtime-provider";
import { AppShell } from "@/components/app/app-shell";
import { ADMIN_TENANT, TENANT_HEADER } from "@/lib/tenant";
import { PLATFORM_NAV_ITEMS } from "@/features/entitlements/nav";
import { ProblemDetails } from "@/components/app/problem-details";
import { ApiError } from "@/lib/api/types";
import { getTranslations } from "next-intl/server";
import type { ReactNode } from "react";

export default async function PlatformLayout({ children }: { children: ReactNode }) {
  const session = await getSession();
  if (!isAuthenticated(session)) {
    redirect("/login");
  }

  const user = session.user!;
  const reqHeaders = await headers();
  const tenant = reqHeaders.get(TENANT_HEADER);

  // Require platform-admin access.
  // Gate on: the x-tenant header being "__admin__" (platform admin host) AND
  // the user holding the "platform.tenants.manage" permission.
  // Assumption documented: "admin" role or "platform.tenants.manage" permission.
  const isPlatformAdmin =
    tenant === ADMIN_TENANT &&
    (user.permissions.includes("platform.tenants.manage") ||
      user.roles.includes("admin"));

  if (!isPlatformAdmin) {
    // Render a 403 instead of redirecting to /login (the user IS authenticated —
    // they just don't have platform access).
    const t = await getTranslations("platform");
    const forbidden = new ApiError({
      status: 403,
      errorCode: "auth.forbidden",
      detail: t("forbidden.area"),
    });
    return (
      <div className="min-h-screen flex items-center justify-center p-6">
        <div className="max-w-sm w-full space-y-4">
          <ProblemDetails error={forbidden} />
        </div>
      </div>
    );
  }

  return (
    <RealtimeProvider>
      <AppShell user={user} navItems={PLATFORM_NAV_ITEMS}>
        {children}
      </AppShell>
    </RealtimeProvider>
  );
}
