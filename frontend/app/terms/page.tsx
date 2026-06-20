import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Terms of Service — ModularPlatform",
};

/**
 * Public Terms of Service page — no auth required.
 * Linked from the registration form's acceptTerms checkbox.
 */
export default function TermsPage() {
  return (
    <div className="min-h-screen bg-background px-4 py-12">
      <div className="mx-auto max-w-2xl space-y-8">
        <header className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">Terms of Service</h1>
          <p className="text-sm text-muted-foreground">Last updated: 20 June 2026</p>
        </header>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">1. Acceptance of Terms</h2>
          <p className="text-muted-foreground leading-relaxed">
            By creating an account or using ModularPlatform you agree to these Terms of Service
            and our{" "}
            <Link href="/privacy" className="text-primary underline underline-offset-4">
              Privacy Policy
            </Link>
            . If you do not agree, do not use the service.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">2. Description of Service</h2>
          <p className="text-muted-foreground leading-relaxed">
            ModularPlatform is a multi-tenant SaaS platform that provides identity management,
            billing, file storage, notifications, and GDPR tooling via a modular architecture.
            Access to individual modules depends on the entitlements granted to your tenant.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">3. Accounts and Security</h2>
          <p className="text-muted-foreground leading-relaxed">
            You are responsible for maintaining the confidentiality of your credentials and for
            all activity that occurs under your account. You must notify us immediately of any
            unauthorised use. We may suspend accounts that violate these terms or that we
            believe are compromised.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">4. Acceptable Use</h2>
          <p className="text-muted-foreground leading-relaxed">
            You agree not to use the platform to transmit unlawful content, attempt to
            circumvent security controls, reverse-engineer any component of the service, or
            interfere with the availability of the service for other users.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">5. Intellectual Property</h2>
          <p className="text-muted-foreground leading-relaxed">
            All platform software and documentation are the exclusive property of ModularPlatform
            and its licensors. Your data remains yours; you grant us a limited licence to
            process it solely to provide the service.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">6. Billing and Credits</h2>
          <p className="text-muted-foreground leading-relaxed">
            Credit purchases are processed by Stripe and are non-refundable unless required by
            applicable law. Unused credits expire according to the plan terms visible in your
            billing dashboard.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">7. Termination</h2>
          <p className="text-muted-foreground leading-relaxed">
            You may delete your account at any time via the Privacy settings page. We may
            terminate or suspend access for violations of these terms. On termination, your
            data is erased in accordance with our Privacy Policy and applicable GDPR obligations.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">8. Limitation of Liability</h2>
          <p className="text-muted-foreground leading-relaxed">
            To the maximum extent permitted by law, ModularPlatform is not liable for any
            indirect, incidental, or consequential damages arising from your use of the service.
            Our total liability to you shall not exceed the amounts you paid us in the twelve
            months preceding the claim.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">9. Changes to These Terms</h2>
          <p className="text-muted-foreground leading-relaxed">
            We may update these terms from time to time. Material changes will be notified via
            the platform or by email. Continued use after the effective date constitutes
            acceptance of the revised terms.
          </p>
        </section>

        <section className="space-y-3">
          <h2 className="text-xl font-semibold">10. Governing Law</h2>
          <p className="text-muted-foreground leading-relaxed">
            These terms are governed by the laws of the Czech Republic. Disputes shall be
            resolved in the courts of the Czech Republic unless mandatory consumer-protection
            law in your jurisdiction provides otherwise.
          </p>
        </section>

        <footer className="border-t border-border pt-6">
          <Link
            href="/login"
            className="text-sm text-primary underline underline-offset-4"
          >
            ← Back to sign in
          </Link>
        </footer>
      </div>
    </div>
  );
}
