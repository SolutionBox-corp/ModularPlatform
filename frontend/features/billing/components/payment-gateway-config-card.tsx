"use client";

import {
  Controller,
  useForm,
  useWatch,
  type UseFormRegisterReturn,
} from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { ShieldCheckIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useConfigurePaymentGateway } from "@/features/billing/hooks";
import {
  buildPaymentGatewaySchema,
  type PaymentGatewayFormValues,
} from "@/features/billing/schema";
import type { ConfigurePaymentGatewayInput } from "@/features/billing/api";

const DEFAULT_VALUES: PaymentGatewayFormValues = {
  provider: "stripe",
  currency: "EUR",
  stripeApiKey: "",
  stripeWebhookSecret: "",
  goPayGoid: "",
  goPayClientId: "",
  goPayClientSecret: "",
  sandbox: true,
};

export function PaymentGatewayConfigCard() {
  const t = useTranslations("billing");
  const mutation = useConfigurePaymentGateway();
  const {
    control,
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<PaymentGatewayFormValues>({
    resolver: zodResolver(buildPaymentGatewaySchema(t)),
    defaultValues: DEFAULT_VALUES,
  });

  const provider = useWatch({ control, name: "provider" });
  const pending = isSubmitting || mutation.isPending;

  const onSubmit = handleSubmit(async (values) => {
    const body: ConfigurePaymentGatewayInput = {
      provider: values.provider,
      currency: values.currency,
      sandbox: values.sandbox,
      stripeApiKey: values.provider === "stripe" ? values.stripeApiKey : null,
      stripeWebhookSecret:
        values.provider === "stripe" ? values.stripeWebhookSecret : null,
      goPayGoid:
        values.provider === "gopay" && values.goPayGoid
          ? Number(values.goPayGoid)
          : null,
      goPayClientId: values.provider === "gopay" ? values.goPayClientId : null,
      goPayClientSecret:
        values.provider === "gopay" ? values.goPayClientSecret : null,
    };

    await mutation.mutateAsync(body);
    reset({ ...DEFAULT_VALUES, provider: values.provider, currency: values.currency });
  });

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex items-start gap-2">
          <ShieldCheckIcon className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
          <div className="min-w-0">
            <CardTitle className="text-sm font-medium">
              {t("paymentGateway.title")}
            </CardTitle>
            <CardDescription className="text-xs">
              {t("paymentGateway.description")}
            </CardDescription>
          </div>
        </div>
      </CardHeader>

      <CardContent>
        <form className="space-y-4" onSubmit={onSubmit} noValidate>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="payment-provider">
                {t("paymentGateway.provider")}
              </Label>
              <Controller
                control={control}
                name="provider"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="payment-provider" className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="stripe">Stripe</SelectItem>
                      <SelectItem value="gopay">GoPay</SelectItem>
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="payment-currency">
                {t("paymentGateway.currency")}
              </Label>
              <Input
                id="payment-currency"
                className="font-mono uppercase"
                maxLength={3}
                disabled={pending}
                aria-invalid={!!errors.currency}
                aria-describedby={
                  errors.currency ? "payment-currency-error" : undefined
                }
                {...register("currency")}
              />
              {errors.currency?.message && (
                <p id="payment-currency-error" className="text-xs text-destructive">
                  {errors.currency.message}
                </p>
              )}
            </div>
          </div>

          {provider === "stripe" && (
            <div className="grid gap-4 sm:grid-cols-2">
              <SecretField
                id="stripe-api-key"
                label={t("paymentGateway.stripeApiKey")}
                error={errors.stripeApiKey?.message}
                disabled={pending}
                registration={register("stripeApiKey")}
              />
              <SecretField
                id="stripe-webhook-secret"
                label={t("paymentGateway.stripeWebhookSecret")}
                error={errors.stripeWebhookSecret?.message}
                disabled={pending}
                registration={register("stripeWebhookSecret")}
              />
            </div>
          )}

          {provider === "gopay" && (
            <div className="grid gap-4 sm:grid-cols-3">
              <TextField
                id="gopay-goid"
                label={t("paymentGateway.goPayGoid")}
                error={errors.goPayGoid?.message}
                disabled={pending}
                registration={register("goPayGoid")}
              />
              <SecretField
                id="gopay-client-id"
                label={t("paymentGateway.goPayClientId")}
                error={errors.goPayClientId?.message}
                disabled={pending}
                registration={register("goPayClientId")}
              />
              <SecretField
                id="gopay-client-secret"
                label={t("paymentGateway.goPayClientSecret")}
                error={errors.goPayClientSecret?.message}
                disabled={pending}
                registration={register("goPayClientSecret")}
              />
            </div>
          )}

          <div className="flex items-start gap-2 rounded-md border bg-muted/30 p-3">
            <Controller
              control={control}
              name="sandbox"
              render={({ field }) => (
                <Checkbox
                  id="payment-sandbox"
                  checked={field.value}
                  onCheckedChange={(checked) => field.onChange(checked === true)}
                  disabled={pending}
                />
              )}
            />
            <div className="space-y-0.5">
              <Label htmlFor="payment-sandbox">
                {t("paymentGateway.sandbox")}
              </Label>
              <p className="text-xs text-muted-foreground">
                {t("paymentGateway.secretHint")}
              </p>
            </div>
          </div>

          <div className="flex justify-end">
            <Button type="submit" disabled={pending}>
              {pending
                ? t("paymentGateway.saving")
                : t("paymentGateway.save")}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function SecretField({
  id,
  label,
  error,
  disabled,
  registration,
}: {
  id: string;
  label: string;
  error?: string;
  disabled: boolean;
  registration: UseFormRegisterReturn;
}) {
  return (
    <TextField
      id={id}
      label={label}
      error={error}
      disabled={disabled}
      registration={registration}
      type="password"
      autoComplete="off"
    />
  );
}

function TextField({
  id,
  label,
  error,
  disabled,
  registration,
  type = "text",
  autoComplete,
}: {
  id: string;
  label: string;
  error?: string;
  disabled: boolean;
  registration: UseFormRegisterReturn;
  type?: string;
  autoComplete?: string;
}) {
  const errorId = `${id}-error`;

  return (
    <div className="space-y-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        type={type}
        disabled={disabled}
        autoComplete={autoComplete}
        aria-invalid={!!error}
        aria-describedby={error ? errorId : undefined}
        {...registration}
      />
      {error && (
        <p id={errorId} className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  );
}
