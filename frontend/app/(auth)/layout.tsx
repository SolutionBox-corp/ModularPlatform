import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getSession, isAuthenticated } from "@/lib/auth/session";
import type { ReactNode } from "react";

export default async function AuthLayout({ children }: { children: ReactNode }) {
  const session = await getSession();
  if (isAuthenticated(session)) {
    redirect("/");
  }

  const t = await getTranslations("auth");

  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4">
      <div className="w-full max-w-sm space-y-6">
        <div className="text-center space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">{t("brand")}</h1>
        </div>
        {children}
      </div>
    </div>
  );
}
