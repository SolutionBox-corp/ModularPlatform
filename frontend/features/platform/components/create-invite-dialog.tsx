"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { MailPlusIcon, CopyIcon, CheckIcon } from "lucide-react";
import { toast } from "sonner";
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
import { useCreateTenantInvite } from "@/features/platform/hooks";
import {
  createInviteSchema,
  type CreateInviteFormValues,
} from "@/features/platform/schema";

interface CreateInviteDialogProps {
  tenantId: string;
  tenantName?: string;
}

export function CreateInviteDialog({
  tenantId,
  tenantName,
}: CreateInviteDialogProps) {
  const [open, setOpen] = useState(false);
  const [token, setToken] = useState<string | null>(null);
  const [expiresAt, setExpiresAt] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const mutation = useCreateTenantInvite();

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateInviteFormValues>({
    resolver: zodResolver(createInviteSchema),
    defaultValues: { expiresInDays: 7 },
  });

  function handleOpenChange(next: boolean) {
    setOpen(next);
    if (!next) {
      reset();
      setToken(null);
      setExpiresAt(null);
      setCopied(false);
    }
  }

  const onSubmit = handleSubmit(async (values) => {
    const result = await mutation.mutateAsync({
      tenantId,
      expiresInDays: values.expiresInDays,
    });
    setToken(result.inviteToken);
    setExpiresAt(result.expiresAt);
  });

  async function handleCopy() {
    if (!token) return;
    await navigator.clipboard.writeText(token);
    setCopied(true);
    toast.success("Invite token copied to clipboard.");
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger
        render={
          <Button variant="outline" size="sm">
            <MailPlusIcon className="h-3.5 w-3.5 mr-1.5" />
            Create invite
          </Button>
        }
      />
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Create invite token</DialogTitle>
          <DialogDescription>
            Mint a single-use invite so a new member can join{" "}
            {tenantName ? (
              <strong>{tenantName}</strong>
            ) : (
              "this workspace"
            )}
            . The raw token is shown once — copy it before closing.
          </DialogDescription>
        </DialogHeader>

        {token ? (
          <div className="space-y-4 py-2">
            <div className="space-y-1.5">
              <Label>Invite token</Label>
              <div className="flex items-center gap-2">
                <Input
                  readOnly
                  value={token}
                  className="font-mono text-xs"
                  aria-label="Invite token"
                />
                <Button
                  type="button"
                  variant="outline"
                  size="icon-sm"
                  onClick={handleCopy}
                  aria-label="Copy token"
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
                Expires{" "}
                {new Date(expiresAt).toLocaleDateString("en", {
                  month: "short",
                  day: "numeric",
                  year: "numeric",
                })}
              </p>
            )}
            <DialogFooter showCloseButton />
          </div>
        ) : (
          <form onSubmit={onSubmit} noValidate>
            <div className="space-y-4 py-4">
              <div className="space-y-1.5">
                <Label htmlFor="ci-days">Expires in (days)</Label>
                <Input
                  id="ci-days"
                  type="number"
                  min={1}
                  max={90}
                  aria-invalid={!!errors.expiresInDays}
                  {...register("expiresInDays", { valueAsNumber: true })}
                />
                {errors.expiresInDays && (
                  <p className="text-xs text-destructive">
                    {errors.expiresInDays.message}
                  </p>
                )}
              </div>
            </div>

            <DialogFooter>
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? "Creating…" : "Create token"}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
