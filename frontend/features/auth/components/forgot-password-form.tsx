"use client";

import { useMemo, useState, useTransition } from "react";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import {
  buildForgotPasswordSchema,
  type ForgotPasswordFormValues,
} from "@/features/auth/schema";
import { forgotPasswordAction } from "@/features/auth/actions";
import { ApiError } from "@/lib/api/types";
import { ProblemDetails } from "@/components/app/problem-details";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export function ForgotPasswordForm() {
  const t = useTranslations("auth");
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<unknown>(null);
  const [accepted, setAccepted] = useState(false);

  const schema = useMemo(() => buildForgotPasswordSchema(t), [t]);
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ForgotPasswordFormValues>({
    resolver: zodResolver(schema),
  });

  function onSubmit(values: ForgotPasswordFormValues) {
    setServerError(null);
    setAccepted(false);
    startTransition(async () => {
      const result = await forgotPasswordAction(values.email);
      if (result.ok) {
        setAccepted(true);
        toast.success(t("forgotPassword.toast"));
        return;
      }

      const err = new ApiError(result);
      if (err.fieldErrors?.email?.[0]) {
        setError("email", { message: err.fieldErrors.email[0] });
        return;
      }
      setServerError(err);
    });
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
      {serverError !== null && <ProblemDetails error={serverError} />}

      {accepted && (
        <div className="rounded-md border border-border bg-muted/40 px-3 py-2 text-sm">
          {t("forgotPassword.accepted")}
        </div>
      )}

      <div className="space-y-1.5">
        <Label htmlFor="email">{t("email")}</Label>
        <Input
          id="email"
          type="email"
          autoComplete="email"
          autoFocus
          aria-invalid={!!errors.email}
          aria-describedby={errors.email ? "email-error" : undefined}
          {...register("email")}
        />
        {errors.email && (
          <p id="email-error" className="text-xs text-destructive">
            {errors.email.message}
          </p>
        )}
      </div>

      <Button type="submit" className="w-full" disabled={isPending}>
        {isPending ? t("forgotPassword.submitting") : t("forgotPassword.label")}
      </Button>

      <p className="text-center text-sm text-muted-foreground">
        <Link href="/login" className="text-primary underline underline-offset-4">
          {t("backToLogin")}
        </Link>
      </p>
    </form>
  );
}
