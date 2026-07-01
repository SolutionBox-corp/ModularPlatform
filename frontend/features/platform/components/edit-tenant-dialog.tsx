"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { PencilIcon } from "lucide-react";
import { useTranslations } from "next-intl";
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
import { useUpdateTenant } from "@/features/platform/hooks";
import {
  buildProvisionTenantSchema,
  type ProvisionTenantFormValues,
} from "@/features/platform/schema";

interface EditTenantDialogProps {
  tenantId: string;
  name: string;
  subdomain: string;
}

export function EditTenantDialog({
  tenantId,
  name,
  subdomain,
}: EditTenantDialogProps) {
  const t = useTranslations("platform");
  const [open, setOpen] = useState(false);
  const mutation = useUpdateTenant();

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ProvisionTenantFormValues>({
    resolver: zodResolver(buildProvisionTenantSchema(t)),
    defaultValues: { name, subdomain },
  });

  useEffect(() => {
    if (open) {
      reset({ name, subdomain });
    }
  }, [name, open, reset, subdomain]);

  const onSubmit = handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync({ tenantId, ...values });
      setOpen(false);
    } catch (err: unknown) {
      if (
        err &&
        typeof err === "object" &&
        "fieldErrors" in err &&
        err.fieldErrors
      ) {
        const fieldErrors = err.fieldErrors as Record<string, string[]>;
        (Object.keys(fieldErrors) as (keyof ProvisionTenantFormValues)[]).forEach(
          (field) => setError(field, { message: fieldErrors[field]?.[0] }),
        );
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger
        render={
          <Button variant="outline" size="sm">
            <PencilIcon className="h-3.5 w-3.5 mr-1.5" />
            {t("editTenantDialog.trigger")}
          </Button>
        }
      />
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>{t("editTenantDialog.title")}</DialogTitle>
            <DialogDescription>
              {t("editTenantDialog.description")}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-4">
            <div className="space-y-1.5">
              <Label htmlFor="et-name">{t("editTenantDialog.nameLabel")}</Label>
              <Input
                id="et-name"
                autoComplete="off"
                aria-invalid={!!errors.name}
                aria-describedby={errors.name ? "et-name-error" : undefined}
                {...register("name")}
              />
              {errors.name && (
                <p id="et-name-error" className="text-xs text-destructive">
                  {errors.name.message}
                </p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="et-subdomain">
                {t("editTenantDialog.subdomainLabel")}
              </Label>
              <div className="flex items-center gap-1.5">
                <Input
                  id="et-subdomain"
                  autoComplete="off"
                  aria-invalid={!!errors.subdomain}
                  aria-describedby={errors.subdomain ? "et-subdomain-error" : undefined}
                  {...register("subdomain")}
                />
                <span className="shrink-0 text-sm text-muted-foreground">
                  .app
                </span>
              </div>
              {errors.subdomain && (
                <p id="et-subdomain-error" className="text-xs text-destructive">
                  {errors.subdomain.message}
                </p>
              )}
            </div>
          </div>

          <DialogFooter>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting
                ? t("editTenantDialog.submitting")
                : t("editTenantDialog.submit")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
