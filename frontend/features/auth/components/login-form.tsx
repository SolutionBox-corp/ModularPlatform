"use client";

import { useState, useTransition } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import Link from "next/link";
import { loginSchema, type LoginFormValues } from "@/features/auth/schema";
import { loginAction } from "@/features/auth/actions";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";
import { ProblemDetails } from "@/components/app/problem-details";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export function LoginForm() {
  const router = useRouter();
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<unknown>(null);

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
      try {
        await loginAction(values.email, values.password);
        toast.success("Welcome back!");
        router.push("/");
        router.refresh();
      } catch (err) {
        if (err instanceof ApiError) {
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
            err.errorCode === "auth.account_locked"
          ) {
            setError("password", {
              message: toDisplayMessage(err, currentLocale()),
            });
            return;
          }
        }
        setServerError(err);
      }
    });
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
      {serverError !== null && <ProblemDetails error={serverError} />}

      <div className="space-y-1.5">
        <Label htmlFor="email">Email</Label>
        <Input
          id="email"
          type="email"
          autoComplete="email"
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
        <Label htmlFor="password">Password</Label>
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
        {isPending ? "Signing in…" : "Sign in"}
      </Button>

      <p className="text-center text-sm text-muted-foreground">
        {"Don't have an account?"}{" "}
        <Link
          href="/register"
          className="text-primary underline-offset-4 hover:underline"
        >
          Create account
        </Link>
      </p>
    </form>
  );
}
