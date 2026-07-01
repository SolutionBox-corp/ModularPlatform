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
  // Auth
  "auth.required": { en: "Please sign in to continue.", cs: "Pro pokračování se přihlaste." },
  "auth.unauthenticated": { en: "Please sign in to continue.", cs: "Pro pokračování se přihlaste." },
  "auth.expired": { en: "Your session expired. Please sign in again.", cs: "Platnost vaší relace vypršela. Přihlaste se znovu." },
  "auth.invalid_credentials": { en: "Incorrect email or password.", cs: "Nesprávný e-mail nebo heslo." },
  // Backend code: auth.locked_out (LoginHandler.cs). Keep auth.account_locked as alias.
  "auth.locked_out": { en: "Account temporarily locked. Try again later.", cs: "Účet je dočasně uzamčen. Zkuste to později." },
  "auth.account_locked": { en: "Account temporarily locked. Try again later.", cs: "Účet je dočasně uzamčen. Zkuste to později." },
  "auth.forbidden": { en: "You do not have permission to access this area.", cs: "Nemáte oprávnění pro přístup do této části." },
  "auth.email_verification_invalid": { en: "This email verification link is invalid or expired.", cs: "Odkaz pro ověření e-mailu je neplatný nebo vypršel." },
  "security.csrf_failed": { en: "Security check failed. Reload and retry.", cs: "Bezpečnostní kontrola selhala. Obnovte stránku." },
  "rate_limit.exceeded": { en: "Too many requests. Please slow down.", cs: "Příliš mnoho požadavků. Zpomalte prosím." },
  // Identity
  "user.email_taken": { en: "That email is already registered.", cs: "Tento e-mail je již registrován." },
  "user.not_found": { en: "User not found.", cs: "Uživatel nenalezen." },
  "user.current_password_invalid": { en: "The current password is incorrect.", cs: "Aktuální heslo není správné." },
  "user.password_unchanged": { en: "The new password must differ from the current one.", cs: "Nové heslo se musí lišit od aktuálního." },
  "user.accepted_terms_version.required": { en: "Terms version is required.", cs: "Verze souhlasu s podmínkami je povinná." },
  "user.accepted_terms_version.too_long": { en: "Terms version is too long.", cs: "Verze souhlasu s podmínkami je příliš dlouhá." },
  "profile.update_failed": { en: "Profile update failed.", cs: "Aktualizace profilu selhala." },
  // Billing
  "billing.insufficient_credits": { en: "Not enough credits for this action.", cs: "Nedostatek kreditů pro tuto akci." },
  "billing.no_billing_account": { en: "No billing account yet. Subscribe or buy credits first.", cs: "Zatím nemáte fakturační účet. Nejprve si předplaťte nebo kupte kredity." },
  "billing.package_not_found": { en: "That package is no longer available.", cs: "Tento balíček již není dostupný." },
  "billing.promo_invalid": { en: "This promo code is not valid.", cs: "Tento promo kód není platný." },
  "credit.account_not_found": { en: "Credit account not found.", cs: "Kreditní účet nenalezen." },
  "billing.gateway.unknown_provider": { en: "Unknown payment provider.", cs: "Neznámý poskytovatel plateb." },
  "billing.gateway.stripe_key_required": { en: "A Stripe API key is required.", cs: "Je vyžadován Stripe API klíč." },
  "billing.gateway.stripe_webhook_secret_required": { en: "A Stripe webhook signing secret is required.", cs: "Je vyžadován podpisový tajný klíč Stripe webhooku." },
  "billing.gateway.gopay_goid_required": { en: "A GoPay merchant id is required.", cs: "Je vyžadováno GoPay obchodní id." },
  "billing.gateway.gopay_client_required": { en: "GoPay client credentials are required.", cs: "Jsou vyžadovány GoPay klientské údaje." },
  "billing.gateway.fake_not_allowed": { en: "The fake payment gateway is not allowed in production.", cs: "Fake platební brána není v produkci povolena." },
  "billing.gateway.credentials_invalid": { en: "The payment gateway credentials could not be validated.", cs: "Údaje platební brány se nepodařilo ověřit." },
  // Files — backend code: file.content_type.not_allowed (UploadFileValidator.cs).
  // Keep file.type_not_allowed as alias in case it appears in older API responses.
  "file.too_large": { en: "File is too large (max 10 MB).", cs: "Soubor je příliš velký (max 10 MB)." },
  "file.content_type.not_allowed": { en: "This file type is not allowed.", cs: "Tento typ souboru není povolen." },
  "file.type_not_allowed": { en: "This file type is not allowed.", cs: "Tento typ souboru není povolen." },
  "file.not_found": { en: "File not found.", cs: "Soubor nenalezen." },
  // Notifications
  "notification.not_found": { en: "Notification not found.", cs: "Oznámení nenalezeno." },
  // GDPR
  "gdpr.export_in_progress": { en: "An export is already in progress.", cs: "Export již probíhá." },
  "gdpr.consent_failed": { en: "Consent write failed.", cs: "Uložení souhlasu selhalo." },
  "gdpr.export_failed": { en: "Export failed.", cs: "Export selhal." },
  "gdpr.erase_failed": { en: "Account erasure failed.", cs: "Vymazání účtu selhalo." },
  // Operations
  "operations.invoke_timeout": { en: "The quick worker request timed out.", cs: "Rychlý požadavek na worker vypršel." },
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
