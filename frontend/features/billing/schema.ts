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

export function buildPaymentGatewaySchema(t: Translate) {
  return z
    .object({
      provider: z.enum(["stripe", "gopay"]),
      currency: z
        .string()
        .trim()
        .min(3, t("paymentGateway.validation.currency"))
        .max(3, t("paymentGateway.validation.currency"))
        .transform((v) => v.toUpperCase()),
      stripeApiKey: z.string().trim().optional(),
      stripeWebhookSecret: z.string().trim().optional(),
      goPayGoid: z.string().trim().optional(),
      goPayClientId: z.string().trim().optional(),
      goPayClientSecret: z.string().trim().optional(),
      sandbox: z.boolean(),
    })
    .superRefine((value, ctx) => {
      if (value.provider === "stripe") {
        if (!value.stripeApiKey) {
          ctx.addIssue({
            code: "custom",
            path: ["stripeApiKey"],
            message: t("paymentGateway.validation.stripeApiKey"),
          });
        }
        if (!value.stripeWebhookSecret) {
          ctx.addIssue({
            code: "custom",
            path: ["stripeWebhookSecret"],
            message: t("paymentGateway.validation.stripeWebhookSecret"),
          });
        }
      }

      if (value.provider === "gopay") {
        if (!value.goPayGoid || Number.isNaN(Number(value.goPayGoid))) {
          ctx.addIssue({
            code: "custom",
            path: ["goPayGoid"],
            message: t("paymentGateway.validation.goPayGoid"),
          });
        }
        if (!value.goPayClientId) {
          ctx.addIssue({
            code: "custom",
            path: ["goPayClientId"],
            message: t("paymentGateway.validation.goPayClientId"),
          });
        }
        if (!value.goPayClientSecret) {
          ctx.addIssue({
            code: "custom",
            path: ["goPayClientSecret"],
            message: t("paymentGateway.validation.goPayClientSecret"),
          });
        }
      }
    });
}

export type PaymentGatewayFormValues = z.infer<
  ReturnType<typeof buildPaymentGatewaySchema>
>;
