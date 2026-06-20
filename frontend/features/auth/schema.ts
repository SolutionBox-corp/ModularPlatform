import { z } from "zod";

/** Translator shape (next-intl's `useTranslations('auth')`) — only what the schema needs. */
type Translate = (key: string) => string;

export function buildLoginSchema(t: Translate) {
  return z.object({
    email: z
      .string()
      .min(1, t("validation.emailRequired"))
      .refine((v) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v), t("validation.emailInvalid")),
    password: z.string().min(1, t("validation.passwordRequired")),
  });
}

export type LoginFormValues = z.infer<ReturnType<typeof buildLoginSchema>>;

export function buildRegisterSchema(t: Translate) {
  return z.object({
    email: z
      .string()
      .min(1, t("validation.emailRequired"))
      .refine((v) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v), t("validation.emailInvalid")),
    password: z
      .string()
      .min(8, t("validation.passwordMin"))
      .max(128, t("validation.passwordMax")),
    displayName: z.string().max(128).optional(),
    inviteToken: z.string().optional(),
    /**
     * Must be `true` — the user MUST actively accept terms. `z.literal(true)` will
     * validate to false when the checkbox is unchecked. We provide a plain string
     * message as the second arg (Zod v4 params).
     */
    acceptTerms: z.literal(true, t("validation.acceptTerms")),
  });
}

export type RegisterFormValues = z.infer<ReturnType<typeof buildRegisterSchema>>;
