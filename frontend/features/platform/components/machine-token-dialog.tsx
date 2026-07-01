"use client";

import { useState, type FormEvent } from "react";
import { CheckIcon, CopyIcon, KeyRoundIcon } from "lucide-react";
import { toast } from "sonner";
import { useLocale, useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useIssueMachineToken } from "@/features/platform/hooks";

interface MachineTokenDialogProps {
  tenantId: string;
  tenantName?: string;
}

export function MachineTokenDialog({
  tenantId,
  tenantName,
}: MachineTokenDialogProps) {
  const t = useTranslations("platform");
  const locale = useLocale();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [token, setToken] = useState<string | null>(null);
  const [expiresAt, setExpiresAt] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const mutation = useIssueMachineToken();

  function handleOpenChange(next: boolean) {
    setOpen(next);
    if (!next) {
      setName("");
      setToken(null);
      setExpiresAt(null);
      setCopied(false);
      mutation.reset();
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;

    const result = await mutation.mutateAsync({ tenantId, name: trimmed });
    setToken(result.accessToken);
    setExpiresAt(result.expiresAt);
  }

  async function handleCopy() {
    if (!token) return;
    await navigator.clipboard.writeText(token);
    setCopied(true);
    toast.success(t("machineTokenDialog.copied"));
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger
        render={
          <Button variant="outline" size="sm">
            <KeyRoundIcon className="h-3.5 w-3.5 mr-1.5" />
            {t("machineTokenDialog.trigger")}
          </Button>
        }
      />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("machineTokenDialog.title")}</DialogTitle>
          <DialogDescription>
            {tenantName
              ? t.rich("machineTokenDialog.descriptionNamed", {
                  tenant: () => <strong>{tenantName}</strong>,
                })
              : t("machineTokenDialog.descriptionFallback")}
          </DialogDescription>
        </DialogHeader>

        {token ? (
          <div className="space-y-4 py-2">
            <div className="space-y-1.5">
              <Label>{t("machineTokenDialog.tokenLabel")}</Label>
              <div className="flex items-center gap-2">
                <Input
                  readOnly
                  value={token}
                  className="font-mono text-xs"
                  aria-label={t("machineTokenDialog.tokenAria")}
                />
                <Button
                  type="button"
                  variant="outline"
                  size="icon-sm"
                  onClick={handleCopy}
                  aria-label={t("machineTokenDialog.copyAria")}
                >
                  {copied ? (
                    <CheckIcon className="h-3.5 w-3.5 text-success" />
                  ) : (
                    <CopyIcon className="h-3.5 w-3.5" />
                  )}
                </Button>
              </div>
            </div>
            {expiresAt && (
              <p className="text-xs text-muted-foreground">
                {t("machineTokenDialog.expires", {
                  date: new Date(expiresAt).toLocaleString(locale, {
                    month: "short",
                    day: "numeric",
                    year: "numeric",
                    hour: "2-digit",
                    minute: "2-digit",
                  }),
                })}
              </p>
            )}
            <p className="text-xs text-muted-foreground">
              {t("machineTokenDialog.oneTimeHint")}
            </p>
            <DialogFooter showCloseButton />
          </div>
        ) : (
          <form onSubmit={handleSubmit} noValidate>
            <div className="space-y-4 py-4">
              <div className="space-y-1.5">
                <Label htmlFor="machine-token-name">
                  {t("machineTokenDialog.nameLabel")}
                </Label>
                <Input
                  id="machine-token-name"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  placeholder={t("machineTokenDialog.namePlaceholder")}
                  maxLength={128}
                  aria-invalid={mutation.isError}
                />
                <p className="text-xs text-muted-foreground">
                  {t("machineTokenDialog.nameHint")}
                </p>
              </div>
            </div>

            <DialogFooter>
              <Button
                type="submit"
                disabled={mutation.isPending || name.trim().length === 0}
              >
                {mutation.isPending
                  ? t("machineTokenDialog.submitting")
                  : t("machineTokenDialog.submit")}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
