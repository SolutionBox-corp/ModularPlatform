"use client";

import { useMemo, useState, useTransition } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import {
  buildResetPasswordSchema,
  type ResetPasswordFormValues,
} from "@/features/auth/schema";
import { resetPasswordAction } from "@/features/auth/actions";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";
import { ProblemDetails } from "@/components/app/problem-details";
import { Button, buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";

interface ResetPasswordFormProps {
  token?: string;
}

export function ResetPasswordForm({ token }: ResetPasswordFormProps) {
  const router = useRouter();
  const t = useTranslations("auth");
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<unknown>(null);

  const schema = useMemo(() => buildResetPasswordSchema(t), [t]);
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ResetPasswordFormValues>({
    resolver: zodResolver(schema),
  });

  if (!token) {
    return (
      <div className="space-y-4">
        <div className="rounded-md border border-destructive/40 bg-destructive/5 px-3 py-2 text-sm text-destructive">
          {t("resetPassword.missingToken")}
        </div>
        <Link
          href="/forgot-password"
          className={cn(buttonVariants({ variant: "outline" }), "w-full")}
        >
          {t("forgotPassword.label")}
        </Link>
      </div>
    );
  }

  const resetToken = token;

  function onSubmit(values: ResetPasswordFormValues) {
    setServerError(null);
    startTransition(async () => {
      const result = await resetPasswordAction(resetToken, values.newPassword);
      if (result.ok) {
        toast.success(t("resetPassword.success"));
        router.push("/login?reason=password-reset");
        router.refresh();
        return;
      }

      const err = new ApiError(result);
      if (err.fieldErrors?.newPassword?.[0]) {
        setError("newPassword", { message: err.fieldErrors.newPassword[0] });
        return;
      }
      if (err.errorCode === "user.password_unchanged") {
        setError("newPassword", { message: toDisplayMessage(err, currentLocale()) });
        return;
      }
      setServerError(err);
    });
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
      {serverError !== null && <ProblemDetails error={serverError} />}

      <div className="space-y-1.5">
        <Label htmlFor="newPassword">{t("newPassword")}</Label>
        <Input
          id="newPassword"
          type="password"
          autoComplete="new-password"
          aria-invalid={!!errors.newPassword}
          aria-describedby={errors.newPassword ? "new-password-error" : undefined}
          {...register("newPassword")}
        />
        {errors.newPassword && (
          <p id="new-password-error" className="text-xs text-destructive">
            {errors.newPassword.message}
          </p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="confirmPassword">{t("confirmPassword")}</Label>
        <Input
          id="confirmPassword"
          type="password"
          autoComplete="new-password"
          aria-invalid={!!errors.confirmPassword}
          aria-describedby={errors.confirmPassword ? "confirm-password-error" : undefined}
          {...register("confirmPassword")}
        />
        {errors.confirmPassword && (
          <p id="confirm-password-error" className="text-xs text-destructive">
            {errors.confirmPassword.message}
          </p>
        )}
      </div>

      <Button type="submit" className="w-full" disabled={isPending}>
        {isPending ? t("resetPassword.submitting") : t("resetPassword.label")}
      </Button>
    </form>
  );
}
