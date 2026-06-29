import { notFound } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { getQueryClient } from "@/lib/api/query-client";
import { BoardList } from "@/features/crm/components/board-list";

export default async function CrmBoardsPage() {
  const t = await getTranslations("crm");
  const ent = await getQueryClient().fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "crm")) notFound();
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">{t("board.pageHeading")}</h1>
        <p className="text-sm text-muted-foreground mt-0.5">{t("board.pageDescription")}</p>
      </div>
      <BoardList />
    </div>
  );
}
