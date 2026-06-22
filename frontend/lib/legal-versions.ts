/**
 * Active legal-document versions sent to the backend on register/consent.
 *
 * These are ISO dates that MUST match the "Last updated" date shown on the
 * corresponding legal page (app/terms, app/privacy). When a page's published
 * date changes, bump the matching constant here so the backend records the
 * version the user actually saw.
 */

/** Terms of Service version — matches app/terms/page.tsx "Last updated". */
export const TERMS_VERSION = "2026-06-20";

/** Privacy Policy version — matches app/privacy/page.tsx "Last updated". */
export const PRIVACY_VERSION = "2026-06-20";
