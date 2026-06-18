"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { PlusIcon } from "lucide-react";
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
import { useProvisionTenant } from "@/features/platform/hooks";
import {
  provisionTenantSchema,
  type ProvisionTenantFormValues,
} from "@/features/platform/schema";

interface ProvisionTenantDialogProps {
  onProvisioned?: (tenantId: string) => void;
}

export function ProvisionTenantDialog({
  onProvisioned,
}: ProvisionTenantDialogProps) {
  const [open, setOpen] = useState(false);
  const mutation = useProvisionTenant();

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ProvisionTenantFormValues>({
    resolver: zodResolver(provisionTenantSchema),
    defaultValues: { name: "", subdomain: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    try {
      const result = await mutation.mutateAsync(values);
      reset();
      setOpen(false);
      onProvisioned?.(result.tenantId);
    } catch (err: unknown) {
      // Field errors from the API land here; let react-hook-form display them.
      if (
        err &&
        typeof err === "object" &&
        "fieldErrors" in err &&
        err.fieldErrors
      ) {
        const fieldErrors = err.fieldErrors as Record<string, string[]>;
        (Object.keys(fieldErrors) as (keyof ProvisionTenantFormValues)[]).forEach(
          (field) => {
            setError(field, { message: fieldErrors[field]?.[0] });
          },
        );
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger
        render={
          <Button size="sm">
            <PlusIcon className="h-3.5 w-3.5 mr-1.5" />
            Provision tenant
          </Button>
        }
      />
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>Provision new tenant</DialogTitle>
            <DialogDescription>
              Creates a new workspace with the default module entitlements
              (billing, notifications, files, operations, gdpr).
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            <div className="space-y-1.5">
              <Label htmlFor="pt-name">Organisation name</Label>
              <Input
                id="pt-name"
                placeholder="Acme Corp"
                autoComplete="off"
                aria-invalid={!!errors.name}
                {...register("name")}
              />
              {errors.name && (
                <p className="text-xs text-destructive">{errors.name.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="pt-subdomain">Subdomain</Label>
              <div className="flex items-center gap-1.5">
                <Input
                  id="pt-subdomain"
                  placeholder="acme"
                  autoComplete="off"
                  aria-invalid={!!errors.subdomain}
                  {...register("subdomain")}
                />
                <span className="shrink-0 text-sm text-muted-foreground">
                  .app
                </span>
              </div>
              {errors.subdomain && (
                <p className="text-xs text-destructive">
                  {errors.subdomain.message}
                </p>
              )}
            </div>
          </div>

          <DialogFooter>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Provisioning…" : "Provision"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
