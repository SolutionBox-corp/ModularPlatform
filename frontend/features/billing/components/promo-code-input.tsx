"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { TagIcon, CheckCircle2Icon, XCircleIcon } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { MoneyAmount } from "@/components/app/money-amount";
import { usePromoCode } from "@/features/billing/hooks";
import {
  buildPromoCodeSchema,
  type PromoCodeInput,
} from "@/features/billing/schema";

/**
 * Inline promo-code validator. Calls GET /v1/billing/promo-codes/{code}/validate
 * and shows the discount details on success.
 *
 * This is a display-only widget — the discount is applied by Stripe during
 * checkout when AllowPromotionCodes is enabled on the Checkout session.
 */
export function PromoCodeInput() {
  const t = useTranslations("billing");
  const [submitted, setSubmitted] = useState<string>("");

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<PromoCodeInput>({
    resolver: zodResolver(buildPromoCodeSchema(t)),
    defaultValues: { code: "" },
  });

  const { data, isFetching, isError } = usePromoCode(
    submitted,
    submitted.length > 0,
  );

  const onSubmit = (values: PromoCodeInput) => {
    setSubmitted(values.code);
  };

  const onReset = () => {
    setSubmitted("");
    reset();
  };

  return (
    <div className="space-y-3">
      <form
        onSubmit={handleSubmit(onSubmit)}
        noValidate
        className="space-y-2"
      >
        <Label htmlFor="promo-code">{t("promo.label")}</Label>
        <div className="flex gap-2 items-start">
          <div className="flex-1 space-y-1">
            <Input
              id="promo-code"
              {...register("code")}
              placeholder={t("promo.inputHint")}
              className="uppercase placeholder:normal-case font-mono text-sm"
              disabled={isFetching}
              aria-invalid={!!errors.code}
              aria-describedby={errors.code ? "promo-code-error" : undefined}
            />
            {errors.code && (
              <p id="promo-code-error" className="text-xs text-destructive">
                {errors.code.message}
              </p>
            )}
          </div>
          <Button
            type="submit"
            variant="outline"
            disabled={isFetching}
            className="shrink-0"
          >
            <TagIcon className="h-3.5 w-3.5 mr-1.5" />
            {isFetching ? t("promo.checking") : t("promo.apply")}
          </Button>
          {submitted && (
            <Button
              type="button"
              variant="ghost"
              onClick={onReset}
              className="shrink-0"
            >
              {t("promo.clear")}
            </Button>
          )}
        </div>
      </form>

      {/* Success result */}
      {submitted && data && (
        <div
          role="status"
          aria-live="polite"
          aria-atomic="true"
          className="flex items-center gap-2 rounded-lg border border-border bg-muted/40 px-3 py-2"
        >
          <CheckCircle2Icon className="h-4 w-4 text-success shrink-0" />
          <span className="text-sm">
            <span className="font-medium font-mono">{data.code}</span>
            {" — "}
            {data.percentOff != null
              ? t("promo.percentOff", { percent: data.percentOff })
              : data.amountOff != null && data.currency != null
                ? (
                  <>
                    <MoneyAmount
                      value={data.amountOff / 100}
                      currency={data.currency}
                    />{" "}
                    {t("promo.amountOffSuffix")}
                  </>
                )
                : t("promo.discountApplied")}
          </span>
        </div>
      )}

      {/* Error result */}
      {submitted && isError && (
        <div
          role="alert"
          aria-live="polite"
          aria-atomic="true"
          className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2"
        >
          <XCircleIcon className="h-4 w-4 text-destructive shrink-0" />
          <p className="text-sm text-destructive">
            {t("promo.invalid")}
          </p>
        </div>
      )}
    </div>
  );
}
