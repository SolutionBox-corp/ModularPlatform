import { redirect } from "next/navigation";
import { getSession, isAuthenticated } from "@/lib/auth/session";
import type { ReactNode } from "react";

export default async function AuthLayout({ children }: { children: ReactNode }) {
  const session = await getSession();
  if (isAuthenticated(session)) {
    redirect("/");
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4">
      <div className="w-full max-w-sm space-y-6">
        <div className="text-center space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">ModularPlatform</h1>
        </div>
        {children}
      </div>
    </div>
  );
}
