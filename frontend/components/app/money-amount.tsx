import { cn } from "@/lib/utils";

interface MoneyAmountProps {
  /** Integer credit amount, or a decimal currency amount. */
  value: number;
  /**
   * ISO-4217 currency code (e.g. "USD", "EUR") or "credits". When omitted the value
   * is rendered as a plain credit count with thousands grouping.
   */
  currency?: string;
  className?: string;
}

/**
 * Renders a monetary or credit value with tabular-nums and right-aligned text.
 * Credits are integer amounts shown with thousands grouping (e.g. 1,250 cr.).
 * Fiat amounts use Intl.NumberFormat with the supplied currency code.
 */
export function MoneyAmount({ value, currency, className }: MoneyAmountProps) {
  const formatted =
    currency && currency !== "credits"
      ? new Intl.NumberFormat("en", {
          style: "currency",
          currency,
          minimumFractionDigits: 2,
          maximumFractionDigits: 2,
        }).format(value)
      : `${new Intl.NumberFormat("en").format(value)} cr.`;

  return (
    <span
      className={cn(
        "tabular-nums text-right font-medium leading-none",
        className,
      )}
      aria-label={formatted}
    >
      {formatted}
    </span>
  );
}
