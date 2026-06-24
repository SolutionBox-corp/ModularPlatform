"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useTransition } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarInset,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
} from "@/components/ui/sidebar";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Separator } from "@/components/ui/separator";
import { RealtimeIndicator } from "@/components/app/realtime-indicator";
import { ThemeToggle } from "@/components/app/theme-toggle";
import { LocaleToggle } from "@/components/app/locale-toggle";
import { AppNav } from "@/features/entitlements/components/app-nav";
import { logoutAction } from "@/features/auth/actions";
import type { SessionUser } from "@/lib/auth/session";

interface AppShellProps {
  children: ReactNode;
  user: SessionUser;
  /** Which nav set the sidebar renders ("platform" = admin console). Serializable — see AppNav. */
  navVariant?: "tenant" | "platform";
}

function userInitials(user: SessionUser): string {
  const name = user.displayName ?? user.email;
  return name
    .split(/\s+/)
    .map((w) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export function AppShell({ children, user, navVariant }: AppShellProps) {
  const t = useTranslations("nav");
  const tShell = useTranslations("shell");
  const router = useRouter();
  const [isPending, startTransition] = useTransition();

  function handleLogout() {
    startTransition(async () => {
      await logoutAction();
      toast.success(tShell("appShell.signedOut"));
      router.push("/login");
      router.refresh();
    });
  }

  return (
    <SidebarProvider>
      {/* Skip navigation link — visible only on focus */}
      <a
        href="#main-content"
        className="sr-only focus-visible:not-sr-only focus-visible:fixed focus-visible:left-4 focus-visible:top-4 focus-visible:z-50 focus-visible:rounded-md focus-visible:bg-background focus-visible:px-4 focus-visible:py-2 focus-visible:text-sm focus-visible:font-medium focus-visible:ring-2 focus-visible:ring-ring"
      >
        {tShell("appShell.skipToContent")}
      </a>

      {/* Left sidebar */}
      <Sidebar collapsible="icon">
        <SidebarHeader>
          <Link
            href="/"
            className="flex items-center gap-2 px-2 py-1.5 text-sm font-semibold tracking-tight"
          >
            <span className="truncate">ModularPlatform</span>
          </Link>
        </SidebarHeader>

        <SidebarContent>
          <AppNav permissions={user.permissions} variant={navVariant} />
        </SidebarContent>

        <SidebarFooter>
          {/* User menu */}
          <DropdownMenu>
            <DropdownMenuTrigger
              className="flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-sm hover:bg-muted transition-colors outline-none focus-visible:ring-2 focus-visible:ring-ring"
              aria-label={tShell("appShell.userMenu")}
            >
              <Avatar className="h-6 w-6 text-xs">
                <AvatarFallback>{userInitials(user)}</AvatarFallback>
              </Avatar>
              <span className="truncate group-data-[collapsible=icon]:hidden">
                {user.displayName ?? user.email}
              </span>
            </DropdownMenuTrigger>
            <DropdownMenuContent side="top" align="start" className="w-52">
              <DropdownMenuGroup>
                <DropdownMenuLabel className="font-normal">
                  <div className="flex flex-col gap-0.5">
                    {user.displayName && (
                      <span className="text-sm font-medium">{user.displayName}</span>
                    )}
                    <span className="text-xs text-muted-foreground truncate">
                      {user.email}
                    </span>
                  </div>
                </DropdownMenuLabel>
              </DropdownMenuGroup>
              <DropdownMenuSeparator />
              <DropdownMenuItem render={<Link href="/account/profile" />}>
                {t("profile")}
              </DropdownMenuItem>
              <DropdownMenuItem render={<Link href="/account/privacy" />}>
                {t("privacy")}
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                onClick={handleLogout}
                disabled={isPending}
                className="text-destructive focus:text-destructive"
              >
                {isPending ? tShell("appShell.signingOut") : t("signOut")}
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </SidebarFooter>

        <SidebarRail />
      </Sidebar>

      {/* Main area */}
      <SidebarInset>
        {/* Slim topbar */}
        <header className="flex h-10 shrink-0 items-center gap-2 border-b border-border px-3">
          <SidebarTrigger className="-ml-1" />
          <Separator orientation="vertical" className="h-4" />

          {/* Push right */}
          <div className="ml-auto flex items-center gap-1">
            <RealtimeIndicator />
            <ThemeToggle />
            <LocaleToggle />
          </div>
        </header>

        {/* Page content */}
        <main id="main-content" className="flex-1 overflow-auto p-4 md:p-6">{children}</main>
      </SidebarInset>
    </SidebarProvider>
  );
}
