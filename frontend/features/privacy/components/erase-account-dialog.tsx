"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { TriangleAlertIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { useEraseAccount } from "@/features/privacy/hooks";
import { logoutAction } from "@/features/auth/actions";

const CONFIRMATION_PHRASE = "delete my account";

/**
 * EraseAccountDialog
 *
 * Destructive action: POST /gdpr/me/erase. The backend fans out a
 * UserErasureRequested integration event to all modules, then crypto-shreds
 * the subject key so stored PII ciphertext becomes permanently unrecoverable.
 *
 * Requires typing the exact phrase "delete my account" to confirm — a friction
 * gate against accidental clicks.
 */
export function EraseAccountDialog() {
  const t = useTranslations("privacy");
  const router = useRouter();
  const { mutate, isPending } = useEraseAccount();
  const [open, setOpen] = useState(false);
  const [confirmValue, setConfirmValue] = useState("");

  const canSubmit = confirmValue.trim().toLowerCase() === CONFIRMATION_PHRASE;

  function handleErase() {
    if (!canSubmit) return;
    mutate(void 0, {
      onSuccess: async () => {
        toast.success(t("erase.toast.queued"));
        setOpen(false);
        // Destroy the iron-session so the user is no longer technically logged in,
        // then redirect to login.
        await logoutAction();
        router.push("/login?reason=erased");
      },
    });
  }

  function handleOpenChange(next: boolean) {
    if (!next) {
      setConfirmValue("");
    }
    setOpen(next);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger
        render={
          <Button variant="destructive" className="gap-1.5">
            <TriangleAlertIcon className="h-4 w-4" aria-hidden />
            {t("erase.dialog.trigger")}
          </Button>
        }
      />

      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("erase.dialog.title")}</DialogTitle>
          <DialogDescription>
            {t.rich("erase.dialog.description", {
              strong: (chunks) => <strong>{chunks}</strong>,
            })}
          </DialogDescription>
        </DialogHeader>

        <form
          onSubmit={(e) => {
            e.preventDefault();
            handleErase();
          }}
          className="space-y-3 py-1"
        >
          <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2">
            <p className="text-xs text-destructive">
              {t("erase.dialog.whatWillBeErased")}
            </p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="erase-confirm" className="text-sm">
              {t.rich("erase.dialog.confirmLabel", {
                phrase: CONFIRMATION_PHRASE,
                strong: (chunks) => (
                  <span className="font-medium text-foreground">{chunks}</span>
                ),
              })}
            </Label>
            <Input
              id="erase-confirm"
              value={confirmValue}
              onChange={(e) => setConfirmValue(e.target.value)}
              placeholder={CONFIRMATION_PHRASE}
              autoComplete="off"
              aria-describedby="erase-confirm-hint"
              disabled={isPending}
            />
            <p id="erase-confirm-hint" className="text-xs text-muted-foreground">
              {t("erase.dialog.confirmHint", { phrase: CONFIRMATION_PHRASE })}
            </p>
          </div>

          <DialogFooter>
            <Button
              type="submit"
              variant="destructive"
              disabled={!canSubmit || isPending}
            >
              {isPending ? t("erase.dialog.erasing") : t("erase.dialog.submit")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
