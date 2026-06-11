using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.PaymentGateway.ConfigureGateway;

/// <summary>
/// Self-service: a tenant configures its OWN tenant-plane payment gateway (end-users paying the tenant). Credentials
/// are sealed at rest via <c>ISecretProtector</c> — only ciphertext is stored. The tenant comes from the token
/// (Law 10), never the body. Provider = <c>stripe</c> | <c>gopay</c> | <c>fake</c> (fake is for the test harness).
/// </summary>
public sealed record ConfigureGatewayCommand(
    string Provider,
    string Currency,
    string? StripeApiKey,
    string? StripeWebhookSecret,
    long? GoPayGoid,
    string? GoPayClientId,
    string? GoPayClientSecret,
    bool Sandbox) : ICommand<ConfigureGatewayResponse>;

public sealed record ConfigureGatewayResponse(Guid ConfigurationId, string Provider, bool Active);

public sealed record ConfigureGatewayRequest(
    string Provider,
    string Currency,
    string? StripeApiKey,
    string? StripeWebhookSecret,
    long? GoPayGoid,
    string? GoPayClientId,
    string? GoPayClientSecret,
    bool Sandbox);
