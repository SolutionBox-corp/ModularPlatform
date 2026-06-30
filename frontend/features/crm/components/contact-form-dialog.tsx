"use client";

import { useState, type ReactNode } from "react";
import { useForm, Controller } from "react-hook-form";
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
import { CONTACT_STATUSES, crmQueries, type Contact, type ContactInput } from "@/features/crm/api";
import { useCreateContact, useUpdateContact } from "@/features/crm/hooks";
import { buildContactSchema, type ContactFormValues } from "@/features/crm/schema";

interface ContactFormDialogProps {
  /** When set, the dialog edits this contact; otherwise it creates a new one. */
  contact?: Contact;
  trigger: ReactNode;
  onSaved?: (id: string) => void;
}

function toFormValues(contact?: Contact): ContactFormValues {
  return {
    firstName: contact?.firstName ?? "",
    lastName: contact?.lastName ?? "",
    companyId: contact?.companyId ?? "none",
    email: contact?.email ?? "",
    phone: contact?.phone ?? "",
    position: contact?.position ?? "",
    notes: contact?.notes ?? "",
    status: (contact?.status as ContactFormValues["status"]) ?? "new",
    tags: contact?.tags.join(", ") ?? "",
  };
}

function toInput(values: ContactFormValues): ContactInput {
  return {
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    companyId: values.companyId === "none" ? null : values.companyId,
    email: values.email?.trim() || null,
    phone: values.phone?.trim() || null,
    position: values.position?.trim() || null,
    notes: values.notes?.trim() || null,
    status: values.status,
    tags:
      values.tags
        ?.split(",")
        .map((s) => s.trim())
        .filter((s) => s.length > 0) ?? [],
  };
}

export function ContactFormDialog({ contact, trigger, onSaved }: ContactFormDialogProps) {
  const t = useTranslations("crm");
  const [open, setOpen] = useState(false);
  const createMutation = useCreateContact();
  const updateMutation = useUpdateContact(contact?.id ?? "");
  const isEdit = !!contact;
  const { data: companies } = useQuery(crmQueries.companies({ page: 1, pageSize: 100 }));
  const companyOptions = companies?.items ?? [];

  const companyLabel = (companyId: string | undefined) => {
    if (!companyId || companyId === "none") return t("contactForm.noCompany");
    return companyOptions.find((company) => company.id === companyId)?.name
      ?? (contact?.companyId === companyId ? contact.companyName : undefined)
      ?? t("contactForm.companyUnknown");
  };

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<ContactFormValues>({
    resolver: zodResolver(buildContactSchema(t)),
    values: toFormValues(contact),
  });

  const onSubmit = handleSubmit(async (values) => {
    try {
      const input = toInput(values);
      const result = isEdit
        ? { id: (await updateMutation.mutateAsync(input)).id }
        : await createMutation.mutateAsync(input);
      reset(toFormValues(contact));
      setOpen(false);
      onSaved?.(result.id);
    } catch (err: unknown) {
      if (err && typeof err === "object" && "fieldErrors" in err && err.fieldErrors) {
        const fieldErrors = err.fieldErrors as Record<string, string[]>;
        (Object.keys(fieldErrors) as (keyof ContactFormValues)[]).forEach((field) => {
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
            <DialogTitle>{isEdit ? t("contactForm.editTitle") : t("contactForm.createTitle")}</DialogTitle>
          </DialogHeader>

          <div className="grid grid-cols-2 gap-3 py-4">
            <div className="space-y-1.5 col-span-2">
              <Label htmlFor="c-company">{t("contactForm.company")}</Label>
              <Controller
                control={control}
                name="companyId"
                render={({ field }) => (
                  <Select value={field.value || "none"} onValueChange={field.onChange}>
                    <SelectTrigger id="c-company">
                      <span data-slot="select-value" className="flex flex-1 text-left">
                        {companyLabel(field.value)}
                      </span>
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="none">{t("contactForm.noCompany")}</SelectItem>
                      {companyOptions.map((company) => (
                        <SelectItem key={company.id} value={company.id}>
                          {company.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="c-firstName">{t("contactForm.firstName")}</Label>
              <Input id="c-firstName" aria-invalid={!!errors.firstName} {...register("firstName")} />
              {errors.firstName && <p className="text-xs text-destructive">{errors.firstName.message}</p>}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="c-lastName">{t("contactForm.lastName")}</Label>
              <Input id="c-lastName" aria-invalid={!!errors.lastName} {...register("lastName")} />
              {errors.lastName && <p className="text-xs text-destructive">{errors.lastName.message}</p>}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="c-email">{t("contactForm.email")}</Label>
              <Input id="c-email" type="email" aria-invalid={!!errors.email} {...register("email")} />
              {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="c-phone">{t("contactForm.phone")}</Label>
              <Input id="c-phone" {...register("phone")} />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="c-position">{t("contactForm.position")}</Label>
              <Input id="c-position" {...register("position")} />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="c-status">{t("contactForm.status")}</Label>
              <Controller
                control={control}
                name="status"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="c-status">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {CONTACT_STATUSES.map((s) => (
                        <SelectItem key={s} value={s}>
                          {t(`status.${s}`)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="c-tags">{t("contactForm.tags")}</Label>
              <Input id="c-tags" placeholder={t("contactForm.tagsPlaceholder")} {...register("tags")} />
            </div>

            <div className="space-y-1.5 col-span-2">
              <Label htmlFor="c-notes">{t("contactForm.notes")}</Label>
              <Textarea id="c-notes" rows={3} {...register("notes")} />
            </div>
          </div>

          <DialogFooter>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t("contactForm.saving") : t("contactForm.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
