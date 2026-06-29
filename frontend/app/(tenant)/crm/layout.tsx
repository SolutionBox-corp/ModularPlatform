import { CrmTabs } from "@/features/crm/components/crm-tabs";

export default function CrmLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">CRM</h1>
        <CrmTabs />
      </div>
      {children}
    </div>
  );
}
