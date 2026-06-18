"use client";

import { AlertCircleIcon } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { toDisplayMessage, currentLocale } from "@/lib/errors/error-map";

interface ProblemDetailsProps {
  /** Any thrown value — an ApiError, a standard Error, or an unknown. */
  error: unknown;
}

/**
 * Renders any thrown error as a non-alarming inline alert.
 * Used inside error.tsx boundaries and forms to surface backend problems.
 * Never shows raw stack traces or internal error details.
 */
export function ProblemDetails({ error }: ProblemDetailsProps) {
  const message = toDisplayMessage(error, currentLocale());

  return (
    <Alert variant="destructive" role="alert">
      <AlertCircleIcon aria-hidden="true" />
      <AlertTitle>Error</AlertTitle>
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  );
}
