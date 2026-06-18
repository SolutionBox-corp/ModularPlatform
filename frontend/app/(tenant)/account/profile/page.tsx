import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { accountQueries } from "@/features/account/api";
import { ProfileForm } from "@/features/account/components/profile-form";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Profile — ModularPlatform",
};

export default async function ProfilePage() {
  const queryClient = getQueryClient();

  // Prefetch without awaiting — streams profile data to the client island.
  void queryClient.prefetchQuery(accountQueries.profile());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="max-w-lg space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Profile</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Your account information.
          </p>
        </div>

        <ProfileForm />
      </div>
    </HydrationBoundary>
  );
}
