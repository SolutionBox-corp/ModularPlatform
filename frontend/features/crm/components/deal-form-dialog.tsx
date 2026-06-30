"use client";

import { useState, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
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
import { crmQueries, DEAL_STAGES, type DealInput } from "@/features/crm/api";
import { useCreateDeal } from "@/features/crm/hooks";
import { buildDealSchema, type DealFormValues } from "@/features/crm/schema";

interface DealFormDialogProps {
  contactId?: string | null;
  trigger: ReactNode;
}

function toLocalDate(iso: string | undefined | null): string {
  return iso ? new Date(iso).toISOString().slice(0, 10) : "";
}

export function DealFormDialog({ contactId, trigger }: DealFormDialogProps) {
  const t = useTranslations("crm");
  const [open, setOpen] = useState(false);
  const [stage, setStage] = useState<(typeof DEAL_STAGES)[number]>("lead");
  const [companyId, setCompanyId] = useState("none");
  const createMutation = useCreateDeal();
  const { data: companies } = useQuery(crmQueries.companies({ page: 1, pageSize: 100 }));
  const companyOptions = companies?.items ?? [];

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<DealFormValues>({
    resolver: zodResolver(buildDealSchema(t)),
    values: {
      title: "",
      amount: 0,
      currency: "USD",
      stage,
      probabilityPercent: 10,
      leadSource: "",
      expectedCloseAt: "",
      nextStep: "",
      notes: "",
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    const input: DealInput = {
      contactId: contactId ?? null,
      companyId: companyId === "none" ? null : companyId,
      title: values.title.trim(),
      amountCents: Math.round(values.amount * 100),
      currency: values.currency.trim().toUpperCase(),
      stage,
      probabilityPercent: values.probabilityPercent,
      leadSource: values.leadSource?.trim().toLowerCase() || null,
      expectedCloseAt: values.expectedCloseAt ? new Date(values.expectedCloseAt).toISOString() : null,
      nextStep: values.nextStep?.trim() || null,
      notes: values.notes?.trim() || null,
    };
    try {
      await createMutation.mutateAsync(input);
      reset();
      setCompanyId("none");
      setOpen(false);
    } catch (err: unknown) {
      if (err && typeof err === "object" && "fieldErrors" in err && err.fieldErrors) {
        const fieldErrors = err.fieldErrors as Record<string, string[]>;
        (Object.keys(fieldErrors) as (keyof DealFormValues)[]).forEach((field) => {
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
            <DialogTitle>{t("dealForm.createTitle")}</DialogTitle>
          </DialogHeader>

          <div className="space-y-3 py-4">
            <div className="space-y-1.5">
              <Label htmlFor="d-title">{t("dealForm.title")}</Label>
              <Input id="d-title" aria-invalid={!!errors.title} {...register("title")} />
              {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
            </div>

            <div className="grid grid-cols-3 gap-3">
              <div className="col-span-2 space-y-1.5">
                <Label htmlFor="d-amount">{t("dealForm.amount")}</Label>
                <Input
                  id="d-amount"
                  type="number"
                  min={0}
                  step="0.01"
                  aria-invalid={!!errors.amount}
                  {...register("amount", { valueAsNumber: true })}
                />
                {errors.amount && <p className="text-xs text-destructive">{errors.amount.message}</p>}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="d-currency">{t("dealForm.currency")}</Label>
                <Input id="d-currency" maxLength={3} {...register("currency")} />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label>{t("dealForm.stage")}</Label>
                <Select value={stage} onValueChange={(v) => setStage((v ?? "lead") as (typeof DEAL_STAGES)[number])}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {DEAL_STAGES.map((s) => (
                      <SelectItem key={s} value={s}>
                        {t(`dealStage.${s}`)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="d-close">{t("dealForm.expectedCloseAt")}</Label>
                <Input id="d-close" type="date" defaultValue={toLocalDate("")} {...register("expectedCloseAt")} />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="d-probability">{t("dealForm.probability")}</Label>
                <Input
                  id="d-probability"
                  type="number"
                  min={0}
                  max={100}
                  aria-invalid={!!errors.probabilityPercent}
                  {...register("probabilityPercent", { valueAsNumber: true })}
                />
                {errors.probabilityPercent && (
                  <p className="text-xs text-destructive">{errors.probabilityPercent.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="d-source">{t("dealForm.leadSource")}</Label>
                <Input id="d-source" {...register("leadSource")} />
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="d-company">{t("dealForm.company")}</Label>
              <Select value={companyId} onValueChange={(value) => setCompanyId(value ?? "none")}>
                <SelectTrigger id="d-company" className="w-full">
                  <span data-slot="select-value" className="flex flex-1 text-left">
                    {companyId === "none"
                      ? t("dealForm.noCompany")
                      : companyOptions.find((company) => company.id === companyId)?.name ?? t("contactForm.companyUnknown")}
                  </span>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">{t("dealForm.noCompany")}</SelectItem>
                  {companyOptions.map((company) => (
                    <SelectItem key={company.id} value={company.id}>
                      {company.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="d-next-step">{t("dealForm.nextStep")}</Label>
              <Input id="d-next-step" {...register("nextStep")} />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="d-notes">{t("dealForm.notes")}</Label>
              <Textarea id="d-notes" rows={3} {...register("notes")} />
            </div>
          </div>

          <DialogFooter>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t("dealForm.saving") : t("dealForm.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
