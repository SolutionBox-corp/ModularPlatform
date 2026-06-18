import { HydrationBoundary, dehydrate } from "@tanstack/react-query";
import { getQueryClient } from "@/lib/api/query-client";
import { privacyQueries } from "@/features/privacy/api";
import { ConsentToggles } from "@/features/privacy/components/consent-toggles";
import { ExportDataFlow } from "@/features/privacy/components/export-data-flow";
import { EraseAccountDialog } from "@/features/privacy/components/erase-account-dialog";
import { Separator } from "@/components/ui/separator";
import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Privacy — ModularPlatform",
};

export default async function PrivacyPage() {
  const queryClient = getQueryClient();

  // Prefetch consent history without awaiting — streamed to the client island.
  void queryClient.prefetchQuery(privacyQueries.consents());

  return (
    <HydrationBoundary state={dehydrate(queryClient)}>
      <div className="max-w-lg space-y-8">
        {/* Page header */}
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Privacy &amp; Data</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Manage your consent preferences and exercise your data rights.
          </p>
        </div>

        {/* Consent toggles */}
        <section aria-labelledby="consents-heading" className="space-y-3">
          <div>
            <h2 id="consents-heading" className="text-sm font-medium">
              Consent preferences
            </h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              Control how we use your data. Changes take effect immediately.
            </p>
          </div>
          <ConsentToggles />
        </section>

        <Separator />

        {/* Export */}
        <section aria-labelledby="export-heading" className="space-y-3">
          <div>
            <h2 id="export-heading" className="text-sm font-medium">
              Export your data
            </h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              Download a machine-readable copy of all personal data we hold about you (GDPR Art.&nbsp;20).
            </p>
          </div>
          <ExportDataFlow />
        </section>

        <Separator />

        {/* Erasure */}
        <section aria-labelledby="erase-heading" className="space-y-3">
          <div>
            <h2 id="erase-heading" className="text-sm font-medium text-destructive">
              Delete account
            </h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              Permanently erase your account and all associated personal data. This cannot be
              reversed (GDPR Art.&nbsp;17).
            </p>
          </div>
          <EraseAccountDialog />
        </section>
      </div>
    </HydrationBoundary>
  );
}
