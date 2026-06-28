"use client";

import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { LayersIcon, CheckIcon } from "lucide-react";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/app/empty-state";
import { billingQueries } from "@/features/billing/api";
import type { SubscriptionPlanResponse } from "@/features/billing/api";
import { useSubscribeCheckout } from "@/features/billing/hooks";

/**
 * Plan picker: lists the config-driven subscription plans and a Subscribe button
 * per plan that starts a Stripe checkout (browser redirect via useSubscribeCheckout).
 * The plan the user is already on (Active/Trialing) renders a "Current plan" badge
 * instead of a button. Price is shown on the Stripe checkout page — the plans API
 * deliberately keeps Stripe price ids server-side and exposes only what a plan grants.
 */
export function SubscriptionPlans() {
  const t = useTranslations("billing");
  const { data: plans, isLoading } = useQuery(billingQueries.subscriptionPlans());
  const { data: current } = useQuery(billingQueries.subscriptionMe());

  if (isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-44 rounded-xl" />
        ))}
      </div>
    );
  }

  if (!plans || plans.length === 0) {
    return (
      <EmptyState
        icon={LayersIcon}
        title={t("plans.emptyTitle")}
        description={t("plans.emptyDescription")}
      />
    );
  }

  const activePlanKey =
    current && (current.status === "Active" || current.status === "Trialing")
      ? current.planKey
      : null;

  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {plans.map((plan) => (
        <PlanCard
          key={plan.planKey}
          plan={plan}
          isCurrent={plan.planKey === activePlanKey}
        />
      ))}
    </div>
  );
}

function PlanCard({
  plan,
  isCurrent,
}: {
  plan: SubscriptionPlanResponse;
  isCurrent: boolean;
}) {
  const t = useTranslations("billing");
  const subscribe = useSubscribeCheckout();

  return (
    <Card className="flex flex-col">
      <CardHeader className="pb-2">
        <div className="flex items-start gap-2">
          <LayersIcon className="h-4 w-4 mt-0.5 text-muted-foreground shrink-0" />
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <CardTitle className="text-sm font-semibold">
                {plan.planKey}
              </CardTitle>
              {isCurrent && (
                <Badge variant="default" className="text-xs">
                  {t("plans.current")}
                </Badge>
              )}
            </div>
            <CardDescription className="text-xs mt-0.5">
              {plan.bucketExpiryDays != null
                ? t("plans.creditsExpireIn", {
                    credits: plan.creditsPerPeriod,
                    days: plan.bucketExpiryDays,
                  })
                : t("plans.creditsNoExpiry", {
                    credits: plan.creditsPerPeriod,
                  })}
            </CardDescription>
          </div>
        </div>
      </CardHeader>

      <CardContent className="flex-1 pb-2">
        <p className="text-2xl font-semibold tabular-nums">
          {plan.creditsPerPeriod.toLocaleString()}
        </p>
        <p className="text-xs text-muted-foreground mt-0.5">
          {t("plans.perPeriod")}
        </p>
      </CardContent>

      <CardFooter className="pt-0">
        {isCurrent ? (
          <Button size="sm" variant="outline" className="w-full" disabled>
            <CheckIcon className="h-4 w-4" aria-hidden />
            {t("plans.currentPlan")}
          </Button>
        ) : (
          <Button
            size="sm"
            className="w-full"
            disabled={subscribe.isPending}
            onClick={() => subscribe.mutate(plan.planKey)}
          >
            {subscribe.isPending
              ? t("plans.redirecting")
              : t("plans.subscribe")}
          </Button>
        )}
      </CardFooter>
    </Card>
  );
}
