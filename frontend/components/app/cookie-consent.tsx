"use client";

import { useEffect } from "react";
import * as CookieConsent from "vanilla-cookieconsent";
import "vanilla-cookieconsent/dist/cookieconsent.css";

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
export function CookieConsentBanner() {
  useEffect(() => {
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
        default: "en",
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
    });
  }, []);

  return null;
}
