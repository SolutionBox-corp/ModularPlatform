"use client";

import { PackageIcon } from "lucide-react";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { MoneyAmount } from "@/components/app/money-amount";
import { useCheckoutPackage } from "@/features/billing/hooks";
import type { CreditPackageResponse } from "@/features/billing/api";

interface PackageCardProps {
  pkg: CreditPackageResponse;
}

/**
 * Renders a single credit package with price, credit amount, expiry details,
 * and a "Buy now" button that initiates Stripe checkout (browser redirect).
 */
export function PackageCard({ pkg }: PackageCardProps) {
  const checkout = useCheckoutPackage();

  return (
    <Card className="flex flex-col">
      <CardHeader className="pb-2">
        <div className="flex items-start gap-2">
          <PackageIcon className="h-4 w-4 mt-0.5 text-muted-foreground shrink-0" />
          <div className="min-w-0">
            <CardTitle className="text-sm font-semibold">{pkg.name}</CardTitle>
            <CardDescription className="text-xs mt-0.5">
              <MoneyAmount value={pkg.creditAmount} />{" "}
              credits
              {pkg.bucketExpiryDays != null
                ? ` · expire in ${pkg.bucketExpiryDays}d`
                : " · no expiry"}
            </CardDescription>
          </div>
        </div>
      </CardHeader>

      <CardContent className="flex-1 pb-2">
        <p className="text-2xl font-semibold tabular-nums">
          <MoneyAmount value={pkg.price} currency={pkg.currency} />
        </p>
        <p className="text-xs text-muted-foreground mt-0.5">one-time payment</p>
      </CardContent>

      <CardFooter className="pt-0">
        <Button
          size="sm"
          className="w-full"
          disabled={checkout.isPending}
          onClick={() => checkout.mutate(pkg.id)}
        >
          {checkout.isPending ? "Redirecting…" : "Buy now"}
        </Button>
      </CardFooter>
    </Card>
  );
}
