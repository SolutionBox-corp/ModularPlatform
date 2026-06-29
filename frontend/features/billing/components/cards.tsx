"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import {
  CreditCardIcon,
  CircleDollarSignIcon,
  AlertCircleIcon,
} from "lucide-react";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { MoneyAmount } from "@/components/app/money-amount";
import { billingQueries } from "@/features/billing/api";
import { useLocale, useTranslations } from "next-intl";
import { useCancelSubscription, useBillingPortal } from "@/features/billing/hooks";

// ---------------------------------------------------------------------------
// Status badge variant
// ---------------------------------------------------------------------------

function subscriptionStatusVariant(
  status: string,
): "default" | "secondary" | "destructive" | "outline" {
  switch (status) {
    case "Active":
    case "Trialing":
      return "default";
    case "PastDue":
      return "destructive";
    default:
      return "secondary";
  }
}

// ---------------------------------------------------------------------------
// CreditBalanceCard
// ---------------------------------------------------------------------------

/**
 * Shows the user's real-time credit balance.
 * Stays live — the realtime provider invalidates the billing root on
 * credit-related server-sent events, so useQuery re-fetches automatically.
 */
export function CreditBalanceCard() {
  const t = useTranslations("billing");
  const { data, isLoading } = useQuery(billingQueries.balance());

  return (
    <Card>
      <CardHeader className="pb-2 flex flex-row items-start gap-2">
        <CircleDollarSignIcon className="h-4 w-4 mt-0.5 text-muted-foreground shrink-0" />
        <div className="flex-1 min-w-0">
          <CardTitle className="text-sm font-medium">
            {t("creditCard.title")}
          </CardTitle>
          <CardDescription className="text-xs">
            {t("creditCard.description")}
          </CardDescription>
        </div>
      </CardHeader>
      <CardContent>
        {isLoading ? (
          <Skeleton className="h-8 w-24" />
        ) : data ? (
          <div className="space-y-0.5">
            <p className="text-2xl font-semibold">
              <MoneyAmount value={data.available} />
            </p>
            <p className="text-xs text-muted-foreground">
              {t.rich("creditCard.ofPosted", {
                amount: () => (
                  <MoneyAmount value={data.posted} className="text-xs" />
                ),
              })}
            </p>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">
            {t("creditCard.noAccount")}
          </p>
        )}
        <Link
          href="/billing"
          className="mt-3 block text-xs text-primary underline-offset-4 hover:underline"
        >
          {t("creditCard.manage")}
        </Link>
      </CardContent>
    </Card>
  );
}

// ---------------------------------------------------------------------------
// SubscriptionCard
// ---------------------------------------------------------------------------

/**
 * Shows the current subscription plan, status, renewal date, and cancel action.
 * Cancel sets cancelAtPeriodEnd on Stripe — the subscription stays active until
 * the period ends; no immediate loss of access.
 */
export function SubscriptionCard() {
  const t = useTranslations("billing");
  const locale = useLocale();
  const { data, isLoading } = useQuery(billingQueries.subscriptionMe());
  const cancel = useCancelSubscription();
  const portal = useBillingPortal();

  const canCancel =
    !!data &&
    (data.status === "Active" || data.status === "Trialing") &&
    !data.cancelAtPeriodEnd;

  return (
    <Card>
      <CardHeader className="pb-2 flex flex-row items-start gap-2">
        <CreditCardIcon className="h-4 w-4 mt-0.5 text-muted-foreground shrink-0" />
        <div className="flex-1 min-w-0">
          <CardTitle className="text-sm font-medium">
            {t("subscriptionCard.title")}
          </CardTitle>
          <CardDescription className="text-xs">
            {t("subscriptionCard.description")}
          </CardDescription>
        </div>
      </CardHeader>
      <CardContent>
        {isLoading ? (
          <Skeleton className="h-6 w-28" />
        ) : data ? (
          <div className="space-y-1.5">
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{data.planKey}</span>
              <Badge
                variant={subscriptionStatusVariant(data.status)}
                className="text-xs"
              >
                {data.status}
              </Badge>
            </div>

            {data.currentPeriodEnd && (
              <p className="text-xs text-muted-foreground">
                {data.cancelAtPeriodEnd
                  ? t("subscriptionCard.cancels")
                  : t("subscriptionCard.renews")}{" "}
                {new Date(data.currentPeriodEnd).toLocaleDateString(locale, {
                  month: "short",
                  day: "numeric",
                  year: "numeric",
                })}
              </p>
            )}

            {data.cancelAtPeriodEnd && (
              <p className="flex items-center gap-1 text-xs text-destructive">
                <AlertCircleIcon className="h-3 w-3 shrink-0" />
                {t("subscriptionCard.cancelsAtPeriodEnd")}
              </p>
            )}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">
            {t("subscriptionCard.none")}
          </p>
        )}

        <div className="mt-3 flex flex-wrap items-center gap-3">
          <Link
            href="/billing#plans"
            className="text-xs text-primary underline-offset-4 hover:underline"
          >
            {t("subscriptionCard.viewPlans")}
          </Link>

          <Button
            variant="ghost"
            size="sm"
            className="h-auto p-0 text-xs text-primary hover:underline"
            disabled={portal.isPending}
            onClick={() => portal.mutate()}
          >
            {portal.isPending
              ? t("portal.opening")
              : t("portal.manage")}
          </Button>

          {canCancel && (
            <Button
              variant="ghost"
              size="sm"
              className="h-auto p-0 text-xs text-muted-foreground hover:text-destructive"
              disabled={cancel.isPending}
              onClick={() => cancel.mutate()}
            >
              {cancel.isPending
                ? t("subscriptionCard.cancelling")
                : t("subscriptionCard.cancelPlan")}
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
