"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { ZapIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { devQuickLogin } from "@/features/auth/dev-actions";

interface DevAccount {
  label: string;
  email: string;
  password: string;
  displayName: string;
}

// Fixed dev accounts. A first click registers the account (its own tenant with the default
// module entitlements); subsequent clicks just log in. Passwords meet the backend policy.
const DEV_ACCOUNTS: DevAccount[] = [
  { label: "Acme Founder", email: "founder@acme.test", password: "Acme1234!secure", displayName: "Acme Founder" },
  { label: "Demo User", email: "demo.user@acme.test", password: "Demo1234!secure", displayName: "Demo User" },
];

/** Dev-only convenience: one-click sign-in for fixed demo accounts. */
export function DevQuickLogin() {
  const router = useRouter();
  const t = useTranslations("auth");
  const [isPending, startTransition] = useTransition();
  const [busyEmail, setBusyEmail] = useState<string | null>(null);

  function quickLogin(account: DevAccount) {
    setBusyEmail(account.email);
    startTransition(async () => {
      const result = await devQuickLogin(account.email, account.password, account.displayName);
      setBusyEmail(null);
      if (result.ok) {
        toast.success(t("dev.signedInAs", { label: account.label }));
        router.push("/");
        router.refresh();
      } else {
        toast.error(t("dev.failed", { label: account.label }));
      }
    });
  }

  return (
    <div className="mt-4 rounded-lg border border-dashed border-border p-3">
      <p className="mb-2 flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        <ZapIcon className="h-3.5 w-3.5" aria-hidden="true" />
        {t("dev.heading")}
      </p>
      <div className="grid gap-2 sm:grid-cols-2">
        {DEV_ACCOUNTS.map((account) => (
          <Button
            key={account.email}
            type="button"
            variant="outline"
            size="sm"
            disabled={isPending}
            onClick={() => quickLogin(account)}
          >
            {busyEmail === account.email ? t("dev.signingIn") : account.label}
          </Button>
        ))}
      </div>
    </div>
  );
}
