"use client";

import { useSyncExternalStore } from "react";
import { useTheme } from "next-themes";
import { useTranslations } from "next-intl";
import { SunIcon, MoonIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";

const noop = () => () => {};

export function ThemeToggle() {
  const t = useTranslations("shell");
  const { resolvedTheme, setTheme } = useTheme();
  // The server can't know the persisted theme (it lives client-side), so theme-dependent
  // UI must wait until mounted to avoid a hydration mismatch on the icon/label. This reads
  // false on the server (getServerSnapshot) and true on the client — no setState-in-effect.
  const mounted = useSyncExternalStore(noop, () => true, () => false);

  const isDark = mounted && resolvedTheme === "dark";
  const toggle = () => setTheme(isDark ? "light" : "dark");

  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button
            variant="ghost"
            size="icon"
            onClick={toggle}
            aria-label={isDark ? t("themeToggle.switchToLight") : t("themeToggle.switchToDark")}
          />
        }
      >
        {/* Until mounted, render a stable placeholder icon (matches SSR) to avoid a flash. */}
        {isDark ? <SunIcon aria-hidden="true" /> : <MoonIcon aria-hidden="true" />}
      </TooltipTrigger>
      <TooltipContent>{isDark ? t("themeToggle.lightMode") : t("themeToggle.darkMode")}</TooltipContent>
    </Tooltip>
  );
}
