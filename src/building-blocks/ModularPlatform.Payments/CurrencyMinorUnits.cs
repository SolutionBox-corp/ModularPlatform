namespace ModularPlatform.Payments;

/// <summary>
/// Converts a decimal amount to a provider "minor units" integer using the correct ISO-4217 exponent. A flat
/// <c>×100</c> is WRONG for zero-decimal currencies (JPY, KRW, …) — it overcharges 100× — and for three-decimal
/// currencies (KWD, BHD, …) — it undercharges 10×. Both Stripe and GoPay expect amounts in the currency's smallest
/// unit, so the exponent must follow the currency, not a hardcoded constant.
/// </summary>
public static class CurrencyMinorUnits
{
    // Stripe's canonical zero-decimal set (amounts are whole units of the currency).
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF",
        "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    // ISO-4217 three-decimal currencies.
    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "JOD", "KWD", "OMR", "TND",
    };

    /// <summary>The number of fractional digits the currency uses (2 for most, 0 for JPY-like, 3 for KWD-like).</summary>
    public static int Exponent(string currency) =>
        ZeroDecimal.Contains(currency) ? 0 : ThreeDecimal.Contains(currency) ? 3 : 2;

    /// <summary>Rounds <paramref name="amount"/> to the currency's smallest unit (e.g. 9.99 EUR → 999; 500 JPY → 500).</summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        var factor = (decimal)Math.Pow(10, Exponent(currency));
        return (long)Math.Round(amount * factor, MidpointRounding.AwayFromZero);
    }
}
