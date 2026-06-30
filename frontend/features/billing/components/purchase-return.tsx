"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { useLocale, useTranslations } from "next-intl";
import {
  AlertCircleIcon,
  CheckCircle2Icon,
  ClockIcon,
  LoaderCircleIcon,
  XCircleIcon,
} from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { billingQueries } from "@/features/billing/api";

const TERMINAL_PURCHASE_STATUSES = new Set(["Completed", "Abandoned"]);

function usePurchaseIdFromReturn(): string | null {
  const params = useSearchParams();
  const fromQuery = params.get("purchaseId") ?? params.get("purchase_id");
  const [stored] = useState<string | null>(() => {
    if (typeof window === "undefined") return null;
    try {
      return window.sessionStorage.getItem("billing:lastPurchaseId");
    } catch {
      return null;
    }
  });

  return fromQuery ?? stored;
}

function clearStoredPurchaseId() {
  try {
    sessionStorage.removeItem("billing:lastPurchaseId");
  } catch {
    // Ignore browsers that block sessionStorage.
  }
}

function StatusGlyph({ status }: { status: string | undefined }) {
  if (status === "Completed") {
    return <CheckCircle2Icon className="h-5 w-5" aria-hidden="true" />;
  }
  if (status === "Abandoned") {
    return <XCircleIcon className="h-5 w-5" aria-hidden="true" />;
  }
  if (status === "Pending") {
    return (
      <LoaderCircleIcon className="h-5 w-5 animate-spin" aria-hidden="true" />
    );
  }
  return <ClockIcon className="h-5 w-5" aria-hidden="true" />;
}

export function PurchaseSuccessContent() {
  const t = useTranslations("billing");
  const locale = useLocale();
  const purchaseId = usePurchaseIdFromReturn();
  const query = useQuery({
    ...billingQueries.purchase(purchaseId ?? ""),
    enabled: Boolean(purchaseId),
    refetchInterval: (result) => {
      const status = result.state.data?.status;
      return status && TERMINAL_PURCHASE_STATUSES.has(status) ? false : 2_000;
    },
  });

  useEffect(() => {
    if (query.data?.status && TERMINAL_PURCHASE_STATUSES.has(query.data.status)) {
      clearStoredPurchaseId();
    }
  }, [query.data?.status]);

  const resolvedAt = query.data?.resolvedAt
    ? new Date(query.data.resolvedAt).toLocaleString(locale, {
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    })
    : null;

  if (!purchaseId) {
    return (
      <Alert>
        <AlertCircleIcon aria-hidden="true" />
        <AlertTitle>{t("purchaseReturn.missingTitle")}</AlertTitle>
        <AlertDescription>{t("purchaseReturn.missingDescription")}</AlertDescription>
      </Alert>
    );
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start gap-3">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted text-muted-foreground">
            <StatusGlyph status={query.data?.status} />
          </span>
          <div className="min-w-0 flex-1">
            <CardTitle>{t("purchaseReturn.title")}</CardTitle>
            <CardDescription>{t("purchaseReturn.description")}</CardDescription>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {query.isLoading ? (
          <p className="text-sm text-muted-foreground">
            {t("purchaseReturn.loading")}
          </p>
        ) : query.isError ? (
          <Alert variant="destructive">
            <AlertCircleIcon aria-hidden="true" />
            <AlertTitle>{t("purchaseReturn.errorTitle")}</AlertTitle>
            <AlertDescription>{t("purchaseReturn.errorDescription")}</AlertDescription>
          </Alert>
        ) : query.data ? (
          <div className="space-y-3">
            <div className="flex items-center gap-2">
              <Badge
                variant={query.data.status === "Completed" ? "default" : "secondary"}
              >
                {query.data.status}
              </Badge>
              <span className="text-sm text-muted-foreground">
                {query.data.status === "Completed"
                  ? t("purchaseReturn.completed")
                  : query.data.status === "Abandoned"
                    ? t("purchaseReturn.abandoned")
                    : t("purchaseReturn.pending")}
              </span>
            </div>
            <dl className="grid gap-3 text-sm sm:grid-cols-2">
              <div>
                <dt className="text-muted-foreground">
                  {t("purchaseReturn.credits")}
                </dt>
                <dd className="font-medium tabular-nums">
                  {query.data.creditAmount.toLocaleString(locale)}
                </dd>
              </div>
              <div>
                <dt className="text-muted-foreground">
                  {t("purchaseReturn.resolvedAt")}
                </dt>
                <dd className="font-medium">
                  {resolvedAt ?? t("purchaseReturn.notResolved")}
                </dd>
              </div>
            </dl>
          </div>
        ) : null}

        <div className="flex flex-wrap gap-2">
          <Button render={<Link href="/billing" />}>
            {t("purchaseReturn.backToBilling")}
          </Button>
          <Button render={<Link href="/operations" />} variant="outline">
            {t("purchaseReturn.viewOperations")}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

export function PurchaseCancelContent() {
  const t = useTranslations("billing");

  useEffect(() => {
    clearStoredPurchaseId();
  }, []);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("purchaseCancel.title")}</CardTitle>
        <CardDescription>{t("purchaseCancel.description")}</CardDescription>
      </CardHeader>
      <CardContent>
        <Button render={<Link href="/billing" />}>
          {t("purchaseCancel.backToBilling")}
        </Button>
      </CardContent>
    </Card>
  );
}
