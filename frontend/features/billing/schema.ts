import { z } from "zod";

/** Promo code input — trimmed, non-empty. */
export const promoCodeSchema = z.object({
  code: z
    .string()
    .min(1, "Enter a promo code.")
    .max(64, "Code is too long.")
    .transform((v) => v.trim().toUpperCase()),
});

export type PromoCodeInput = z.infer<typeof promoCodeSchema>;
