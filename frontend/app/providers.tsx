"use client";

import { useState, type ReactNode } from "react";
import { QueryClientProvider } from "@tanstack/react-query";
import { ReactQueryDevtools } from "@tanstack/react-query-devtools";
import { ThemeProvider } from "next-themes";
import { TooltipProvider } from "@/components/ui/tooltip";
import { Toaster } from "@/components/ui/sonner";
import { getQueryClient } from "@/lib/api/query-client";
import { CookieConsentBanner } from "@/components/app/cookie-consent";
import { ClientErrorReporter } from "@/components/app/client-error-reporter";

/**
 * Global client providers wrapped around the whole app: one QueryClient (the single
 * data source), theming, tooltips and the toaster (the single error/feedback surface).
 * The realtime SSE provider is mounted only inside authenticated layouts (not here), so
 * unauthenticated pages like /login don't open a stream that would 401-redirect-loop.
 */
export function Providers({ children, nonce }: { children: ReactNode; nonce?: string }) {
  const [queryClient] = useState(getQueryClient);

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider
        attribute="class"
        defaultTheme="system"
        enableSystem
        disableTransitionOnChange
        nonce={nonce}
        // The no-flash theme <script> runs only on the server (a real executable
        // tag), so it still sets the theme before first paint. On the client it
        // reconciles as `text/plain`, which stops React 19's dev-only "Encountered
        // a script tag while rendering" warning + the resulting tree-diff hydration
        // noise. next-themes already forces suppressHydrationWarning on this node,
        // covering the type-attribute mismatch. (Next.js 16 documented pattern.)
        scriptProps={{ type: typeof window === "undefined" ? "text/javascript" : "text/plain" }}
      >
        <TooltipProvider>{children}</TooltipProvider>
        <Toaster richColors closeButton position="top-right" />
        <CookieConsentBanner />
        <ClientErrorReporter />
      </ThemeProvider>
      {process.env.NODE_ENV === "development" && <ReactQueryDevtools initialIsOpen={false} />}
    </QueryClientProvider>
  );
}
