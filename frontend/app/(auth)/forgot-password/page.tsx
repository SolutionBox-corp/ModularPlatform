import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { ForgotPasswordForm } from "@/features/auth/components/forgot-password-form";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth");
  return {
    title: t("forgotPassword.metaTitle"),
  };
}

export default async function ForgotPasswordPage() {
  const t = await getTranslations("auth");
  return (
    <section className="space-y-4">
      <div className="space-y-1 text-center">
        <h2 className="text-xl font-semibold">{t("forgotPassword.heading")}</h2>
        <p className="text-sm text-muted-foreground">
          {t("forgotPassword.subtitle")}
        </p>
      </div>
      <ForgotPasswordForm />
    </section>
  );
}
