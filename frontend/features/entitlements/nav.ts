import type { LucideIcon } from "lucide-react";
import {
  LayoutDashboardIcon,
  CreditCardIcon,
  FileIcon,
  BellIcon,
  UserIcon,
  ShieldIcon,
  UsersIcon,
  BuildingIcon,
} from "lucide-react";

export interface NavItem {
  key: string;
  href: string;
  /** next-intl key in the "nav" namespace. */
  labelKey: string;
  icon: LucideIcon;
  /** When set, nav item is shown only if the tenant has this module enabled. */
  moduleKey?: string;
  /** When set, nav item is shown only if the session user has this permission. */
  permission?: string;
}

/** Canonical nav config. The AppNav component filters by entitlements + permissions. */
export const NAV_ITEMS: NavItem[] = [
  {
    key: "dashboard",
    href: "/",
    labelKey: "dashboard",
    icon: LayoutDashboardIcon,
  },
  {
    key: "billing",
    href: "/billing",
    labelKey: "billing",
    icon: CreditCardIcon,
    moduleKey: "billing",
  },
  {
    key: "files",
    href: "/files",
    labelKey: "files",
    icon: FileIcon,
    moduleKey: "files",
  },
  {
    key: "notifications",
    href: "/notifications",
    labelKey: "notifications",
    icon: BellIcon,
    moduleKey: "notifications",
  },
  {
    key: "profile",
    href: "/account/profile",
    labelKey: "profile",
    icon: UserIcon,
  },
  {
    key: "privacy",
    href: "/account/privacy",
    labelKey: "privacy",
    icon: ShieldIcon,
  },
];

/** Platform-admin nav (used in /platform layout only). */
export const PLATFORM_NAV_ITEMS: NavItem[] = [
  {
    key: "platformTenants",
    href: "/platform/tenants",
    labelKey: "platformTenants",
    icon: BuildingIcon,
    permission: "platform.tenants.manage",
  },
  {
    key: "users",
    href: "/platform/users",
    labelKey: "users",
    icon: UsersIcon,
    permission: "identity.manage_roles",
  },
  {
    key: "audit",
    href: "/platform/audit",
    labelKey: "audit",
    icon: ShieldIcon,
    permission: "audit.read",
  },
];
