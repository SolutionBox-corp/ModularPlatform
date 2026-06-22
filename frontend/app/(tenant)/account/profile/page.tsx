import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getTranslations } from "next-intl/server";
import { getQueryClient } from "@/lib/api/query-client";
import { accountQueries } from "@/features/account/api";
import { ProfileForm } from "@/features/account/components/profile-form";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("account");
  return {
    title: t("page.metaTitle"),
  };
}

export default async function ProfilePage() {
  const queryClient = getQueryClient();
  const t = await getTranslations("account");

  // Prefetch without awaiting — streams profile data to the client island.
  void queryClient.prefetchQuery(accountQueries.profile());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="max-w-lg space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            {t("page.heading")}
          </h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            {t("page.subtitle")}
          </p>
        </div>

        <ProfileForm />
      </div>
    </HydrationBoundary>
  );
}
