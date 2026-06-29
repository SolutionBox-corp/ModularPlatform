"use client";

import { useMemo, useState, useTransition } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import Link from "next/link";
import { buildLoginSchema, type LoginFormValues } from "@/features/auth/schema";
import { loginAction } from "@/features/auth/actions";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";
import { ProblemDetails } from "@/components/app/problem-details";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export function LoginForm() {
  const router = useRouter();
  const t = useTranslations("auth");
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<unknown>(null);

  const loginSchema = useMemo(() => buildLoginSchema(t), [t]);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
  });

  function onSubmit(values: LoginFormValues) {
    setServerError(null);
    startTransition(async () => {
      const result = await loginAction(values.email, values.password);
      if (result.ok) {
        toast.success(t("login.success"));
        router.push("/");
        router.refresh();
        return;
      }
      // Re-wrap the structured result as a client-side ApiError to reuse the display logic.
      const err = new ApiError(result);
      if (err.fieldErrors) {
        for (const [field, messages] of Object.entries(err.fieldErrors)) {
          const key = field.toLowerCase() as keyof LoginFormValues;
          if (key === "email" || key === "password") {
            setError(key, { message: messages[0] });
          }
        }
      }
      if (
        err.errorCode === "auth.invalid_credentials" ||
        err.errorCode === "auth.account_locked" ||
        err.errorCode === "auth.locked_out"
      ) {
        setError("password", { message: toDisplayMessage(err, currentLocale()) });
        return;
      }
      setServerError(err);
    });
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
      {serverError !== null && <ProblemDetails error={serverError} />}

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

      <div className="space-y-1.5">
        <div className="flex items-center justify-between gap-3">
          <Label htmlFor="password">{t("password")}</Label>
          <Link
            href="/forgot-password"
            className="text-xs text-primary underline underline-offset-4"
          >
            {t("forgotPassword.link")}
          </Link>
        </div>
        <Input
          id="password"
          type="password"
          autoComplete="current-password"
          aria-invalid={!!errors.password}
          aria-describedby={errors.password ? "password-error" : undefined}
          {...register("password")}
        />
        {errors.password && (
          <p id="password-error" className="text-xs text-destructive">
            {errors.password.message}
          </p>
        )}
      </div>

      <Button type="submit" className="w-full" disabled={isPending}>
        {isPending ? t("login.submitting") : t("login.label")}
      </Button>

      <p className="text-center text-sm text-muted-foreground">
        {t("noAccount")}{" "}
        <Link
          href="/register"
          className="text-primary underline underline-offset-4"
        >
          {t("register.label")}
        </Link>
      </p>
    </form>
  );
}
