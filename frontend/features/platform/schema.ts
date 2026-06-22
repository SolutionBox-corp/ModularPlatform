import { z } from "zod";

/** Translator shape (next-intl's `useTranslations('platform')`) — only what the schema needs. */
type Translate = (key: string) => string;

export function buildProvisionTenantSchema(t: Translate) {
  return z.object({
    name: z
      .string()
      .min(1, t("validation.nameRequired"))
      .max(256, t("validation.nameMax")),
    subdomain: z
      .string()
      .min(1, t("validation.subdomainRequired"))
      .max(63, t("validation.subdomainMax"))
      .regex(/^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/, t("validation.subdomainPattern")),
  });
}

export type ProvisionTenantFormValues = z.infer<
  ReturnType<typeof buildProvisionTenantSchema>
>;

export function buildCreateInviteSchema(t: Translate) {
  return z.object({
    expiresInDays: z
      .number({ error: t("validation.daysNumber") })
      .int(t("validation.daysWhole"))
      .min(1, t("validation.daysMin"))
      .max(90, t("validation.daysMax")),
  });
}

export type CreateInviteFormValues = z.infer<
  ReturnType<typeof buildCreateInviteSchema>
>;
