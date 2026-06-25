"use client";

import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { accountQueries, updateProfile } from "@/features/account/api";
import { profileUpdateSchema, type ProfileUpdateValues } from "@/features/account/schema";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";
import { locales, type AppLocale } from "@/lib/i18n/config";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

const LOCALE_LABELS: Record<AppLocale, string> = {
  en: "English",
  cs: "Čeština",
};

/**
 * Editable profile form (PATCH /v1/identity/users/me). Email is read-only (changing it
 * needs a verified-email-change flow that does not exist yet). Display name + locale are
 * editable. Saving a new locale also sets the NEXT_LOCALE cookie and reloads so the UI
 * language follows the stored preference (mirrors the top-bar locale toggle).
 */
export function ProfileForm() {
  const t = useTranslations("account");
  const queryClient = useQueryClient();
  const { data, isLoading } = useQuery(accountQueries.profile());

  const {
    control,
    register,
    handleSubmit,
    formState: { errors, isDirty },
  } = useForm<ProfileUpdateValues>({
    resolver: zodResolver(profileUpdateSchema),
    // `values` reactively syncs with the loaded profile (RHF deep-compares, so a
    // same-content refetch won't clobber in-progress edits).
    values: {
      displayName: data?.displayName ?? "",
      locale: (data?.locale as AppLocale) ?? "en",
    },
  });

  const mutation = useMutation({
    mutationFn: updateProfile,
    onSuccess: async (updated) => {
      await queryClient.invalidateQueries({
        queryKey: accountQueries.profile().queryKey,
      });
      toast.success(t("form.saved"));
      // If the stored locale changed, follow it in the UI (cookie + reload, like the top-bar toggle).
      if (
        typeof document !== "undefined" &&
        updated.locale &&
        updated.locale !== currentLocale()
      ) {
        document.cookie = `NEXT_LOCALE=${updated.locale}; path=/; max-age=${60 * 60 * 24 * 365}; samesite=lax`;
        window.location.reload();
      }
    },
    onError: (err) => {
      toast.error(
        err instanceof ApiError
          ? toDisplayMessage(err, currentLocale())
          : t("form.saveError"),
      );
    },
  });

  const onSubmit = handleSubmit((values) => {
    const name = values.displayName.trim();
    mutation.mutate({
      displayName: name === "" ? null : name,
      locale: values.locale,
    });
  });

  return (
    <Card>
      <form onSubmit={onSubmit}>
        <CardHeader>
          <CardTitle className="text-base font-semibold">
            {t("form.title")}
          </CardTitle>
          <CardDescription className="text-sm">
            {t("form.description")}
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-5">
          {/* Email — read-only */}
          <div className="space-y-1.5">
            <Label htmlFor="profile-email">{t("form.email.label")}</Label>
            {isLoading ? (
              <Skeleton className="h-8 w-full" />
            ) : (
              <Input
                id="profile-email"
                type="email"
                value={data?.email ?? ""}
                readOnly
                disabled
                aria-label={t("form.email.ariaLabel")}
              />
            )}
            <p className="text-xs text-muted-foreground">
              {t("form.email.readOnlyHint")}
            </p>
          </div>

          {/* Display name — editable */}
          <div className="space-y-1.5">
            <Label htmlFor="profile-display-name">
              {t("form.displayName.label")}
            </Label>
            {isLoading ? (
              <Skeleton className="h-8 w-full" />
            ) : (
              <Input
                id="profile-display-name"
                type="text"
                placeholder={t("form.displayName.inputHint")}
                aria-label={t("form.displayName.ariaLabel")}
                aria-invalid={!!errors.displayName}
                {...register("displayName")}
              />
            )}
            {errors.displayName && (
              <p className="text-xs text-destructive">
                {t("form.displayName.tooLong")}
              </p>
            )}
          </div>

          {/* Locale — editable */}
          <div className="space-y-1.5">
            <Label htmlFor="profile-locale">{t("form.locale.label")}</Label>
            {isLoading ? (
              <Skeleton className="h-8 w-32" />
            ) : (
              <Controller
                control={control}
                name="locale"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger
                      id="profile-locale"
                      className="w-48"
                      aria-label={t("form.locale.ariaLabel")}
                    >
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {locales.map((loc) => (
                        <SelectItem key={loc} value={loc}>
                          {LOCALE_LABELS[loc]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            )}
            <p className="text-xs text-muted-foreground">
              {t("form.locale.hint")}
            </p>
          </div>
        </CardContent>

        <CardFooter>
          <Button
            type="submit"
            disabled={mutation.isPending || isLoading || !isDirty}
          >
            {mutation.isPending ? t("form.saving") : t("form.save")}
          </Button>
        </CardFooter>
      </form>
    </Card>
  );
}
