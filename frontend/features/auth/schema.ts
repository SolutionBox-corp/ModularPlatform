import { z } from "zod";

export const loginSchema = z.object({
  email: z
    .string()
    .min(1, "Email is required")
    .refine((v) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v), "Enter a valid email"),
  password: z.string().min(1, "Password is required"),
});

export type LoginFormValues = z.infer<typeof loginSchema>;

export const registerSchema = z.object({
  email: z
    .string()
    .min(1, "Email is required")
    .refine((v) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v), "Enter a valid email"),
  password: z
    .string()
    .min(8, "Password must be at least 8 characters")
    .max(128, "Password too long"),
  displayName: z.string().max(128).optional(),
  inviteToken: z.string().optional(),
  /**
   * Must be `true` — the user MUST actively accept terms. `z.literal(true)` will
   * validate to false when the checkbox is unchecked. We provide a plain string
   * message as the second arg (Zod v4 params).
   */
  acceptTerms: z.literal(true, "You must accept the Terms and Privacy Policy"),
});

export type RegisterFormValues = z.infer<typeof registerSchema>;
