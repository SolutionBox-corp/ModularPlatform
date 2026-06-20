import type { Metadata } from "next";
import { Suspense } from "react";
import { getTranslations } from "next-intl/server";
import { RegisterForm } from "@/features/auth/components/register-form";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth");
  return {
    title: t("register.metaTitle"),
  };
}

export default async function RegisterPage() {
  const t = await getTranslations("auth");
  return (
    <section className="space-y-4">
      <div className="space-y-1 text-center">
        <h2 className="text-xl font-semibold">{t("register.heading")}</h2>
        <p className="text-sm text-muted-foreground">
          {t("register.subtitle")}
        </p>
      </div>
      {/* Suspense required because RegisterForm uses useSearchParams */}
      <Suspense>
        <RegisterForm />
      </Suspense>
    </section>
  );
}
