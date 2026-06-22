import { redirect } from "next/navigation";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { getSession, isAuthenticated } from "@/lib/auth/session";
import { RealtimeProvider } from "@/lib/realtime/realtime-provider";
import { AppShell } from "@/components/app/app-shell";
import { entitlementQueries } from "@/features/entitlements/api";
import type { ReactNode } from "react";

export default async function TenantLayout({ children }: { children: ReactNode }) {
  const session = await getSession();
  if (!isAuthenticated(session)) {
    redirect("/login");
  }

  const user = session.user!;
  const queryClient = getQueryClient();

  // AWAIT the entitlements prefetch: it is the nav source of truth and must be resolved
  // before SSR so the server renders the final nav (no hydration mismatch / flash).
  await queryClient.prefetchQuery(entitlementQueries.me());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <RealtimeProvider>
        <AppShell user={user}>{children}</AppShell>
      </RealtimeProvider>
    </HydrationBoundary>
  );
}
