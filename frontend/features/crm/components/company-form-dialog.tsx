"use client";

import { useState, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { type CompanyInput } from "@/features/crm/api";
import { useCreateCompany } from "@/features/crm/hooks";
import { buildCompanySchema, type CompanyFormValues } from "@/features/crm/schema";

interface CompanyFormDialogProps {
  trigger: ReactNode;
}

export function CompanyFormDialog({ trigger }: CompanyFormDialogProps) {
  const t = useTranslations("crm");
  const [open, setOpen] = useState(false);
  const createMutation = useCreateCompany();

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CompanyFormValues>({
    resolver: zodResolver(buildCompanySchema(t)),
    values: { name: "", domain: "", industry: "", notes: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    const input: CompanyInput = {
      name: values.name.trim(),
      domain: values.domain?.trim() || null,
      industry: values.industry?.trim() || null,
      notes: values.notes?.trim() || null,
    };
    try {
      await createMutation.mutateAsync(input);
      reset();
      setOpen(false);
    } catch (err: unknown) {
      if (err && typeof err === "object" && "fieldErrors" in err && err.fieldErrors) {
        const fieldErrors = err.fieldErrors as Record<string, string[]>;
        (Object.keys(fieldErrors) as (keyof CompanyFormValues)[]).forEach((field) => {
          setError(field, { message: fieldErrors[field]?.[0] });
        });
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={trigger as React.ReactElement} />
      <DialogContent>
        <form onSubmit={onSubmit} noValidate>
          <DialogHeader>
            <DialogTitle>{t("companyForm.createTitle")}</DialogTitle>
          </DialogHeader>

          <div className="space-y-3 py-4">
            <div className="space-y-1.5">
              <Label htmlFor="co-name">{t("companyForm.name")}</Label>
              <Input id="co-name" aria-invalid={!!errors.name} {...register("name")} />
              {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="co-domain">{t("companyForm.domain")}</Label>
                <Input id="co-domain" {...register("domain")} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="co-industry">{t("companyForm.industry")}</Label>
                <Input id="co-industry" {...register("industry")} />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="co-notes">{t("companyForm.notes")}</Label>
              <Textarea id="co-notes" rows={3} {...register("notes")} />
            </div>
          </div>

          <DialogFooter>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t("companyForm.saving") : t("companyForm.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
