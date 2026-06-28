namespace ModularPlatform.Billing.Features.Credits.ReserveCredits;

public sealed record ReserveCreditsRequest(long Amount, int? HoldMinutes);
