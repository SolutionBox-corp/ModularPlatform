"use client";

import { useLocale, useTranslations } from "next-intl";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { RefreshCwIcon, CheckCircle2Icon, XCircleIcon, ClockIcon } from "lucide-react";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { usePulls, useTriggerPull } from "@/features/marketing/hooks";
import {
  buildTriggerPullSchema,
  type TriggerPullInput,
} from "@/features/marketing/schema";
import type { PullStatusResponse } from "@/features/marketing/api";
import { useHasHydrated } from "@/hooks/use-has-hydrated";

function pullStatusVariant(
  status: string,
): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case "Completed":
      return "default";
    case "Failed":
      return "destructive";
    case "Running":
    case "Pending":
      return "secondary";
    default:
      return "outline";
  }
}

function PullStatusIcon({ status }: { status: string }) {
  switch (status) {
    case "Completed":
      return <CheckCircle2Icon className="h-4 w-4 shrink-0 text-success" aria-hidden="true" />;
    case "Failed":
      return <XCircleIcon className="h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />;
    default:
      return <ClockIcon className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />;
  }
}

/**
 * Trigger a data pull (ga4 | gsc) and watch its status. The realtime
 * "marketing.pull_completed" event invalidates the marketing root, so the list
 * below refreshes automatically when a pull finishes — no client polling.
 */
export function PullPanel() {
  const t = useTranslations("marketing");
  const locale = useLocale();
  const { data, isLoading } = usePulls();
  const trigger = useTriggerPull();
  const hasHydrated = useHasHydrated();

  const {
    control,
    handleSubmit,
    register,
    setValue,
    formState: { errors },
  } = useForm<TriggerPullInput>({
    resolver: zodResolver(buildTriggerPullSchema(t)),
    defaultValues: { source: "ga4", startDate: "", endDate: "" },
  });

  const source = useWatch({ control, name: "source" });
  const showSkeleton = !hasHydrated || (isLoading && data === undefined);

  const onSubmit = (values: TriggerPullInput) => {
    trigger.mutate({
      source: values.source,
      startDate: values.startDate || undefined,
      endDate: values.endDate || undefined,
    });
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{t("pulls.heading")}</CardTitle>
        <CardDescription>{t("pulls.description")}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <form
          onSubmit={handleSubmit(onSubmit)}
          noValidate
          className="grid gap-2 sm:grid-cols-[10rem_1fr_1fr_auto] sm:items-end"
        >
          <div className="space-y-1">
            <Select
              value={source}
              onValueChange={(v) =>
                setValue("source", v as TriggerPullInput["source"], {
                  shouldValidate: true,
                })
              }
            >
              <SelectTrigger className="w-40" aria-label={t("pulls.sourceLabel")}>
                <SelectValue placeholder={t("pulls.sourcePlaceholder")} />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="ga4">{t("pulls.sourceGa4")}</SelectItem>
                <SelectItem value="gsc">{t("pulls.sourceGsc")}</SelectItem>
              </SelectContent>
            </Select>
            {errors.source && (
              <p className="text-xs text-destructive">{errors.source.message}</p>
            )}
          </div>
          <div className="space-y-1">
            <Label htmlFor="pull-start-date">
              {t("pulls.startDate")}
            </Label>
            <Input
              id="pull-start-date"
              type="date"
              aria-invalid={!!errors.startDate}
              {...register("startDate")}
            />
            {errors.startDate && (
              <p className="text-xs text-destructive">{errors.startDate.message}</p>
            )}
          </div>
          <div className="space-y-1">
            <Label htmlFor="pull-end-date">
              {t("pulls.endDate")}
            </Label>
            <Input
              id="pull-end-date"
              type="date"
              aria-invalid={!!errors.endDate}
              {...register("endDate")}
            />
            {errors.endDate && (
              <p className="text-xs text-destructive">{errors.endDate.message}</p>
            )}
          </div>
          <Button type="submit" disabled={trigger.isPending}>
            <RefreshCwIcon className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
            {trigger.isPending ? t("pulls.triggering") : t("pulls.trigger")}
          </Button>
        </form>

        <div className="space-y-2">
          <h3 className="text-sm font-medium text-muted-foreground">
            {t("pulls.recent")}
          </h3>
          {showSkeleton ? (
            <div className="space-y-2">
              <Skeleton className="h-9 w-full" />
              <Skeleton className="h-9 w-full" />
            </div>
          ) : data && data.items.length > 0 ? (
            <ul className="space-y-1.5">
              {data.items.map((pull: PullStatusResponse) => (
                <li
                  key={pull.id}
                  className="flex items-center justify-between gap-3 rounded-lg border border-border px-3 py-2"
                >
                  <div className="flex items-center gap-2 min-w-0">
                    <PullStatusIcon status={pull.status} />
                    <span className="text-sm font-medium uppercase">{pull.source}</span>
                    {pull.errorCode && (
                      <span className="truncate text-xs text-destructive">
                        {pull.errorCode}
                      </span>
                    )}
                  </div>
                  <div className="flex items-center gap-2 shrink-0">
                    {pull.completedAt && (
                      <span className="text-xs text-muted-foreground" suppressHydrationWarning>
                        {new Date(pull.completedAt).toLocaleString(locale, {
                          month: "short",
                          day: "numeric",
                          hour: "2-digit",
                          minute: "2-digit",
                        })}
                      </span>
                    )}
                    <Badge variant={pullStatusVariant(pull.status)} className="text-xs">
                      {pull.status}
                    </Badge>
                  </div>
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-muted-foreground">{t("pulls.empty")}</p>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
