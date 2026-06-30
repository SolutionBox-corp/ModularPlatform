import { getTranslations } from "next-intl/server";
import { CrmTabs } from "@/features/crm/components/crm-tabs";

export default async function CrmLayout({ children }: { children: React.ReactNode }) {
  const t = await getTranslations("crm");
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">{t("title")}</h1>
        <CrmTabs />
      </div>
      {children}
    </div>
  );
}
