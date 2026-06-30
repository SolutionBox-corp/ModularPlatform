import { z } from "zod";

/** Translator shape (next-intl's `useTranslations('marketing')`) — only what the schema needs. */
type Translate = (key: string) => string;

/**
 * Trigger-pull input — the data source to pull. Mirrors the backend's currently wired gateways: "ga4" | "gsc".
 */
export function buildTriggerPullSchema(t: Translate) {
  return z
    .object({
      source: z.enum(["ga4", "gsc"], {
        message: t("validation.sourceRequired"),
      }),
      startDate: z.string().optional(),
      endDate: z.string().optional(),
    })
    .refine(
      (values) =>
        !values.startDate ||
        !values.endDate ||
        values.startDate <= values.endDate,
      {
        message: t("validation.dateRangeInvalid"),
        path: ["startDate"],
      },
    );
}

export type TriggerPullInput = z.infer<ReturnType<typeof buildTriggerPullSchema>>;

/**
 * Vibe-chat message input — non-empty, max 4000 chars. Mirrors SendMessageValidator.cs
 * (marketing.vibe.message_required / marketing.vibe.message_too_long).
 */
export function buildChatMessageSchema(t: Translate) {
  return z.object({
    content: z
      .string()
      .trim()
      .min(1, t("validation.messageRequired"))
      .max(4000, t("validation.messageTooLong")),
  });
}

export type ChatMessageInput = z.infer<ReturnType<typeof buildChatMessageSchema>>;
