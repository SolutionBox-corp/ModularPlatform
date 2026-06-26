"use client";

import { useQuery } from "@tanstack/react-query";
import { ReceiptIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { platformQueries } from "@/features/platform/api";

export function PlatformBillingCard() {
  const t = useTranslations("platform");
  const { data, isLoading } = useQuery(platformQueries.billingStatus());

  return (
    <Card>
      <CardHeader className="pb-2 flex flex-row items-start gap-2">
        <ReceiptIcon className="h-4 w-4 mt-0.5 text-muted-foreground shrink-0" />
        <div className="flex-1 min-w-0">
          <CardTitle className="text-sm font-medium">
            {t("billingCard.title")}
          </CardTitle>
          <CardDescription className="text-xs">
            {t("billingCard.description")}
          </CardDescription>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {isLoading ? (
          <Skeleton className="h-6 w-24" />
        ) : data ? (
          <>
            <div className="flex items-center gap-2">
              <span className="text-base font-semibold capitalize">
                {data.plan}
              </span>
              <Badge variant={data.checkoutReady ? "default" : "secondary"}>
                {data.checkoutReady
                  ? t("billingCard.on")
                  : t("billingCard.off")}
              </Badge>
            </div>
            <div className="text-xs text-muted-foreground">
              {data.provider && (
                <span className="capitalize">{data.provider}</span>
              )}
              {data.actionRequired && (
                <span className="ml-2 font-mono">{data.actionRequired}</span>
              )}
            </div>
            <ul className="space-y-1" role="list">
              {data.modules.map((mod) => (
                <li
                  key={mod.key}
                  className="flex items-center justify-between text-xs"
                >
                  <span className="text-muted-foreground capitalize">
                    {mod.key}
                  </span>
                  <div className="flex items-center gap-1.5">
                    {mod.tier && (
                      <Badge variant="secondary" className="py-0">
                        {mod.tier}
                      </Badge>
                    )}
                    <span
                      className={
                        mod.enabled
                          ? "text-success"
                          : "text-muted-foreground"
                      }
                    >
                      {mod.enabled ? t("billingCard.on") : t("billingCard.off")}
                    </span>
                  </div>
                </li>
              ))}
            </ul>
          </>
        ) : (
          <p className="text-sm text-muted-foreground">
            {t("billingCard.empty")}
          </p>
        )}
      </CardContent>
    </Card>
  );
}
