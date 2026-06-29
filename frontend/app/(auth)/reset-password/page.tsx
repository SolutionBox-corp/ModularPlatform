import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { ResetPasswordForm } from "@/features/auth/components/reset-password-form";

interface PageProps {
  searchParams: Promise<{ token?: string | string[] }>;
}

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth");
  return {
    title: t("resetPassword.metaTitle"),
  };
}

export default async function ResetPasswordPage({ searchParams }: PageProps) {
  const t = await getTranslations("auth");
  const { token: rawToken } = await searchParams;
  const token = Array.isArray(rawToken) ? rawToken[0] : rawToken;

  return (
    <section className="space-y-4">
      <div className="space-y-1 text-center">
        <h2 className="text-xl font-semibold">{t("resetPassword.heading")}</h2>
        <p className="text-sm text-muted-foreground">
          {t("resetPassword.subtitle")}
        </p>
      </div>
      <ResetPasswordForm token={token} />
    </section>
  );
}
