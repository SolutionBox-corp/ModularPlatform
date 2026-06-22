import { getRequestConfig } from "next-intl/server";
import { cookies } from "next/headers";
import { type AppLocale } from "@/lib/i18n/config";

export { locales, defaultLocale, type AppLocale } from "@/lib/i18n/config";

/** Locale comes from the `NEXT_LOCALE` cookie (set by the theme/locale switcher). */
export default getRequestConfig(async () => {
  const cookieLocale = (await cookies()).get("NEXT_LOCALE")?.value;
  const locale: AppLocale = cookieLocale === "cs" ? "cs" : "en";
  return {
    locale,
    messages: (await import(`@/messages/${locale}.json`)).default,
  };
});
