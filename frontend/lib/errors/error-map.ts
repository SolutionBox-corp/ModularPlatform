import { isApiError } from "@/lib/api/types";

/**
 * THE single error→message mapping. One pure function, a static catalog keyed by the
 * backend's stable `errorCode`. Works everywhere (React or a QueryCache.onError handler),
 * unlike next-intl hooks. Internals are NEVER surfaced: unknown codes fall back to the
 * backend's localized `detail`, then a generic message. Feature code adds new codes here.
 */

export type Locale = "en" | "cs";

type Catalog = Record<string, { en: string; cs: string }>;

const CATALOG: Catalog = {
  // Generic
  "generic.error": { en: "Something went wrong. Please try again.", cs: "Něco se pokazilo. Zkuste to znovu." },
  "network.error": { en: "Network error. Check your connection.", cs: "Chyba sítě. Zkontrolujte připojení." },
  "auth.unauthenticated": { en: "Please sign in to continue.", cs: "Pro pokračování se přihlaste." },
  "auth.invalid_credentials": { en: "Incorrect email or password.", cs: "Nesprávný e-mail nebo heslo." },
  "auth.account_locked": { en: "Account temporarily locked. Try again later.", cs: "Účet je dočasně uzamčen. Zkuste to později." },
  "security.csrf_failed": { en: "Security check failed. Reload and retry.", cs: "Bezpečnostní kontrola selhala. Obnovte stránku." },
  "rate_limit.exceeded": { en: "Too many requests. Please slow down.", cs: "Příliš mnoho požadavků. Zpomalte prosím." },
  // Identity
  "user.email_taken": { en: "That email is already registered.", cs: "Tento e-mail je již registrován." },
  "user.not_found": { en: "User not found.", cs: "Uživatel nenalezen." },
  // Billing
  "billing.insufficient_credits": { en: "Not enough credits for this action.", cs: "Nedostatek kreditů pro tuto akci." },
  "billing.package_not_found": { en: "That package is no longer available.", cs: "Tento balíček již není dostupný." },
  "billing.promo_invalid": { en: "This promo code is not valid.", cs: "Tento promo kód není platný." },
  // Files
  "file.too_large": { en: "File is too large (max 10 MB).", cs: "Soubor je příliš velký (max 10 MB)." },
  "file.type_not_allowed": { en: "This file type is not allowed.", cs: "Tento typ souboru není povolen." },
  "file.not_found": { en: "File not found.", cs: "Soubor nenalezen." },
  // Notifications
  "notification.not_found": { en: "Notification not found.", cs: "Oznámení nenalezeno." },
  // GDPR
  "gdpr.export_in_progress": { en: "An export is already in progress.", cs: "Export již probíhá." },
};

/** Map an error code (and locale) to a user-facing message. Pure, side-effect free. */
export function errorCodeToMessage(
  errorCode: string | undefined,
  locale: Locale,
  fallbackDetail?: string,
): string {
  if (errorCode && CATALOG[errorCode]) return CATALOG[errorCode][locale];
  if (fallbackDetail && fallbackDetail.trim().length > 0) return fallbackDetail;
  return CATALOG["generic.error"][locale];
}

/** Resolve a thrown value (ApiError or anything) into a safe display message. */
export function toDisplayMessage(error: unknown, locale: Locale): string {
  if (isApiError(error)) {
    if (error.status === 429) {
      const base = errorCodeToMessage("rate_limit.exceeded", locale);
      return error.retryAfter ? `${base} (${error.retryAfter}s)` : base;
    }
    return errorCodeToMessage(error.errorCode, locale, error.detail);
  }
  return CATALOG["generic.error"][locale];
}

/** Best-effort current locale on the client (driven by <html lang>). */
export function currentLocale(): Locale {
  if (typeof document !== "undefined") {
    return document.documentElement.lang === "cs" ? "cs" : "en";
  }
  return "en";
}
