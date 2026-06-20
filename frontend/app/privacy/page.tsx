import type { Metadata } from "next";
import Link from "next/link";
import { getLocale, getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("legal");
  return {
    title: t("privacy.metaTitle"),
  };
}

/**
 * Public Privacy Policy page — no auth required.
 * Linked from the registration form's acceptTerms checkbox and the cookie consent banner.
 *
 * i18n note: the long-form legal BODY paragraphs keep ENGLISH as the legally
 * authoritative text. A Czech DRAFT translation is provided; when the locale is
 * `cs` a banner is shown stating the Czech text is a draft and English governs.
 */
export default async function PrivacyPolicyPage() {
  const t = await getTranslations("legal");
  const locale = await getLocale();

  return (
    <div className="min-h-screen bg-background px-4 py-12">
      <div className="mx-auto max-w-2xl space-y-8">
        <header className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">{t("privacy.title")}</h1>
          <p className="text-sm text-muted-foreground">
            {t("lastUpdated", { date: t("privacy.lastUpdatedDate") })}
          </p>
        </header>

        {locale === "cs" ? (
          <div
            role="note"
            className="rounded-md border border-border bg-muted/40 px-4 py-3 text-sm text-muted-foreground"
          >
            {t("draftBanner")}
          </div>
        ) : null}

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.whoWeAre.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.whoWeAre.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.dataWeCollect.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.dataWeCollect.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.legalBasis.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.legalBasis.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.howWeUse.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.howWeUse.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.cookies.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.cookies.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.retention.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.retention.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.yourRights.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.yourRights.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.security.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.security.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.processors.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.processors.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("privacy.sections.changes.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("privacy.sections.changes.body")}
          </p>
        </section>

        <footer className="border-t border-border pt-6">
          <Link
            href="/login"
            className="text-sm text-primary underline underline-offset-4"
          >
            {t("backToSignIn")}
          </Link>
        </footer>
      </div>
    </div>
  );
}
