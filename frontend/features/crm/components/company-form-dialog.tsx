"use client";

import { useState, type ReactNode } from "react";
import { useForm, Controller } from "react-hook-form";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { COMPANY_TYPES, type Company, type CompanyInput } from "@/features/crm/api";
import { useCreateCompany, useUpdateCompany } from "@/features/crm/hooks";
import { buildCompanySchema, type CompanyFormValues } from "@/features/crm/schema";

interface CompanyFormDialogProps {
  company?: Company;
  trigger: ReactNode;
}

function toFormValues(company?: Company): CompanyFormValues {
  return {
    name: company?.name ?? "",
    domain: company?.domain ?? "",
    industry: company?.industry ?? "",
    type: (company?.type as CompanyFormValues["type"]) ?? "prospect",
    identificationNumber: company?.identificationNumber ?? "",
    taxIdentificationNumber: company?.taxIdentificationNumber ?? "",
    registeredAddress: company?.registeredAddress ?? "",
    city: company?.city ?? "",
    postalCode: company?.postalCode ?? "",
    country: company?.country ?? "",
    notes: company?.notes ?? "",
  };
}

export function CompanyFormDialog({ company, trigger }: CompanyFormDialogProps) {
  const t = useTranslations("crm");
  const [open, setOpen] = useState(false);
  const createMutation = useCreateCompany();
  const updateMutation = useUpdateCompany(company?.id ?? "");
  const isEdit = !!company;

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CompanyFormValues>({
    resolver: zodResolver(buildCompanySchema(t)),
    values: toFormValues(company),
  });

  const onSubmit = handleSubmit(async (values) => {
    const input: CompanyInput = {
      name: values.name.trim(),
      domain: values.domain?.trim() || null,
      industry: values.industry?.trim() || null,
      type: values.type,
      identificationNumber: values.identificationNumber?.trim() || null,
      taxIdentificationNumber: values.taxIdentificationNumber?.trim() || null,
      registeredAddress: values.registeredAddress?.trim() || null,
      city: values.city?.trim() || null,
      postalCode: values.postalCode?.trim() || null,
      country: values.country?.trim() || null,
      notes: values.notes?.trim() || null,
    };
    try {
      if (isEdit) {
        await updateMutation.mutateAsync(input);
      } else {
        await createMutation.mutateAsync(input);
      }
      reset(toFormValues(company));
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
            <DialogTitle>{isEdit ? t("companyForm.editTitle") : t("companyForm.createTitle")}</DialogTitle>
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
              <Label htmlFor="co-type">{t("companyForm.type")}</Label>
              <Controller
                control={control}
                name="type"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="co-type" className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {COMPANY_TYPES.map((type) => (
                        <SelectItem key={type} value={type}>
                          {t(`companyType.${type}`)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="co-ico">{t("companyForm.identificationNumber")}</Label>
                <Input id="co-ico" {...register("identificationNumber")} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="co-dic">{t("companyForm.taxIdentificationNumber")}</Label>
                <Input id="co-dic" {...register("taxIdentificationNumber")} />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="co-address">{t("companyForm.registeredAddress")}</Label>
              <Input id="co-address" {...register("registeredAddress")} />
            </div>
            <div className="grid grid-cols-3 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="co-city">{t("companyForm.city")}</Label>
                <Input id="co-city" {...register("city")} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="co-postalCode">{t("companyForm.postalCode")}</Label>
                <Input id="co-postalCode" {...register("postalCode")} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="co-country">{t("companyForm.country")}</Label>
                <Input id="co-country" {...register("country")} />
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
