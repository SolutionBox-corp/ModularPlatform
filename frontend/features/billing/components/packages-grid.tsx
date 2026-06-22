"use client";

import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { PackageIcon } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/app/empty-state";
import { billingQueries } from "@/features/billing/api";
import { PackageCard } from "@/features/billing/components/package-card";

/**
 * Responsive grid of available credit packages.
 * Each card wires its own checkout mutation.
 */
export function PackagesGrid() {
  const t = useTranslations("billing");
  const { data, isLoading } = useQuery(billingQueries.packages());

  if (isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-40 rounded-xl" />
        ))}
      </div>
    );
  }

  if (!data || data.length === 0) {
    return (
      <EmptyState
        icon={PackageIcon}
        title={t("packages.emptyTitle")}
        description={t("packages.emptyDescription")}
      />
    );
  }

  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {data.map((pkg) => (
        <PackageCard key={pkg.id} pkg={pkg} />
      ))}
    </div>
  );
}
