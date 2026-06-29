"use client";

import { useEffect, useState, useTransition } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { verifyEmailAction, type AuthResult } from "@/features/auth/actions";
import { ApiError } from "@/lib/api/types";
import { ProblemDetails } from "@/components/app/problem-details";
import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface VerifyEmailResultProps {
  token?: string;
}

export function VerifyEmailResult({ token }: VerifyEmailResultProps) {
  const t = useTranslations("auth");
  const [isPending, startTransition] = useTransition();
  const [result, setResult] = useState<AuthResult | null>(null);

  useEffect(() => {
    if (!token) {
      return;
    }

    startTransition(async () => {
      setResult(await verifyEmailAction(token));
    });
  }, [token]);

  if (!token) {
    return (
      <div className="space-y-4">
        <ProblemDetails
          error={
            new ApiError({
              status: 400,
              errorCode: "auth.email_verification_invalid",
              detail: t("verifyEmail.missingToken"),
            })
          }
        />
        <Link
          href="/login"
          className={cn(buttonVariants({ variant: "outline" }), "w-full")}
        >
          {t("backToLogin")}
        </Link>
      </div>
    );
  }

  if (isPending || result === null) {
    return (
      <div className="rounded-md border border-border bg-muted/40 px-3 py-2 text-sm">
        {t("verifyEmail.verifying")}
      </div>
    );
  }

  if (!result.ok) {
    return (
      <div className="space-y-4">
        <ProblemDetails error={new ApiError(result)} />
        <Link
          href="/login"
          className={cn(buttonVariants({ variant: "outline" }), "w-full")}
        >
          {t("backToLogin")}
        </Link>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="rounded-md border border-border bg-muted/40 px-3 py-2 text-sm">
        {t("verifyEmail.success")}
      </div>
      <Link href="/login" className={cn(buttonVariants(), "w-full")}>
        {t("login.label")}
      </Link>
    </div>
  );
}
