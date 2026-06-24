"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar";
import { entitlementQueries, isModuleEnabled } from "@/features/entitlements/api";
import { NAV_ITEMS, PLATFORM_NAV_ITEMS } from "@/features/entitlements/nav";
import { cn } from "@/lib/utils";

interface AppNavProps {
  /** Permissions from the server session (already in the token). */
  permissions: string[];
  /**
   * Which nav set to render. "platform" = the admin-console nav. Passed as a serializable string (NOT the
   * nav array) because the items carry Lucide icon COMPONENTS — handing functions from a Server Component to
   * this Client Component crashes RSC ("Functions cannot be passed directly to Client Components"). Icons stay
   * client-side here.
   */
  variant?: "tenant" | "platform";
}

export function AppNav({ permissions, variant = "tenant" }: AppNavProps) {
  const pathname = usePathname();
  const t = useTranslations("nav");
  const { data: entitlements } = useQuery(entitlementQueries.me());

  const items = variant === "platform" ? PLATFORM_NAV_ITEMS : NAV_ITEMS;
  const visibleItems = items.filter((item) => {
    if (item.moduleKey && !isModuleEnabled(entitlements, item.moduleKey)) {
      return false;
    }
    if (item.permission && !permissions.includes(item.permission)) {
      return false;
    }
    return true;
  });

  return (
    <SidebarMenu>
      {visibleItems.map((item) => {
        const active =
          item.href === "/"
            ? pathname === "/"
            : pathname.startsWith(item.href);

        return (
          <SidebarMenuItem key={item.key}>
            <SidebarMenuButton
              render={
                <Link
                  href={item.href}
                  aria-current={active ? "page" : undefined}
                />
              }
              isActive={active}
              // next-intl's t() is typed against the messages shape; cast via unknown.
              tooltip={t(item.labelKey as unknown as Parameters<typeof t>[0])}
              className={cn(active && "font-medium")}
            >
              <item.icon aria-hidden="true" />
              <span>{t(item.labelKey as unknown as Parameters<typeof t>[0])}</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        );
      })}
    </SidebarMenu>
  );
}
