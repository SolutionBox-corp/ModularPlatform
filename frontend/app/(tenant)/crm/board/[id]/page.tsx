import { notFound } from "next/navigation";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { getQueryClient } from "@/lib/api/query-client";
import { KanbanBoardView } from "@/features/crm/components/kanban-board";

export default async function CrmBoardPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const ent = await getQueryClient().fetchQuery(entitlementQueries.me());
  if (!isModuleEnabled(ent, "crm")) notFound();
  return <KanbanBoardView boardId={id} />;
}
