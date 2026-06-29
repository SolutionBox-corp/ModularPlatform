import { z } from "zod";
import { CONTACT_STATUSES, INTERACTION_TYPES, DEAL_STAGES } from "@/features/crm/api";

/** Translator shape (next-intl's `useTranslations('crm')`) — only what the schema needs. */
type Translate = (key: string) => string;

/** Mirrors CreateContactValidator / UpdateContactValidator (ModularPlatform.Crm). */
export function buildContactSchema(t: Translate) {
  return z.object({
    fullName: z
      .string()
      .min(1, t("validation.fullNameRequired"))
      .max(256, t("validation.fullNameMax")),
    email: z
      .string()
      .max(256, t("validation.emailMax"))
      .email(t("validation.emailInvalid"))
      .or(z.literal(""))
      .optional(),
    phone: z.string().max(64, t("validation.phoneMax")).optional(),
    company: z.string().max(256, t("validation.companyMax")).optional(),
    position: z.string().max(256, t("validation.positionMax")).optional(),
    notes: z.string().max(8192, t("validation.notesMax")).optional(),
    status: z.enum(CONTACT_STATUSES),
    tags: z.string().max(512, t("validation.tagsMax")).optional(),
  });
}

export type ContactFormValues = z.infer<ReturnType<typeof buildContactSchema>>;

/** Mirrors CreateMeetingValidator / UpdateMeetingValidator. */
export function buildMeetingSchema(t: Translate) {
  return z.object({
    title: z
      .string()
      .min(1, t("validation.titleRequired"))
      .max(256, t("validation.titleMax")),
    scheduledAt: z.string().min(1, t("validation.scheduledAtRequired")),
    durationMinutes: z
      .number({ error: t("validation.durationInvalid") })
      .int(t("validation.durationInvalid"))
      .min(1, t("validation.durationInvalid"))
      .max(1440, t("validation.durationInvalid")),
    location: z.string().max(512, t("validation.locationMax")).optional(),
    notes: z.string().max(8192, t("validation.notesMax")).optional(),
  });
}

export type MeetingFormValues = z.infer<ReturnType<typeof buildMeetingSchema>>;

export function buildInteractionSchema(t: Translate) {
  return z.object({
    type: z.enum(INTERACTION_TYPES),
    body: z.string().max(8192, t("validation.bodyMax")).optional(),
  });
}

export type InteractionFormValues = z.infer<ReturnType<typeof buildInteractionSchema>>;

/** Mirrors CreateDealValidator / UpdateDealValidator. Amount is entered in major units, sent as cents. */
export function buildDealSchema(t: Translate) {
  return z.object({
    title: z.string().min(1, t("validation.titleRequired")).max(256, t("validation.titleMax")),
    amount: z.number({ error: t("validation.amountInvalid") }).min(0, t("validation.amountInvalid")),
    currency: z.string().length(3, t("validation.currencyInvalid")),
    stage: z.enum(DEAL_STAGES),
    expectedCloseAt: z.string().optional(),
    notes: z.string().max(8192, t("validation.notesMax")).optional(),
  });
}

export type DealFormValues = z.infer<ReturnType<typeof buildDealSchema>>;
