"use client";

import { ProblemDetails } from "@/components/app/problem-details";
import { Button } from "@/components/ui/button";

interface ErrorProps {
  error: Error & { digest?: string };
  unstable_retry: () => void;
}

export default function AuthError({ error, unstable_retry }: ErrorProps) {
  return (
    <div className="flex flex-col items-center justify-center min-h-96 gap-4 p-6">
      <ProblemDetails error={error} />
      <Button variant="outline" size="sm" onClick={unstable_retry}>
        Try again
      </Button>
    </div>
  );
}
