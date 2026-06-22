import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { privacyQueries } from "@/features/privacy/api";
import { ConsentToggles } from "@/features/privacy/components/consent-toggles";
import { ExportDataFlow } from "@/features/privacy/components/export-data-flow";
import { EraseAccountDialog } from "@/features/privacy/components/erase-account-dialog";
import { Separator } from "@/components/ui/separator";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("privacy");
  return {
    title: t("metadata.title"),
  };
}

export default async function PrivacyPage() {
  const queryClient = getQueryClient();
  const t = await getTranslations("privacy");

  // Prefetch consent history without awaiting — streamed to the client island.
  void queryClient.prefetchQuery(privacyQueries.consents());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="max-w-lg space-y-8">
        {/* Page header */}
        <div>
          <h1 className="text-xl font-semibold tracking-tight">{t("header.title")}</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            {t("header.description")}
          </p>
        </div>

        {/* Consent toggles */}
        <section aria-labelledby="consents-heading" className="space-y-3">
          <div>
            <h2 id="consents-heading" className="text-sm font-medium">
              {t("consents.heading")}
            </h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              {t("consents.description")}
            </p>
          </div>
          <ConsentToggles />
        </section>

        <Separator />

        {/* Export */}
        <section aria-labelledby="export-heading" className="space-y-3">
          <div>
            <h2 id="export-heading" className="text-sm font-medium">
              {t("export.heading")}
            </h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              {t("export.description")}
            </p>
          </div>
          <ExportDataFlow />
        </section>

        <Separator />

        {/* Erasure */}
        <section aria-labelledby="erase-heading" className="space-y-3">
          <div>
            <h2 id="erase-heading" className="text-sm font-medium text-destructive">
              {t("erase.heading")}
            </h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              {t("erase.description")}
            </p>
          </div>
          <EraseAccountDialog />
        </section>
      </div>
    </HydrationBoundary>
  );
}
