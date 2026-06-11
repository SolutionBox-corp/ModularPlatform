using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Payments;

/// <summary>
/// GoPay adapter for the neutral <see cref="IPaymentGateway"/> over the GoPay REST API (no SDK). Fundamentally
/// different from Stripe and these differences are why the port abstracts them:
/// <list type="bullet">
/// <item>auth = OAuth2 <c>client_credentials</c> (Basic), a ~30-min bearer cached + refreshed here;</item>
/// <item>checkout = <c>POST /payments/payment</c> → a <c>gw_url</c> redirect (no hosted "session" object);</item>
/// <item>webhook = an UNSIGNED GET carrying only an id ⇒ verification == re-fetch <c>GET /payments/payment/{id}</c>.</item>
/// </list>
/// Built with a tenant's credentials (goid + clientId/secret) + its base URL + notification URL by the resolver.
/// </summary>
public sealed class GoPayPaymentGateway(
    HttpClient http,
    GoPayCredentials credentials,
    IClock clock) : IPaymentGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? _token;
    private DateTimeOffset _tokenExpiresAt;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public GatewayCapabilities Capabilities { get; } = new(
        SignedWebhooks: false, NativeSubscriptions: false, NativeCoupons: false, NativeTax: false, PreAuthorization: true);

    public async Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = request.AmountMinorUnits,
            ["currency"] = request.Currency.ToUpperInvariant(),
            ["order_number"] = request.ReferenceId,
            ["order_description"] = request.Description,
            ["target"] = new Dictionary<string, object?> { ["type"] = "ACCOUNT", ["goid"] = credentials.Goid },
            ["callback"] = new Dictionary<string, object?>
            {
                ["return_url"] = request.SuccessUrl,
                ["notification_url"] = credentials.NotificationUrl,
            },
            ["additional_params"] = request.Metadata.Select(kv =>
                new Dictionary<string, object?> { ["name"] = kv.Key, ["value"] = kv.Value }).ToArray(),
            ["lang"] = "EN",
        };

        using var doc = await SendAsync(HttpMethod.Post, "/payments/payment", body, ct);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt64().ToString();
        var gwUrl = root.GetProperty("gw_url").GetString() ?? string.Empty;
        return new CheckoutResult(id, gwUrl);
    }

    public async Task<PaymentSnapshot> GetPaymentStateAsync(string providerPaymentId, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/payments/payment/{providerPaymentId}", null, ct);
        return ToSnapshot(doc.RootElement);
    }

    public async Task<RefundResult> RefundAsync(string providerPaymentId, long? amountMinorUnits, CancellationToken ct = default)
    {
        var current = await GetPaymentStateAsync(providerPaymentId, ct);
        var amount = amountMinorUnits ?? current.AmountMinorUnits
            ?? throw new InvalidOperationException("Refund amount unknown for GoPay payment.");

        using var doc = await SendAsync(HttpMethod.Post, $"/payments/payment/{providerPaymentId}/refund",
            new Dictionary<string, object?> { ["amount"] = amount }, ct);

        var full = amountMinorUnits is null || amountMinorUnits >= current.AmountMinorUnits;
        return new RefundResult(providerPaymentId, full ? PaymentState.Refunded : PaymentState.PartiallyRefunded);
    }

    public Task<PaymentSnapshot> VerifyNotificationAsync(NotificationContext context, CancellationToken ct = default)
    {
        // GoPay sends an unsigned GET with only an id — the notification is a "go look" hint, never authoritative.
        if (!context.Query.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("GoPay notification is missing the payment id.");
        }

        return GetPaymentStateAsync(id, ct);
    }

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct = default)
    {
        try
        {
            await GetTokenAsync(ct);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var message = new HttpRequestMessage(method, credentials.BaseUrl.TrimEnd('/') + path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, options: Json);
        }

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        // 60s safety margin so an in-flight request never uses a token that expires mid-call.
        if (_token is not null && clock.UtcNow < _tokenExpiresAt.AddSeconds(-60))
        {
            return _token;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && clock.UtcNow < _tokenExpiresAt.AddSeconds(-60))
            {
                return _token;
            }

            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{credentials.ClientId}:{credentials.ClientSecret}"));
            using var request = new HttpRequestMessage(HttpMethod.Post, credentials.BaseUrl.TrimEnd('/') + "/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["scope"] = "payment-all",
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            _token = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 1800;
            _tokenExpiresAt = clock.UtcNow.AddSeconds(expiresIn);
            return _token!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PaymentSnapshot ToSnapshot(JsonElement root)
    {
        var id = root.GetProperty("id").GetInt64().ToString();
        var state = root.TryGetProperty("state", out var s) ? s.GetString() : null;
        long? amount = root.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt64() : null;
        var currency = root.TryGetProperty("currency", out var c) ? c.GetString() : null;

        var metadata = new Dictionary<string, string>();
        if (root.TryGetProperty("additional_params", out var ap) && ap.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ap.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var n) && item.TryGetProperty("value", out var v)
                    && n.GetString() is { } name && v.GetString() is { } value)
                {
                    metadata[name] = value;
                }
            }
        }

        return new PaymentSnapshot(id, PaymentStateMapping.FromGoPay(state), amount, currency, metadata);
    }
}

/// <summary>A tenant's GoPay configuration: merchant id + OAuth credentials + the environment base URL and the per-tenant notification (webhook) URL.</summary>
public sealed record GoPayCredentials(
    long Goid,
    string ClientId,
    string ClientSecret,
    string BaseUrl,
    string NotificationUrl);
