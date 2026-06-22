import { z } from "zod";

/** Translator shape (next-intl's `useTranslations('billing')`) — only what the schema needs. */
type Translate = (key: string) => string;

/** Promo code input — trimmed, non-empty. */
export function buildPromoCodeSchema(t: Translate) {
  return z.object({
    code: z
      .string()
      .min(1, t("promo.validation.required"))
      .max(64, t("promo.validation.tooLong"))
      .transform((v) => v.trim().toUpperCase()),
  });
}

export type PromoCodeInput = z.infer<ReturnType<typeof buildPromoCodeSchema>>;
