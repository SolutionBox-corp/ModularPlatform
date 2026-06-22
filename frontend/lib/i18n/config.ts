/** Client-safe i18n constants (no server imports — safe to import from Client Components). */
export const locales = ["en", "cs"] as const;
export type AppLocale = (typeof locales)[number];
export const defaultLocale: AppLocale = "en";
