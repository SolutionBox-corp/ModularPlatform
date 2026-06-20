import type { Metadata } from "next";
import { LoginForm } from "@/features/auth/components/login-form";
import { DevQuickLogin } from "@/features/auth/components/dev-quick-login";
import { SessionExpiredBanner } from "@/features/auth/components/session-expired-banner";

export const metadata: Metadata = {
  title: "Sign in — ModularPlatform",
};

export default function LoginPage() {
  return (
    <section className="space-y-4">
      <div className="space-y-1 text-center">
        <h2 className="text-xl font-semibold">Sign in</h2>
        <p className="text-sm text-muted-foreground">
          Enter your email and password to continue.
        </p>
      </div>
      {/* Reads ?reason=expired from the URL; renders nothing when absent. */}
      <SessionExpiredBanner />
      <LoginForm />
      {process.env.NODE_ENV !== "production" && <DevQuickLogin />}
    </section>
  );
}
