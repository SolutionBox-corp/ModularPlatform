import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { getQueryClient } from "@/lib/api/query-client";
import { getSession, isAuthenticated } from "@/lib/auth/session";
import { platformQueries } from "@/features/platform/api";
import { PlatformAuditContent } from "@/features/platform/components/platform-audit-content";
import { ProblemDetails } from "@/components/app/problem-details";
import { ApiError } from "@/lib/api/types";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("platform");
  return {
    title: t("meta.auditTitle"),
  };
}

const REQUIRED_PERMISSION = "audit.read";

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface PageProps {
  searchParams: Promise<{ userId?: string | string[] }>;
}

/**
 * Platform-admin audit viewer (cross-tenant, read-only).
 *
 * The /platform layout enforces platform-admin access; this page additionally
 * requires "audit.read" (mirroring the (tenant) admin page's per-feature gating).
 * Reads ?userId= from the URL; when present and valid, prefetches the trail via
 * the cross-tenant `platformQueries.userAudit` and renders it through the shared
 * `AuditTrailTable`. Otherwise it shows a UUID lookup field.
 */
export default async function PlatformAuditPage({ searchParams }: PageProps) {
  const session = await getSession();
  if (!isAuthenticated(session)) {
    redirect("/login");
  }

  const t = await getTranslations("platform");

  if (!session.user!.permissions.includes(REQUIRED_PERMISSION)) {
    const forbidden = new ApiError({
      status: 403,
      errorCode: "auth.forbidden",
      detail: t("forbidden.audit"),
    });
    return (
      <div className="max-w-sm space-y-4">
        <ProblemDetails error={forbidden} />
      </div>
    );
  }

  const { userId: rawUserId } = await searchParams;
  const userIdParam = Array.isArray(rawUserId) ? rawUserId[0] : rawUserId;
  const userId =
    userIdParam && UUID_RE.test(userIdParam.trim())
      ? userIdParam.trim()
      : undefined;

  const queryClient = getQueryClient();
  if (userId) {
    await queryClient.prefetchQuery(platformQueries.userAudit(userId));
  }

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6 max-w-3xl">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            {t("audit.heading")}
          </h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            {t.rich("audit.description", {
              mono: (chunks) => <span className="font-mono">{chunks}</span>,
            })}
          </p>
        </div>

        <PlatformAuditContent userId={userId} />
      </div>
    </HydrationBoundary>
  );
}
