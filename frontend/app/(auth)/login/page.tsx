import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { LoginForm } from "@/features/auth/components/login-form";
import { DevQuickLogin } from "@/features/auth/components/dev-quick-login";
import { SessionExpiredBanner } from "@/features/auth/components/session-expired-banner";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth");
  return {
    title: t("login.metaTitle"),
  };
}

export default async function LoginPage() {
  const t = await getTranslations("auth");
  return (
    <section className="space-y-4">
      <div className="space-y-1 text-center">
        <h2 className="text-xl font-semibold">{t("login.heading")}</h2>
        <p className="text-sm text-muted-foreground">
          {t("login.subtitle")}
        </p>
      </div>
      {/* Reads ?reason=expired from the URL; renders nothing when absent. */}
      <SessionExpiredBanner />
      <LoginForm />
      {process.env.NODE_ENV !== "production" && <DevQuickLogin />}
    </section>
  );
}
