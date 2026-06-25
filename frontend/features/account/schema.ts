import { z } from "zod";
import { locales } from "@/lib/i18n/config";

/**
 * Schema for the profile display form fields (read-only mirror of the API shape).
 */
export const profileSchema = z.object({
  email: z.string(),
  displayName: z.string().nullable(),
  locale: z.string(),
});

export type ProfileFormValues = z.infer<typeof profileSchema>;

/**
 * Schema for the editable profile fields submitted to PATCH /v1/identity/users/me.
 * Mirrors the backend UpdateProfileValidator: display name ≤ 128 chars (empty → cleared),
 * locale from the supported set.
 */
export const profileUpdateSchema = z.object({
  displayName: z.string().max(128, { message: "displayNameTooLong" }),
  locale: z.enum(locales),
});

export type ProfileUpdateValues = z.infer<typeof profileUpdateSchema>;

/**
 * Schema for the change-password form. Mirrors the backend ChangePasswordValidator
 * (new password 8–256 chars) plus a client-only confirm-match check. `message` values
 * are i18n keys resolved in the form.
 */
export const changePasswordSchema = z
  .object({
    currentPassword: z.string().min(1, { message: "currentRequired" }),
    newPassword: z
      .string()
      .min(8, { message: "tooShort" })
      .max(256, { message: "tooLong" }),
    confirmPassword: z.string(),
  })
  .refine((v) => v.newPassword === v.confirmPassword, {
    path: ["confirmPassword"],
    message: "mismatch",
  });

export type ChangePasswordValues = z.infer<typeof changePasswordSchema>;
