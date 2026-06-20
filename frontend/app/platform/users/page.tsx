import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import type { Metadata } from "next";
import { getQueryClient } from "@/lib/api/query-client";
import { getSession, isAuthenticated } from "@/lib/auth/session";
import { redirect } from "next/navigation";
import { platformQueries } from "@/features/platform/api";
import { PlatformUsersTable } from "@/features/platform/components/platform-users-table";
import { ProblemDetails } from "@/components/app/problem-details";
import { ApiError } from "@/lib/api/types";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("platform");
  return {
    title: t("meta.usersTitle"),
  };
}

const REQUIRED_PERMISSION = "platform.users.list";

/**
 * Platform-admin user list (cross-tenant, read-only).
 *
 * The /platform layout already enforces platform-admin access (admin host +
 * platform.tenants.manage). This page additionally requires the
 * "platform.users.list" permission — the same way the (tenant) admin page
 * gates per-feature permissions. The backend (.RequirePermission) is the real
 * enforcement gate; this is defence-in-depth + a friendly 403.
 */
export default async function PlatformUsersPage() {
  const session = await getSession();
  if (!isAuthenticated(session)) {
    redirect("/login");
  }

  const t = await getTranslations("platform");

  if (!session.user!.permissions.includes(REQUIRED_PERMISSION)) {
    const forbidden = new ApiError({
      status: 403,
      errorCode: "auth.forbidden",
      detail: t("forbidden.users"),
    });
    return (
      <div className="max-w-sm space-y-4">
        <ProblemDetails error={forbidden} />
      </div>
    );
  }

  const queryClient = getQueryClient();
  // Prefetch the first page (limit/offset defaults: 50 / 0).
  await queryClient.prefetchQuery(platformQueries.users());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            {t("users.heading")}
          </h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            {t("users.description")}
          </p>
        </div>

        <PlatformUsersTable />
      </div>
    </HydrationBoundary>
  );
}
