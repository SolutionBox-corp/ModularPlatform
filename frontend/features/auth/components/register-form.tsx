"use client";

import { useMemo, useState, useTransition } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import Link from "next/link";
import { buildRegisterSchema, type RegisterFormValues } from "@/features/auth/schema";
import { registerAction } from "@/features/auth/actions";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";
import { ProblemDetails } from "@/components/app/problem-details";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";

export function RegisterForm() {
  const router = useRouter();
  const t = useTranslations("auth");
  const searchParams = useSearchParams();
  const inviteToken = searchParams.get("invite") ?? undefined;
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<unknown>(null);

  const registerSchema = useMemo(() => buildRegisterSchema(t), [t]);

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    setError,
    formState: { errors },
  } = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      inviteToken,
      // Cast because react-hook-form's defaultValues must be partial;
      // validation will catch when this is false.
      acceptTerms: false as unknown as true,
    },
  });

  const acceptTerms = watch("acceptTerms");

  function onSubmit(values: RegisterFormValues) {
    setServerError(null);
    startTransition(async () => {
      const result = await registerAction(
        values.email,
        values.password,
        values.displayName,
        values.inviteToken,
      );
      if (result.ok) {
        toast.success(t("register.success"));
        router.push("/");
        router.refresh();
        return;
      }
      // Re-wrap the structured result as a client-side ApiError to reuse the display logic.
      const err = new ApiError(result);
      if (err.fieldErrors) {
        const map: Record<string, keyof RegisterFormValues> = {
          email: "email", password: "password", displayname: "displayName", invitetoken: "inviteToken",
        };
        for (const [field, messages] of Object.entries(err.fieldErrors)) {
          const mapped = map[field.toLowerCase()];
          if (mapped && messages[0]) setError(mapped, { message: messages[0] });
        }
      }
      if (err.errorCode === "user.email_taken") {
        setError("email", { message: toDisplayMessage(err, currentLocale()) });
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
          aria-describedby={errors.email ? "reg-email-error" : undefined}
          {...register("email")}
        />
        {errors.email && (
          <p id="reg-email-error" className="text-xs text-destructive">
            {errors.email.message}
          </p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="displayName">
          {t("displayName")}{" "}
          <span className="text-muted-foreground font-normal">{t("displayNameOptional")}</span>
        </Label>
        <Input
          id="displayName"
          type="text"
          autoComplete="name"
          aria-invalid={!!errors.displayName}
          aria-describedby={errors.displayName ? "reg-name-error" : undefined}
          {...register("displayName")}
        />
        {errors.displayName && (
          <p id="reg-name-error" className="text-xs text-destructive">
            {errors.displayName.message}
          </p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="reg-password">{t("password")}</Label>
        <Input
          id="reg-password"
          type="password"
          autoComplete="new-password"
          aria-invalid={!!errors.password}
          aria-describedby={errors.password ? "reg-pw-error" : undefined}
          {...register("password")}
        />
        {errors.password && (
          <p id="reg-pw-error" className="text-xs text-destructive">
            {errors.password.message}
          </p>
        )}
      </div>

      {/* Hidden invite token — populated from URL param */}
      <input type="hidden" {...register("inviteToken")} />

      <div className="space-y-1.5">
        <div className="flex items-start gap-2">
          <Checkbox
            id="acceptTerms"
            checked={acceptTerms === true}
            onCheckedChange={(checked) =>
              setValue(
                "acceptTerms",
                checked as unknown as true,
                { shouldValidate: true },
              )
            }
            aria-invalid={!!errors.acceptTerms}
            aria-describedby={
              errors.acceptTerms ? "reg-terms-error" : undefined
            }
          />
          <Label
            htmlFor="acceptTerms"
            className="text-sm font-normal text-muted-foreground leading-snug cursor-pointer"
          >
            {t("termsPrefix")}{" "}
            <Link
              href="/terms"
              className="text-primary underline underline-offset-4"
            >
              {t("termsLink")}
            </Link>{" "}
            {t("termsConjunction")}{" "}
            <Link
              href="/privacy"
              className="text-primary underline underline-offset-4"
            >
              {t("privacyLink")}
            </Link>
          </Label>
        </div>
        {errors.acceptTerms && (
          <p id="reg-terms-error" className="text-xs text-destructive">
            {errors.acceptTerms.message}
          </p>
        )}
      </div>

      <Button type="submit" className="w-full" disabled={isPending}>
        {isPending ? t("register.submitting") : t("register")}
      </Button>

      <p className="text-center text-sm text-muted-foreground">
        {t("haveAccount")}{" "}
        <Link
          href="/login"
          className="text-primary underline underline-offset-4"
        >
          {t("login")}
        </Link>
      </p>
    </form>
  );
}
