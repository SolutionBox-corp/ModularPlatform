import { z } from "zod";

export const provisionTenantSchema = z.object({
  name: z
    .string()
    .min(1, "Name is required")
    .max(256, "Name must be 256 characters or fewer"),
  subdomain: z
    .string()
    .min(1, "Subdomain is required")
    .max(63, "Subdomain must be 63 characters or fewer")
    .regex(
      /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/,
      "Subdomain must be lowercase alphanumeric with hyphens (no leading/trailing hyphen)",
    ),
});

export type ProvisionTenantFormValues = z.infer<typeof provisionTenantSchema>;

export const createInviteSchema = z.object({
  expiresInDays: z
    .number({ error: "Must be a number" })
    .int("Must be a whole number")
    .min(1, "Minimum 1 day")
    .max(90, "Maximum 90 days"),
});

export type CreateInviteFormValues = z.infer<typeof createInviteSchema>;
