import { redirect } from "next/navigation";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { getSession } from "@/lib/auth/session";
import { billingQueries } from "@/features/billing/api";
import { notificationQueries } from "@/features/notifications/api";
import { CreditBalanceCard, SubscriptionCard } from "@/features/billing/components/cards";
import { RecentNotifications } from "@/features/notifications/components/recent-notifications";
import Link from "next/link";
import { ArrowRightIcon } from "lucide-react";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Dashboard — ModularPlatform",
};

export default async function DashboardPage() {
  const session = await getSession();
  const queryClient = getQueryClient();
  const user = session.user;
  if (!user) redirect("/login");

  // Await the prefetches so the dehydrated cache is fully resolved → server and client
  // render the same first paint (no hydration mismatch, no skeleton flash). The queries
  // are independent, so run them concurrently.
  await Promise.all([
    queryClient.prefetchQuery(billingQueries.balance()),
    queryClient.prefetchQuery(billingQueries.subscription()),
    queryClient.prefetchQuery(notificationQueries.list({ page: 1, pageSize: 5 })),
  ]);

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            Welcome back{user.displayName ? `, ${user.displayName}` : ""}
          </h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Here&apos;s an overview of your workspace.
          </p>
        </div>

        {/* Stats grid */}
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <CreditBalanceCard />
          <SubscriptionCard />
        </div>

        {/* Recent notifications */}
        <div className="grid gap-4 lg:grid-cols-2">
          <RecentNotifications />

          {/* Quick actions */}
          <div className="space-y-2">
            <h2 className="text-sm font-medium text-muted-foreground">
              Quick actions
            </h2>
            <div className="rounded-lg border border-border divide-y divide-border">
              {[
                { label: "Upload a file", href: "/files" },
                { label: "Top up credits", href: "/billing/packages" },
                { label: "View audit trail", href: "/account/privacy" },
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
