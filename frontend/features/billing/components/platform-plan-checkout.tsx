"use client";

import { useLocale, useTranslations } from "next-intl";
import { ExternalLinkIcon, ShieldCheckIcon } from "lucide-react";
import { toast } from "sonner";
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
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { EmptyState } from "@/components/app/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import {
  useCreatePlatformCheckout,
  useMyPlatformBillingStatus,
  usePlatformPlans,
} from "@/features/platform/hooks";
import type { PlatformPlanResponse } from "@/features/platform/api";

function safeProviderRedirect(url: string, messages: {
  invalidRedirect: string;
  unexpectedRedirect: string;
}) {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    toast.error(messages.invalidRedirect);
    return;
  }

  if (parsed.protocol !== "https:" || parsed.hostname === "localhost") {
    toast.error(messages.unexpectedRedirect);
    return;
  }

  window.location.href = url;
}

export function PlatformPlanCheckout() {
  const t = useTranslations("billing.platformPlans");
  const checkoutT = useTranslations("billing.checkout");
  const locale = useLocale();
  const plans = usePlatformPlans();
  const status = useMyPlatformBillingStatus();
  const checkout = useCreatePlatformCheckout();

  if (plans.isLoading || status.isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 2 }).map((_, index) => (
          <Skeleton key={index} className="h-44 rounded-xl" />
        ))}
      </div>
    );
  }

  if (!plans.data || plans.data.length === 0) {
    return (
      <EmptyState
        icon={ShieldCheckIcon}
        title={t("emptyTitle")}
        description={t("emptyDescription")}
      />
    );
  }

  const checkoutReady = status.data?.checkoutReady ?? false;
  const currentPlan = status.data?.plan ?? "free";

  return (
    <div className="space-y-4">
      {!checkoutReady && (
        <Alert>
          <ShieldCheckIcon aria-hidden="true" />
          <AlertTitle>{t("notReadyTitle")}</AlertTitle>
          <AlertDescription>
            {status.data?.actionRequired
              ? t("notReadyAction", { code: status.data.actionRequired })
              : t("notReadyDescription")}
          </AlertDescription>
        </Alert>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {plans.data.map((plan) => (
          <PlatformPlanCard
            key={plan.planKey}
            plan={plan}
            locale={locale}
            currentPlan={currentPlan}
            disabled={!checkoutReady || checkout.isPending}
            submitting={checkout.isPending}
            onCheckout={() =>
              checkout.mutate(
                { planKey: plan.planKey },
                {
                  onSuccess: (result) =>
                    safeProviderRedirect(result.redirectUrl, {
                      invalidRedirect: checkoutT("invalidRedirect"),
                      unexpectedRedirect: checkoutT("unexpectedRedirect"),
                    }),
                  onError: () => toast.error(t("error")),
                },
              )
            }
          />
        ))}
      </div>
    </div>
  );
}

function PlatformPlanCard({
  plan,
  locale,
  currentPlan,
  disabled,
  submitting,
  onCheckout,
}: {
  plan: PlatformPlanResponse;
  locale: string;
  currentPlan: string;
  disabled: boolean;
  submitting: boolean;
  onCheckout: () => void;
}) {
  const t = useTranslations("billing.platformPlans");
  const isCurrent = plan.planKey.toLowerCase() === currentPlan.toLowerCase();
  const price = new Intl.NumberFormat(locale, {
    style: "currency",
    currency: plan.currency,
  }).format(plan.amountMinorUnits / 100);

  return (
    <Card className="flex flex-col">
      <CardHeader className="pb-2">
        <div className="flex items-start gap-2">
          <ShieldCheckIcon className="h-4 w-4 mt-0.5 text-muted-foreground shrink-0" />
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <CardTitle className="text-sm font-semibold">
                {plan.description}
              </CardTitle>
              {isCurrent && (
                <Badge variant="default" className="text-xs">
                  {t("current")}
                </Badge>
              )}
            </div>
            <CardDescription className="text-xs mt-0.5 font-mono">
              {plan.planKey}
            </CardDescription>
          </div>
        </div>
      </CardHeader>

      <CardContent className="flex-1 pb-2">
        <p className="text-2xl font-semibold tabular-nums">{price}</p>
        <p className="text-xs text-muted-foreground mt-0.5">
          {t("oneTime")}
        </p>
      </CardContent>

      <CardFooter className="pt-0">
        <Button
          size="sm"
          className="w-full"
          variant={isCurrent ? "outline" : "default"}
          disabled={disabled || isCurrent}
          onClick={onCheckout}
        >
          <ExternalLinkIcon className="h-4 w-4" aria-hidden="true" />
          {isCurrent
            ? t("currentPlan")
            : submitting
              ? t("redirecting")
              : t("checkout")}
        </Button>
      </CardFooter>
    </Card>
  );
}
