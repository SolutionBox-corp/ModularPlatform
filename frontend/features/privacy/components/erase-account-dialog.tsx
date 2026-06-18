"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
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
  const router = useRouter();
  const { mutate, isPending } = useEraseAccount();
  const [open, setOpen] = useState(false);
  const [confirmValue, setConfirmValue] = useState("");

  const canSubmit = confirmValue.trim().toLowerCase() === CONFIRMATION_PHRASE;

  function handleErase() {
    if (!canSubmit) return;
    mutate(void 0, {
      onSuccess: () => {
        toast.success("Account erasure has been queued. You will be signed out.");
        setOpen(false);
        // Redirect to login — the session will be invalidated by the Worker.
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
            Delete my account
          </Button>
        }
      />

      <DialogContent>
        <DialogHeader>
          <DialogTitle>Delete account permanently</DialogTitle>
          <DialogDescription>
            This is <strong>irreversible</strong>. All personal data will be
            erased from every module. Audit logs will be anonymised and your
            encryption key will be destroyed, making stored data permanently
            unrecoverable.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2">
            <p className="text-xs text-destructive">
              What will be erased: your profile, consents, notification
              history, billing records, and any files you own. This cannot be
              undone.
            </p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="erase-confirm" className="text-sm">
              To confirm, type{" "}
              <span className="font-medium text-foreground">
                {CONFIRMATION_PHRASE}
              </span>{" "}
              below:
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
              Type exactly: {CONFIRMATION_PHRASE}
            </p>
          </div>
        </div>

        <DialogFooter>
          <Button
            variant="destructive"
            onClick={handleErase}
            disabled={!canSubmit || isPending}
          >
            {isPending ? "Erasing…" : "Permanently delete my account"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
