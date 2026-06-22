import { redirect } from "next/navigation";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { getSession } from "@/lib/auth/session";
import { billingQueries } from "@/features/billing/api";
import { notificationQueries } from "@/features/notifications/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { CreditBalanceCard, SubscriptionCard } from "@/features/billing/components/cards";
import { RecentNotifications } from "@/features/notifications/components/recent-notifications";
import Link from "next/link";
import { ArrowRightIcon } from "lucide-react";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("shell");
  return { title: t("dashboard.metaTitle") };
}

export default async function DashboardPage() {
  const t = await getTranslations("shell");
  const session = await getSession();
  const queryClient = getQueryClient();
  const user = session.user;
  if (!user) redirect("/login");

  // Entitlements: the layout already awaited this; fetchQuery reuses the cached result.
  const ent = await queryClient.fetchQuery(entitlementQueries.me());

  const billingEnabled = isModuleEnabled(ent, "billing");
  const notificationsEnabled = isModuleEnabled(ent, "notifications");

  // Prefetch only the modules that are entitled — avoids 404 errors on the API
  // for disabled modules. Queries are independent so run them concurrently.
  const prefetches: Promise<unknown>[] = [];
  if (billingEnabled) {
    prefetches.push(
      queryClient.prefetchQuery(billingQueries.balance()),
      queryClient.prefetchQuery(billingQueries.subscription()),
    );
  }
  if (notificationsEnabled) {
    prefetches.push(
      queryClient.prefetchQuery(notificationQueries.list({ page: 1, pageSize: 5 })),
    );
  }
  await Promise.all(prefetches);

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            {user.displayName
              ? t("dashboard.welcomeNamed", { name: user.displayName })
              : t("dashboard.welcome")}
          </h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            {t("dashboard.subtitle")}
          </p>
        </div>

        {/* Stats grid — only shown when the billing module is enabled */}
        {billingEnabled && (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <CreditBalanceCard />
            <SubscriptionCard />
          </div>
        )}

        <div className="grid gap-4 lg:grid-cols-2">
          {/* Recent notifications — only shown when the notifications module is enabled */}
          {notificationsEnabled && <RecentNotifications />}

          {/* Quick actions — always shown */}
          <div className="space-y-2">
            <h2 className="text-sm font-medium text-muted-foreground">
              {t("dashboard.quickActions")}
            </h2>
            <div className="rounded-lg border border-border divide-y divide-border">
              {[
                { label: t("dashboard.actions.uploadFile"), href: "/files" },
                { label: t("dashboard.actions.topUpCredits"), href: "/billing/packages" },
                { label: t("dashboard.actions.viewAuditTrail"), href: "/account/privacy" },
              ].map(({ label, href }) => (
                <Link
                  key={href}
                  href={href}
                  className="flex items-center justify-between px-4 py-3 text-sm hover:bg-muted/50 transition-colors"
                >
                  {label}
                  <ArrowRightIcon className="h-3.5 w-3.5 text-muted-foreground" />
                </Link>
              ))}
            </div>
          </div>
        </div>
      </div>
    </HydrationBoundary>
  );
}
