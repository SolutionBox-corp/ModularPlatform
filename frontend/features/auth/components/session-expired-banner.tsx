"use client";

import { useSearchParams } from "next/navigation";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { InfoIcon } from "lucide-react";

/**
 * Reads ?reason=expired from the URL and renders a banner when present.
 * Rendered on the login page as a server-component child; must be "use client"
 * because useSearchParams is a client-only hook.
 */
export function SessionExpiredBanner() {
  const params = useSearchParams();
  if (params.get("reason") !== "expired") return null;

  return (
    <Alert>
      <InfoIcon />
      <AlertDescription>
        Your session expired. Please sign in again.
      </AlertDescription>
    </Alert>
  );
}
