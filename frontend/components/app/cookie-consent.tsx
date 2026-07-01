"use client";

import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import * as CookieConsent from "vanilla-cookieconsent";
import "vanilla-cookieconsent/dist/cookieconsent.css";
import { CSRF_COOKIE, CSRF_HEADER } from "@/lib/auth/csrf";
import { PRIVACY_VERSION } from "@/lib/legal-versions";
import { queryRoots } from "@/lib/api/query-keys";

/**
 * CookieConsentBanner
 *
 * Initialises vanilla-cookieconsent v3 with three categories:
 *   - necessary  — always on; covers the iron-session auth cookie and CSRF token.
 *   - analytics  — opt-in; usage analytics (none active today, seam for future).
 *   - marketing  — opt-in; marketing/attribution cookies (none active today, seam for future).
 *
 * No non-essential cookie or script runs before the user grants consent. Because the platform
 * currently has no analytics or marketing scripts, the practical effect is recording the
 * user's decision and displaying a compliant banner.
 *
 * Mounted once inside <Providers> (client boundary). The banner is shown on first visit and
 * hidden permanently after consent is recorded in the cc_cookie cookie.
 */
/** Reads the NEXT_LOCALE cookie ("cs" → Czech, anything else → English). */
function readLocale(): "en" | "cs" {
  if (typeof document === "undefined") return "en";
  const match = document.cookie.match(/(?:^|;\s*)NEXT_LOCALE=(\w+)/);
  return match?.[1] === "cs" ? "cs" : "en";
}

export function CookieConsentBanner() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const locale = readLocale();
    void CookieConsent.run({
      categories: {
        necessary: {
          enabled: true,
          readOnly: true,
        },
        analytics: {
          enabled: false,
          readOnly: false,
        },
        marketing: {
          enabled: false,
          readOnly: false,
        },
      },

      language: {
        // Cookie-consent strings are managed by the vanilla-cookieconsent library's own
        // translation map (it renders outside the React/next-intl tree). We provide both
        // English and Czech here and pick the active language from the NEXT_LOCALE cookie.
        default: locale,
        translations: {
          en: {
            consentModal: {
              title: "We use cookies",
              description:
                "We use strictly necessary cookies to keep you signed in. With your consent we also set analytics and marketing cookies. See our <a href='/privacy' class='cc-link'>Privacy Policy</a> for details.",
              acceptAllBtn: "Accept all",
              acceptNecessaryBtn: "Necessary only",
              showPreferencesBtn: "Manage preferences",
            },
            preferencesModal: {
              title: "Cookie preferences",
              acceptAllBtn: "Accept all",
              acceptNecessaryBtn: "Necessary only",
              savePreferencesBtn: "Save preferences",
              closeIconLabel: "Close",
              sections: [
                {
                  title: "Strictly necessary cookies",
                  description:
                    "These cookies are required for the platform to function and cannot be disabled. They include the session cookie that keeps you signed in.",
                  linkedCategory: "necessary",
                },
                {
                  title: "Analytics cookies",
                  description:
                    "These cookies help us understand how you use the platform so we can improve it. No personal data is shared with third parties.",
                  linkedCategory: "analytics",
                },
                {
                  title: "Marketing cookies",
                  description:
                    "These cookies record your consent preferences and help us measure the reach of our communications.",
                  linkedCategory: "marketing",
                },
                {
                  title: "More information",
                  description:
                    "For questions about our cookie use, see our <a href='/privacy' class='cc-link'>Privacy Policy</a>.",
                },
              ],
            },
          },
          cs: {
            consentModal: {
              title: "Používáme cookies",
              description:
                "Používáme nezbytně nutné cookies, abyste zůstali přihlášeni. S vaším souhlasem nastavujeme také analytické a marketingové cookies. Podrobnosti najdete v našich <a href='/privacy' class='cc-link'>zásadách ochrany osobních údajů</a>.",
              acceptAllBtn: "Přijmout vše",
              acceptNecessaryBtn: "Jen nezbytné",
              showPreferencesBtn: "Spravovat předvolby",
            },
            preferencesModal: {
              title: "Předvolby cookies",
              acceptAllBtn: "Přijmout vše",
              acceptNecessaryBtn: "Jen nezbytné",
              savePreferencesBtn: "Uložit předvolby",
              closeIconLabel: "Zavřít",
              sections: [
                {
                  title: "Nezbytně nutné cookies",
                  description:
                    "Tyto cookies jsou nutné pro fungování platformy a nelze je vypnout. Patří mezi ně session cookie, která vás udržuje přihlášené.",
                  linkedCategory: "necessary",
                },
                {
                  title: "Analytické cookies",
                  description:
                    "Tyto cookies nám pomáhají porozumět tomu, jak platformu používáte, abychom ji mohli vylepšovat. Žádné osobní údaje nesdílíme s třetími stranami.",
                  linkedCategory: "analytics",
                },
                {
                  title: "Marketingové cookies",
                  description:
                    "Tyto cookies zaznamenávají vaše předvolby souhlasu a pomáhají nám měřit dosah naší komunikace.",
                  linkedCategory: "marketing",
                },
                {
                  title: "Více informací",
                  description:
                    "S dotazy ohledně používání cookies se obraťte na naše <a href='/privacy' class='cc-link'>zásady ochrany osobních údajů</a>.",
                },
              ],
            },
          },
        },
      },

      guiOptions: {
        consentModal: {
          layout: "box",
          position: "bottom right",
        },
        preferencesModal: {
          layout: "box",
          position: "right",
        },
      },

      onFirstConsent: () => {
        void persistCookieConsent().then((changed) => {
          if (changed) {
            void queryClient.invalidateQueries({
              queryKey: [...queryRoots.gdpr, "consents"],
            });
          }
        }).catch(() => undefined);
      },

      onChange: () => {
        void persistCookieConsent().then((changed) => {
          if (changed) {
            void queryClient.invalidateQueries({
              queryKey: [...queryRoots.gdpr, "consents"],
            });
          }
        }).catch(() => undefined);
      },
    });
  }, [queryClient]);

  return null;
}

const COOKIE_CONSENT_TYPES = [
  { category: "analytics", consentType: "cookie_analytics" },
  { category: "marketing", consentType: "cookie_marketing" },
] as const;

async function persistCookieConsent(): Promise<boolean> {
  if (typeof window === "undefined") return false;

  const results = await Promise.all(
    COOKIE_CONSENT_TYPES.map(({ category, consentType }) =>
      persistConsentDecision(consentType, CookieConsent.acceptedCategory(category)),
    ),
  );

  return results.some(Boolean);
}

async function persistConsentDecision(
  consentType: string,
  granted: boolean,
): Promise<boolean> {
  const csrf = readCookie(CSRF_COOKIE);
  const response = await fetch(
    `/api/bff/gdpr/consents/${granted ? "grant" : "withdraw"}`,
    {
      method: "POST",
      credentials: "same-origin",
      headers: {
        accept: "application/json",
        "accept-language": document.documentElement.lang || "en",
        "content-type": "application/json",
        ...(csrf ? { [CSRF_HEADER]: csrf } : {}),
      },
      body: JSON.stringify({ consentType, policyVersion: PRIVACY_VERSION }),
    },
  );

  if (response.ok) return true;
  if (response.status === 401 || response.status === 403) return false;

  throw new Error(`Cookie consent persistence failed: ${response.status}`);
}

function readCookie(name: string): string | undefined {
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : undefined;
}
