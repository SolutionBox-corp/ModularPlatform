"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";

const TABS = [
  { href: "/crm", labelKey: "tabs.contacts", exact: true },
  { href: "/crm/companies", labelKey: "tabs.companies" },
  { href: "/crm/deals", labelKey: "tabs.deals" },
  { href: "/crm/tasks", labelKey: "tabs.tasks" },
  { href: "/crm/meetings", labelKey: "tabs.meetings" },
  { href: "/crm/board", labelKey: "tabs.board" },
];

export function CrmTabs() {
  const t = useTranslations("crm");
  const pathname = usePathname();

  return (
    <nav className="flex gap-1 border-b">
      {TABS.map((tab) => {
        const active = tab.exact ? pathname === tab.href : pathname.startsWith(tab.href);
        return (
          <Link
            key={tab.href}
            href={tab.href}
            className={`-mb-px border-b-2 px-3 py-2 text-sm font-medium ${
              active ? "border-primary text-foreground" : "border-transparent text-muted-foreground hover:text-foreground"
            }`}
          >
            {t(tab.labelKey)}
          </Link>
        );
      })}
    </nav>
  );
}
