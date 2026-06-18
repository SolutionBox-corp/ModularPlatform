"use client";

import "./globals.css";
import { AlertCircleIcon } from "lucide-react";
import { Button } from "@/components/ui/button";

interface GlobalErrorProps {
  error: Error & { digest?: string };
  reset: () => void;
}

/**
 * Global error boundary — catches errors in the root layout. Cannot use next-intl
 * here (the intl provider is inside the root layout that threw). Kept intentionally
 * minimal: a centered message and a hard-reset button.
 */
export default function GlobalError({ reset }: GlobalErrorProps) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body className="bg-background text-foreground min-h-screen flex items-center justify-center font-sans">
        <div className="text-center max-w-sm px-8 py-8 space-y-4">
          <AlertCircleIcon className="h-10 w-10 mx-auto text-destructive" />
          <h1 className="text-lg font-semibold">Something went wrong</h1>
          <p className="text-sm text-muted-foreground">
            An unexpected error occurred. Please reload the page.
          </p>
          <Button onClick={reset}>Try again</Button>
        </div>
      </body>
    </html>
  );
}
