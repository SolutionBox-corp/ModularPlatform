import { z } from "zod";
import { CONTACT_STATUSES, INTERACTION_TYPES, DEAL_STAGES, TASK_PRIORITIES, COMPANY_TYPES } from "@/features/crm/api";

/** Translator shape (next-intl's `useTranslations('crm')`) — only what the schema needs. */
type Translate = (key: string) => string;

/** Mirrors CreateContactValidator / UpdateContactValidator (ModularPlatform.Crm). */
export function buildContactSchema(t: Translate) {
  return z.object({
    firstName: z
      .string()
      .min(1, t("validation.firstNameRequired"))
      .max(128, t("validation.firstNameMax")),
    lastName: z
      .string()
      .min(1, t("validation.lastNameRequired"))
      .max(128, t("validation.lastNameMax")),
    companyId: z.string().optional(),
    email: z
      .string()
      .max(256, t("validation.emailMax"))
      .email(t("validation.emailInvalid"))
      .or(z.literal(""))
      .optional(),
    phone: z.string().max(64, t("validation.phoneMax")).optional(),
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
    contactId: z.string().min(1, t("validation.meetingContactRequired")),
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
    probabilityPercent: z.number({ error: t("validation.probabilityInvalid") }).int(t("validation.probabilityInvalid")).min(0, t("validation.probabilityInvalid")).max(100, t("validation.probabilityInvalid")),
    leadSource: z.string().max(64, t("validation.leadSourceMax")).optional(),
    expectedCloseAt: z.string().optional(),
    nextStep: z.string().max(512, t("validation.nextStepMax")).optional(),
    notes: z.string().max(8192, t("validation.notesMax")).optional(),
  });
}

export type DealFormValues = z.infer<ReturnType<typeof buildDealSchema>>;

/** Mirrors CreateTaskValidator / UpdateTaskValidator. */
export function buildTaskSchema(t: Translate) {
  return z.object({
    title: z.string().min(1, t("validation.titleRequired")).max(256, t("validation.titleMax")),
    description: z.string().max(8192, t("validation.notesMax")).optional(),
    dueAt: z.string().optional(),
    priority: z.enum(TASK_PRIORITIES),
  });
}

export type TaskFormValues = z.infer<ReturnType<typeof buildTaskSchema>>;

/** Mirrors CreateCompanyValidator / UpdateCompanyValidator. */
export function buildCompanySchema(t: Translate) {
  return z.object({
    name: z.string().min(1, t("validation.nameRequired")).max(256, t("validation.nameMax")),
    domain: z.string().max(256, t("validation.domainMax")).optional(),
    industry: z.string().max(128, t("validation.industryMax")).optional(),
    type: z.enum(COMPANY_TYPES),
    identificationNumber: z.string().max(32, t("validation.identificationNumberMax")).optional(),
    taxIdentificationNumber: z.string().max(32, t("validation.taxIdentificationNumberMax")).optional(),
    registeredAddress: z.string().max(512, t("validation.registeredAddressMax")).optional(),
    city: z.string().max(128, t("validation.cityMax")).optional(),
    postalCode: z.string().max(32, t("validation.postalCodeMax")).optional(),
    country: z.string().max(128, t("validation.countryMax")).optional(),
    notes: z.string().max(8192, t("validation.notesMax")).optional(),
  });
}

export type CompanyFormValues = z.infer<ReturnType<typeof buildCompanySchema>>;
