import Link from "next/link";
import { Button } from "@/components/ui/button";
import { getTranslations } from "next-intl/server";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("shell");
  return { title: t("notFound.metaTitle") };
}

export default async function NotFound() {
  const t = await getTranslations("shell");
  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4">
      <div className="text-center space-y-4 max-w-sm">
        <p className="text-6xl font-bold text-muted-foreground/40 select-none">
          404
        </p>
        <h1 className="text-xl font-semibold">{t("notFound.heading")}</h1>
        <p className="text-sm text-muted-foreground">
          {t("notFound.description")}
        </p>
        <Button
          variant="outline"
          render={<Link href="/" />}
        >
          {t("notFound.goToDashboard")}
        </Button>
      </div>
    </div>
  );
}
