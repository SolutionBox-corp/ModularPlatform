"use client";

import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { changePassword } from "@/features/account/api";
import {
  changePasswordSchema,
  type ChangePasswordValues,
} from "@/features/account/schema";
import { logoutAction } from "@/features/auth/actions";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

/**
 * Change-password card. On success the backend revokes every session, so we sign the
 * user out (clears the BFF session) and send them to /login to re-authenticate.
 */
export function ChangePasswordForm() {
  const t = useTranslations("account");
  const router = useRouter();
  const [isLeaving, startTransition] = useTransition();

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ChangePasswordValues>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: { currentPassword: "", newPassword: "", confirmPassword: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    try {
      await changePassword({
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      });
      toast.success(t("password.changed"));
      startTransition(async () => {
        await logoutAction();
        router.push("/login?reason=password-changed");
      });
    } catch (err) {
      if (err instanceof ApiError && err.errorCode === "user.current_password_invalid") {
        setError("currentPassword", { message: "currentInvalid" });
        return;
      }
      toast.error(
        err instanceof ApiError
          ? toDisplayMessage(err, currentLocale())
          : t("password.error"),
      );
    }
  });

  const busy = isSubmitting || isLeaving;

  return (
    <Card>
      <form onSubmit={onSubmit} noValidate>
        <CardHeader>
          <CardTitle className="text-base font-semibold">
            {t("password.title")}
          </CardTitle>
          <CardDescription className="text-sm">
            {t("password.description")}
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-5">
          <div className="space-y-1.5">
            <Label htmlFor="cp-current">{t("password.current")}</Label>
            <Input
              id="cp-current"
              type="password"
              autoComplete="current-password"
              aria-invalid={!!errors.currentPassword}
              {...register("currentPassword")}
            />
            {errors.currentPassword && (
              <p className="text-xs text-destructive">
                {errors.currentPassword.message === "currentInvalid"
                  ? t("password.currentInvalid")
                  : t("password.currentRequired")}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="cp-new">{t("password.new")}</Label>
            <Input
              id="cp-new"
              type="password"
              autoComplete="new-password"
              aria-invalid={!!errors.newPassword}
              {...register("newPassword")}
            />
            {errors.newPassword && (
              <p className="text-xs text-destructive">
                {errors.newPassword.message === "tooLong"
                  ? t("password.tooLong")
                  : t("password.tooShort")}
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="cp-confirm">{t("password.confirm")}</Label>
            <Input
              id="cp-confirm"
              type="password"
              autoComplete="new-password"
              aria-invalid={!!errors.confirmPassword}
              {...register("confirmPassword")}
            />
            {errors.confirmPassword && (
              <p className="text-xs text-destructive">
                {t("password.mismatch")}
              </p>
            )}
          </div>
        </CardContent>

        <CardFooter>
          <Button type="submit" disabled={busy}>
            {busy ? t("password.saving") : t("password.submit")}
          </Button>
        </CardFooter>
      </form>
    </Card>
  );
}
