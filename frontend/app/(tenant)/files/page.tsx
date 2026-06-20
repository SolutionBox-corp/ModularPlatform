import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { fileQueries } from "@/features/files/api";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { FileDropzone } from "@/features/files/components/file-dropzone";
import { FileTable } from "@/features/files/components/file-table";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("files");
  return { title: t("metaTitle") };
}

export default async function FilesPage() {
  const t = await getTranslations("files");
  const queryClient = getQueryClient();

  // Guard: the layout already awaited this query; fetchQuery reuses the cached result.
  const ent = await queryClient.fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "files")) notFound();

  // Prefetch page 1 — streamed into HydrationBoundary, no loading flash.
  void queryClient.prefetchQuery(fileQueries.list(1, 20));

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">{t("page.heading")}</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            {t("page.description")}
          </p>
        </div>

        <FileDropzone />

        <div className="space-y-2">
          <h2 className="text-sm font-medium text-muted-foreground">{t("page.yourFiles")}</h2>
          <FileTable />
        </div>
      </div>
    </HydrationBoundary>
  );
}
