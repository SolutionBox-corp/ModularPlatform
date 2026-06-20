"use client";

import { ProblemDetails } from "@/components/app/problem-details";
import { Button } from "@/components/ui/button";
import { useTranslations } from "next-intl";

interface ErrorProps {
  error: Error & { digest?: string };
  unstable_retry: () => void;
}

export default function PlatformError({ error, unstable_retry }: ErrorProps) {
  const t = useTranslations("shell");
  return (
    <div className="flex flex-col items-center justify-center min-h-96 gap-4 p-6">
      <ProblemDetails error={error} />
      <Button variant="outline" size="sm" onClick={unstable_retry}>
        {t("error.tryAgain")}
      </Button>
    </div>
  );
}
