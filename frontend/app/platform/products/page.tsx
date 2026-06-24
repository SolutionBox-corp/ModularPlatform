import { redirect } from "next/navigation";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { getQueryClient } from "@/lib/api/query-client";
import { getSession, isAuthenticated } from "@/lib/auth/session";
import { platformQueries } from "@/features/platform/api";
import { ProductsContent } from "@/features/platform/components/products-content";
import { ProblemDetails } from "@/components/app/problem-details";
import { ApiError } from "@/lib/api/types";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("platform");
  return { title: t("meta.productsTitle") };
}

const REQUIRED_PERMISSION = "billing.manage";

export default async function PlatformProductsPage() {
  const session = await getSession();
  if (!isAuthenticated(session)) {
    redirect("/login");
  }

  const t = await getTranslations("platform");

  if (!session.user!.permissions.includes(REQUIRED_PERMISSION)) {
    const forbidden = new ApiError({
      status: 403,
      errorCode: "auth.forbidden",
      detail: t("forbidden.area"),
    });
    return (
      <div className="max-w-sm w-full space-y-4">
        <ProblemDetails error={forbidden} />
      </div>
    );
  }

  const queryClient = getQueryClient();
  await queryClient.prefetchQuery(platformQueries.adminPackages());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <ProductsContent />
    </HydrationBoundary>
  );
}
