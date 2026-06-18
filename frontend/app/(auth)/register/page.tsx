import type { Metadata } from "next";
import { Suspense } from "react";
import { RegisterForm } from "@/features/auth/components/register-form";

export const metadata: Metadata = {
  title: "Create account — ModularPlatform",
};

export default function RegisterPage() {
  return (
    <section className="space-y-4">
      <div className="space-y-1 text-center">
        <h2 className="text-xl font-semibold">Create account</h2>
        <p className="text-sm text-muted-foreground">
          Get started with ModularPlatform.
        </p>
      </div>
      {/* Suspense required because RegisterForm uses useSearchParams */}
      <Suspense>
        <RegisterForm />
      </Suspense>
    </section>
  );
}
