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
  contactDisplayName,
  crmQueries,
  type Meeting,
  type MeetingInput,
} from "@/features/crm/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
} from "@/components/ui/select";
import { useCreateMeeting, useUpdateMeeting } from "@/features/crm/hooks";
import { buildMeetingSchema, type MeetingFormValues } from "@/features/crm/schema";

interface MeetingFormDialogProps {
  meeting?: Meeting;
  /** Pre-links the meeting to a contact (e.g. scheduling from the contact detail page). */
  contactId?: string | null;
  /** When set, contact choices are limited to that company's contacts. */
  companyId?: string;
  /** When set, new meetings are linked to this deal hub. */
  dealId?: string;
  trigger: ReactNode;
}

/** ISO string → value for <input type="datetime-local"> in the browser's local zone. */
function toLocalInput(iso: string | undefined): string {
  if (!iso) return "";
  const d = new Date(iso);
  const off = d.getTimezoneOffset();
  const local = new Date(d.getTime() - off * 60_000);
  return local.toISOString().slice(0, 16);
}

function defaultValues(meeting?: Meeting, contactId?: string | null): MeetingFormValues {
  return {
    contactId: meeting?.contactId ?? contactId ?? "",
    title: meeting?.title ?? "",
    scheduledAt: toLocalInput(meeting?.scheduledAt),
    durationMinutes: meeting?.durationMinutes ?? 30,
    location: meeting?.location ?? "",
    notes: meeting?.notes ?? "",
  };
}

export function MeetingFormDialog({ meeting, contactId, companyId, dealId, trigger }: MeetingFormDialogProps) {
  const t = useTranslations("crm");
  const [open, setOpen] = useState(false);
  const createMutation = useCreateMeeting();
  const updateMutation = useUpdateMeeting(meeting?.id ?? "");
  const isEdit = !!meeting;
  const contactLocked = !!contactId || isEdit;
  const { data: contacts } = useQuery(crmQueries.contacts({ page: 1, pageSize: 100, companyId }));
  const contactOptions = contacts?.items ?? [];

  const selectedContactLabel = (selectedContactId: string | undefined) => {
    if (!selectedContactId) return t("meetingForm.contactPlaceholder");
    const selected = contactOptions.find((contact) => contact.id === selectedContactId);
    return selected ? contactDisplayName(selected) : t("meetingForm.contactUnknown");
  };

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<MeetingFormValues>({
    resolver: zodResolver(buildMeetingSchema(t)),
    values: defaultValues(meeting, contactId),
  });

  const onSubmit = handleSubmit(async (values) => {
    const input: MeetingInput = {
      contactId: meeting?.contactId ?? contactId ?? values.contactId,
      dealId: meeting?.dealId ?? dealId ?? null,
      title: values.title.trim(),
      scheduledAt: new Date(values.scheduledAt).toISOString(),
      durationMinutes: values.durationMinutes,
      location: values.location?.trim() || null,
      notes: values.notes?.trim() || null,
    };
    try {
      if (isEdit) {
        await updateMutation.mutateAsync(input);
      } else {
        await createMutation.mutateAsync(input);
      }
      reset(defaultValues(meeting, contactId));
      setOpen(false);
    } catch (err: unknown) {
      if (err && typeof err === "object" && "fieldErrors" in err && err.fieldErrors) {
        const fieldErrors = err.fieldErrors as Record<string, string[]>;
        (Object.keys(fieldErrors) as (keyof MeetingFormValues)[]).forEach((field) => {
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
            <DialogTitle>{isEdit ? t("meetingForm.editTitle") : t("meetingForm.createTitle")}</DialogTitle>
          </DialogHeader>

          <div className="space-y-3 py-4">
            {!contactLocked && (
              <div className="space-y-1.5">
                <Label htmlFor="m-contact">{t("meetingForm.contact")}</Label>
                <Controller
                  control={control}
                  name="contactId"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger id="m-contact" aria-invalid={!!errors.contactId}>
                        <span data-slot="select-value" className="flex flex-1 text-left">
                          {selectedContactLabel(field.value)}
                        </span>
                      </SelectTrigger>
                      <SelectContent>
                        {contactOptions.map((contact) => (
                          <SelectItem key={contact.id} value={contact.id}>
                            {contactDisplayName(contact)}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                />
                {errors.contactId && <p className="text-xs text-destructive">{errors.contactId.message}</p>}
              </div>
            )}

            <div className="space-y-1.5">
              <Label htmlFor="m-title">{t("meetingForm.title")}</Label>
              <Input id="m-title" aria-invalid={!!errors.title} {...register("title")} />
              {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label htmlFor="m-when">{t("meetingForm.scheduledAt")}</Label>
                <Input
                  id="m-when"
                  type="datetime-local"
                  aria-invalid={!!errors.scheduledAt}
                  {...register("scheduledAt")}
                />
                {errors.scheduledAt && (
                  <p className="text-xs text-destructive">{errors.scheduledAt.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="m-duration">{t("meetingForm.duration")}</Label>
                <Input
                  id="m-duration"
                  type="number"
                  min={1}
                  max={1440}
                  aria-invalid={!!errors.durationMinutes}
                  {...register("durationMinutes", { valueAsNumber: true })}
                />
                {errors.durationMinutes && (
                  <p className="text-xs text-destructive">{errors.durationMinutes.message}</p>
                )}
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="m-location">{t("meetingForm.location")}</Label>
              <Input id="m-location" {...register("location")} />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="m-notes">{t("meetingForm.notes")}</Label>
              <Textarea id="m-notes" rows={3} {...register("notes")} />
            </div>
          </div>

          <DialogFooter>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? t("meetingForm.saving") : t("meetingForm.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
