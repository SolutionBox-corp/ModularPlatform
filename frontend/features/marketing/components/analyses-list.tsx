"use client";

import { useLocale, useTranslations } from "next-intl";
import { SparklesIcon } from "lucide-react";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/app/empty-state";
import { useAnalyses } from "@/features/marketing/hooks";

/** List of AI marketing analyses (summary + source + date), newest first. */
export function AnalysesList() {
  const t = useTranslations("marketing");
  const locale = useLocale();
  const { data, isLoading } = useAnalyses();

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-24 w-full" />
      </div>
    );
  }

  if (!data || data.items.length === 0) {
    return (
      <EmptyState
        icon={SparklesIcon}
        title={t("analyses.emptyTitle")}
        description={t("analyses.emptyDescription")}
      />
    );
  }

  return (
    <div className="space-y-3">
      {data.items.map((analysis) => (
        <Card key={analysis.id}>
          <CardHeader className="pb-2 flex flex-row items-start justify-between gap-2">
            <CardTitle className="flex items-center gap-2 text-sm font-medium">
              <SparklesIcon className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
              <Badge variant="secondary" className="text-xs font-normal uppercase">
                {analysis.source}
              </Badge>
            </CardTitle>
            <span className="text-xs text-muted-foreground" suppressHydrationWarning>
              {new Date(analysis.analyzedAt).toLocaleDateString(locale, {
                year: "numeric",
                month: "short",
                day: "numeric",
              })}
            </span>
          </CardHeader>
          <CardContent>
            <p className="whitespace-pre-wrap break-words text-sm text-foreground">
              {analysis.summary}
            </p>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
