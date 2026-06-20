import type { Metadata } from "next";
import Link from "next/link";
import { getLocale, getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("legal");
  return {
    title: t("terms.metaTitle"),
  };
}

/**
 * Public Terms of Service page — no auth required.
 * Linked from the registration form's acceptTerms checkbox.
 *
 * i18n note: the long-form legal BODY paragraphs keep ENGLISH as the legally
 * authoritative text. A Czech DRAFT translation is provided; when the locale is
 * `cs` a banner is shown stating the Czech text is a draft and English governs.
 */
export default async function TermsPage() {
  const t = await getTranslations("legal");
  const locale = await getLocale();

  return (
    <div className="min-h-screen bg-background px-4 py-12">
      <div className="mx-auto max-w-2xl space-y-8">
        <header className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">{t("terms.title")}</h1>
          <p className="text-sm text-muted-foreground">
            {t("lastUpdated", { date: t("terms.lastUpdatedDate") })}
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
          <h2 className="text-xl font-semibold">{t("terms.sections.acceptance.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t.rich("terms.sections.acceptance.body", {
              privacyLink: (chunks) => (
                <Link href="/privacy" className="text-primary underline underline-offset-4">
                  {chunks}
                </Link>
              ),
            })}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.description.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.description.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.accounts.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.accounts.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.acceptableUse.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.acceptableUse.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.intellectualProperty.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.intellectualProperty.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.billing.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.billing.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.termination.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.termination.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.liability.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.liability.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.changes.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.changes.body")}
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">{t("terms.sections.governingLaw.heading")}</h2>
          <p className="text-muted-foreground leading-relaxed">
            {t("terms.sections.governingLaw.body")}
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
