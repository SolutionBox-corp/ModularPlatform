import { z } from "zod";

/**
 * Schema for the profile display form fields. Currently used for type-safety
 * on the read-only form only — no mutation is wired because the backend has no
 * profile update endpoint yet.
 *
 * When a PATCH /v1/identity/users/me endpoint is added, extend this schema with
 * validation rules (e.g. displayName max-length) and use it with a useMutation.
 */
export const profileSchema = z.object({
  /** Read-only: email changes require a separate verified-email-change flow. */
  email: z.string(),
  displayName: z.string().nullable(),
  /** Read-only: locale is controlled by the locale toggle in the top bar. */
  locale: z.string(),
});

export type ProfileFormValues = z.infer<typeof profileSchema>;
