"use client";

import Link from "next/link";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { TERMS_VERSION } from "@/lib/legal-versions";
import { ApiError } from "@/lib/api/types";
import { currentLocale, toDisplayMessage } from "@/lib/errors/error-map";
import {
  acceptTerms,
  accountQueries,
  type UserProfileResponse,
} from "@/features/account/api";

interface TermsAcceptanceCardProps {
  profile?: UserProfileResponse;
}

export function TermsAcceptanceCard({ profile }: TermsAcceptanceCardProps) {
  const t = useTranslations("account");
  const queryClient = useQueryClient();

  const accept = useMutation({
    mutationFn: () => acceptTerms(TERMS_VERSION),
    onSuccess: async (updated) => {
      queryClient.setQueryData(accountQueries.profile().queryKey, updated);
      await queryClient.invalidateQueries({
        queryKey: accountQueries.profile().queryKey,
      });
      toast.success(t("termsAcceptance.accepted"));
    },
    onError: (err) => {
      toast.error(
        err instanceof ApiError
          ? toDisplayMessage(err, currentLocale())
          : t("termsAcceptance.error"),
      );
    },
  });

  if (!profile || profile.acceptedTermsVersion === TERMS_VERSION) {
    return null;
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base font-semibold">
          {t("termsAcceptance.title")}
        </CardTitle>
        <CardDescription className="text-sm">
          {t("termsAcceptance.description", { version: TERMS_VERSION })}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <p className="text-sm text-muted-foreground">
          {t.rich("termsAcceptance.hint", {
            terms: (chunks) => (
              <Link href="/terms" className="underline underline-offset-4">
                {chunks}
              </Link>
            ),
            privacy: (chunks) => (
              <Link href="/privacy" className="underline underline-offset-4">
                {chunks}
              </Link>
            ),
          })}
        </p>
      </CardContent>
      <CardFooter>
        <Button onClick={() => accept.mutate()} disabled={accept.isPending}>
          {accept.isPending
            ? t("termsAcceptance.accepting")
            : t("termsAcceptance.accept")}
        </Button>
      </CardFooter>
    </Card>
  );
}
