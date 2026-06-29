"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import {
  accountQueries,
  requestEmailVerification,
  type UserProfileResponse,
} from "@/features/account/api";
import { ApiError } from "@/lib/api/types";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

interface EmailVerificationCardProps {
  profile?: UserProfileResponse;
}

export function EmailVerificationCard({ profile }: EmailVerificationCardProps) {
  const t = useTranslations("account");
  const queryClient = useQueryClient();

  const resend = useMutation({
    mutationFn: requestEmailVerification,
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: accountQueries.profile().queryKey,
      });
      toast.success(t("emailVerification.sent"));
    },
    onError: (err) => {
      toast.error(
        err instanceof ApiError
          ? toDisplayMessage(err, currentLocale())
          : t("emailVerification.error"),
      );
    },
  });

  if (!profile || profile.emailConfirmed) {
    return null;
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base font-semibold">
          {t("emailVerification.title")}
        </CardTitle>
        <CardDescription className="text-sm">
          {t("emailVerification.description", { email: profile.email })}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <p className="text-sm text-muted-foreground">
          {t("emailVerification.hint")}
        </p>
      </CardContent>
      <CardFooter>
        <Button onClick={() => resend.mutate()} disabled={resend.isPending}>
          {resend.isPending
            ? t("emailVerification.sending")
            : t("emailVerification.resend")}
        </Button>
      </CardFooter>
    </Card>
  );
}
