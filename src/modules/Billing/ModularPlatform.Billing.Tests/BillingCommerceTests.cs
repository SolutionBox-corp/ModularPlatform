using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Payments;
using Shouldly;
using Stripe;
using Wolverine;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Billing commerce end-to-end through the <c>IStripeGateway</c> seam (FakeStripeGateway — the harness sets
/// <c>Billing:Stripe:UseFakeGateway=true</c>), which makes the FULL worker path assertable offline:
/// <list type="bullet">
/// <item>Package purchase: admin catalogue (billing.manage) → list → Stripe Checkout → webhook confirm →
/// <c>CreditPurchaseSaga</c> grants via the idempotent ledger top-up → purchase Completed (the saga row).</item>
/// <item>ST-1/ST-2 FULL: a signed top-up event applies the ledger top-up exactly once and stamps
/// <c>ProcessedAt</c> — previously unreachable without the seam.</item>
/// <item>ST-4: subscription webhooks reconcile against Stripe OBJECT state, so out-of-order deliveries
/// (updated-before-created, invoice-before-subscription) converge; <c>invoice.paid</c> grants per-period
/// credits exactly once (key <c>sub-invoice:{id}</c>).</item>
/// <item>Saga compensation: an explicit timeout abandons a pending purchase; a LATE confirmation still
/// grants (money is never lost to a workflow timeout).</item>
/// </list>
/// </summary>
[Collection("Integration")]
public sealed class BillingCommerceTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";
    private const string WebhookPath = "/v1/billing/webhooks/stripe";
    private const string CompatibleApiVersion = "2026-05-27.dahlia";

    private FakeStripeGateway Fake => (FakeStripeGateway)fixture.Services.GetRequiredService<IStripeGateway>();

    // The shared per-tenant fake gateway (the resolver hands this singleton out for a tenant whose provider is "fake").
    private FakePaymentGateway FakePay => fixture.Services.GetRequiredService<FakePaymentGateway>();

    /// <summary>Configures the admin's OWN tenant gateway to the in-memory fake (so its members' checkouts resolve it).</summary>
    private async Task ConfigureAdminFakeGatewayAsync(string adminToken) =>
        (await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, "/v1/billing/payment-gateway", adminToken,
            new { provider = "fake", currency = "EUR", sandbox = false }))).EnsureSuccessStatusCode();

    /// <summary>Marks the fake payment PAID and drives the per-tenant webhook (re-fetch model) that confirms the purchase.</summary>
    private async Task<HttpResponseMessage> ConfirmPaidViaTenantWebhookAsync(Guid tenantId, string providerPaymentId)
    {
        FakePay.SetState(providerPaymentId, PaymentState.Paid);
        return await fixture.Client.PostAsync($"/v1/billing/webhooks/fake/{tenantId}?id={providerPaymentId}", content: null);
    }

    // ---------------------------------------------------------------------------------------------------
    // Package purchase e2e (the canonical saga happy path)
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Package_purchase_completes_end_to_end_via_checkout_webhook_and_saga()
    {
        var adminToken = await EnsureAdminAsync();
        await ConfigureAdminFakeGatewayAsync(adminToken);
        var tenantId = TenantOf(adminToken);

        // Admin creates the catalogue entry (billing.manage) in its OWN tenant.
        var create = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/admin/packages", adminToken,
            new { name = $"Starter {Guid.CreateVersion7():N}", creditAmount = 500, price = 9.99, currency = "EUR", active = true, stripePriceId = "price_test_starter" }));
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var packageId = (await PlatformApiFactory.ReadData(create)).GetProperty("id").GetGuid();

        // A member of the tenant sees it in the catalogue.
        var (_, userToken) = await RegisterBuyerInAdminTenantAsync(adminToken);
        var list = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/packages", userToken));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(list)).EnumerateArray()
            .Any(p => p.GetProperty("id").GetGuid() == packageId).ShouldBeTrue();

        // Checkout on the TENANT's OWN gateway (resolved per tenant); outboxes CreditPurchaseStarted.
        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", userToken));
        checkout.StatusCode.ShouldBe(HttpStatusCode.OK);
        var checkoutData = await PlatformApiFactory.ReadData(checkout);
        var purchaseId = checkoutData.GetProperty("purchaseId").GetGuid();
        var providerPaymentId = checkoutData.GetProperty("checkoutSessionId").GetString()!;
        checkoutData.GetProperty("checkoutUrl").GetString().ShouldNotBeNullOrEmpty();

        // The Worker materializes the saga (Pending) from the outboxed start message.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}'", 1);

        // The tenant's gateway reports PAID; the per-tenant webhook re-fetches the authoritative state and confirms.
        (await ConfirmPaidViaTenantWebhookAsync(tenantId, providerPaymentId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The saga granted through the ledger (idempotency key purchase:{id}) and completed.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'", 1);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Completed'", 1);

        var purchase = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/billing/purchases/{purchaseId}", userToken));
        purchase.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(purchase)).GetProperty("status").GetString().ShouldBe("Completed");

        var balance = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/credits/balance", userToken));
        (await PlatformApiFactory.ReadData(balance)).GetProperty("available").GetInt64().ShouldBe(500);
    }

    [Fact]
    public async Task Unpaid_checkout_session_does_not_grant_credits()
    {
        var (purchaseId, providerPaymentId, tenantId) = await StartPackageCheckoutAsync(400, "price_test_unpaid");

        // A delayed payment method has not settled — the gateway still reports the payment as NOT paid. Drive the
        // per-tenant webhook WITHOUT marking it paid: the re-fetch returns a non-paid state, so nothing is granted.
        (await fixture.Client.PostAsync($"/v1/billing/webhooks/fake/{tenantId}?id={providerPaymentId}", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await Task.Delay(750); // negative settle: a regression that grants on "unpaid" would land within this window
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'")).ShouldBe(0);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Completed'")).ShouldBe(0);
    }

    [Fact]
    public async Task Async_payment_succeeded_grants_credits_on_settlement()
    {
        var (purchaseId, providerPaymentId, tenantId) = await StartPackageCheckoutAsync(350, "price_test_async");

        // The delayed payment settles: the gateway now reports PAID and the per-tenant webhook confirms the grant.
        (await ConfirmPaidViaTenantWebhookAsync(tenantId, providerPaymentId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'", 1);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Completed'", 1);
    }

    [Fact]
    public async Task Saga_timeout_abandons_pending_purchase_and_late_confirmation_still_grants()
    {
        var adminToken = await EnsureAdminAsync();
        await ConfigureAdminFakeGatewayAsync(adminToken);
        var create = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/admin/packages", adminToken,
            new { name = $"Slowpoke {Guid.CreateVersion7():N}", creditAmount = 250, price = 4.99, currency = "EUR", active = true, stripePriceId = "price_test_slow" }));
        var packageId = (await PlatformApiFactory.ReadData(create)).GetProperty("id").GetGuid();

        var (userId, userToken) = await RegisterBuyerInAdminTenantAsync(adminToken);
        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", userToken));
        var purchaseId = (await PlatformApiFactory.ReadData(checkout)).GetProperty("purchaseId").GetGuid();

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Pending'", 1);

        // Deliver the timeout NOW (the scheduled one would arrive after the configured window).
        await PublishAsync(new Sagas.CreditPurchaseTimeout(purchaseId, Minutes: 0));
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Abandoned'", 1);

        // The customer pays late: the confirmation still grants — compensation never eats money.
        await PublishAsync(new Sagas.CreditPurchaseConfirmed(
            purchaseId, userId, CreditAmount: 250, BucketExpiryDays: null, StripeEventId: $"evt_{Guid.CreateVersion7():N}"));
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'", 1);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Completed'", 1);
    }

    // ---------------------------------------------------------------------------------------------------
    // ST-1 / ST-2 FULL: ledger top-up + ProcessedAt through the seam
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Signed_topup_event_applies_ledger_topup_exactly_once_and_stamps_processed()
    {
        var (userId, userToken) = await fixture.RegisterAndLoginAsync($"topup-{Guid.CreateVersion7():N}@test.io", Password);

        var eventId = $"evt_{Guid.CreateVersion7():N}";
        Fake.SeedEvent(new Event
        {
            Id = eventId,
            Type = "payment_intent.succeeded",
            Data = new EventData
            {
                Object = new PaymentIntent
                {
                    Id = $"pi_{Guid.CreateVersion7():N}",
                    Metadata = new Dictionary<string, string>
                    {
                        ["user_id"] = userId.ToString(),
                        ["credit_amount"] = "300",
                    },
                },
            },
        });

        (await PostSignedWebhookAsync(eventId, "payment_intent.succeeded")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // FULL ST-1: the worker refetched the event through the seam, the top-up landed in the ledger and
        // the StripeEvent row is stamped processed.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = '{eventId}'", 1);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}' AND \"ProcessedAt\" IS NOT NULL", 1);

        // FULL ST-2: redelivery (fresh signature, same event id) stays exactly-once in the LEDGER.
        (await PostSignedWebhookAsync(eventId, "payment_intent.succeeded")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Task.Delay(1000);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = '{eventId}'")).ShouldBe(1);

        var balance = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/credits/balance", userToken));
        (await PlatformApiFactory.ReadData(balance)).GetProperty("available").GetInt64().ShouldBe(300);
    }

    // ---------------------------------------------------------------------------------------------------
    // Subscriptions: object-state reconcile (ST-4), per-period grant, cancel
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Subscription_lifecycle_reconciles_object_state_out_of_order_and_grants_per_invoice()
    {
        var (userId, userToken) = await fixture.RegisterAndLoginAsync($"sub-{Guid.CreateVersion7():N}@test.io", Password);
        var subId = $"sub_{Guid.CreateVersion7():N}";

        Fake.SeedSubscription(new StripeSubscriptionState(
            SubscriptionId: subId,
            Status: "active",
            CustomerId: "cus_test",
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddMonths(1),
            CancelAtPeriodEnd: false,
            Metadata: new Dictionary<string, string> { ["user_id"] = userId.ToString(), ["plan_key"] = "pro" }));

        // OUT OF ORDER: "updated" arrives with no prior "created" — the upsert reconciles from object state.
        await SendSubscriptionEventAsync("customer.subscription.updated", subId);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM subscriptions WHERE \"StripeSubscriptionId\" = '{subId}' AND \"Status\" = 'Active'", 1);

        var me = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/subscriptions/me", userToken));
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(me)).GetProperty("planKey").GetString().ShouldBe("pro");

        // invoice.paid grants the plan's per-period credits exactly once per invoice.
        var invoiceId = $"in_{Guid.CreateVersion7():N}";
        var invoiceEventId = $"evt_{Guid.CreateVersion7():N}";
        Fake.SeedEvent(new Event
        {
            Id = invoiceEventId,
            Type = "invoice.paid",
            Data = new EventData
            {
                Object = new Invoice
                {
                    Id = invoiceId,
                    Parent = new InvoiceParent
                    {
                        SubscriptionDetails = new InvoiceParentSubscriptionDetails { SubscriptionId = subId },
                    },
                },
            },
        });
        (await PostSignedWebhookAsync(invoiceEventId, "invoice.paid")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'sub-invoice:{invoiceId}'", 1);

        var balance = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/credits/balance", userToken));
        (await PlatformApiFactory.ReadData(balance)).GetProperty("available").GetInt64().ShouldBe(100);

        // Cancel: Stripe first (fake flips CancelAtPeriodEnd), local mirror eagerly honest.
        var cancel = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/cancel", userToken));
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(cancel)).GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeTrue();

        // Stripe finishes the period and sends "deleted" — object state now canceled; the mirror converges.
        Fake.SeedSubscription(new StripeSubscriptionState(
            subId, "canceled", "cus_test", DateTimeOffset.UtcNow, true,
            new Dictionary<string, string> { ["user_id"] = userId.ToString(), ["plan_key"] = "pro" }));
        await SendSubscriptionEventAsync("customer.subscription.deleted", subId);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM subscriptions WHERE \"StripeSubscriptionId\" = '{subId}' AND \"Status\" = 'Canceled'", 1);

        var meAfter = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/subscriptions/me", userToken));
        meAfter.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Subscription_plans_come_from_config_and_promo_codes_validate_through_stripe()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"plans-{Guid.CreateVersion7():N}@test.io", Password);

        var plans = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/billing/subscriptions/plans", token));
        plans.StatusCode.ShouldBe(HttpStatusCode.OK);
        var plan = (await PlatformApiFactory.ReadData(plans)).EnumerateArray()
            .Single(p => p.GetProperty("planKey").GetString() == "pro");
        plan.GetProperty("creditsPerPeriod").GetInt64().ShouldBe(100);

        Fake.SeedPromotionCode(new PromotionCodeState("SUMMER10", PercentOff: 10m, AmountOff: null, Currency: null));
        var valid = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/promo-codes/SUMMER10/validate", token));
        valid.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(valid)).GetProperty("percentOff").GetDecimal().ShouldBe(10m);

        var invalid = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/promo-codes/NOPE/validate", token));
        invalid.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Admin bootstrap: register (tolerating "already exists") + login; role granted via AdminEmails.</summary>
    private async Task<string> EnsureAdminAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue($"admin login failed: {(int)login.StatusCode}");
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>Registers + logs in a buyer that JOINS the admin's tenant (signup on the admin tenant's subdomain),
    /// so it can purchase the admin's per-tenant catalogue package (packages are tenant-scoped, bought by members).</summary>
    private async Task<(Guid UserId, string AccessToken)> RegisterBuyerInAdminTenantAsync(string adminToken)
    {
        var subdomain = await fixture.ScalarAsync<string>(
            $"SELECT \"Subdomain\" FROM tenants WHERE \"Id\" = '{TenantOf(adminToken)}'");
        var email = $"buyer-{Guid.CreateVersion7():N}@test.io";

        var register = new HttpRequestMessage(HttpMethod.Post, "/v1/identity/users")
        {
            Content = JsonContent.Create(new { email, password = Password }),
        };
        register.Headers.Host = $"{subdomain}.lvh.me"; // signup on the gym's subdomain => joins that tenant
        var registered = await fixture.Client.SendAsync(register);
        registered.EnsureSuccessStatusCode();
        var userId = (await PlatformApiFactory.ReadData(registered)).GetProperty("userId").GetGuid();

        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue();
        return (userId, (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!);
    }

    private static Guid TenantOf(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=').Replace('-', '+').Replace('_', '/');
        var claims = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
        return Guid.Parse(claims.GetProperty("tenant_id").GetString()!);
    }

    /// <summary>Admin creates a package, a fresh user checks out → returns the saga id, session id and the
    /// checkout session spec (carrying the purchase metadata) so a test can seed the confirming Stripe event.</summary>
    private async Task<(Guid PurchaseId, string ProviderPaymentId, Guid TenantId)> StartPackageCheckoutAsync(
        long creditAmount, string stripePriceId)
    {
        var adminToken = await EnsureAdminAsync();
        await ConfigureAdminFakeGatewayAsync(adminToken);
        var tenantId = TenantOf(adminToken);

        var create = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/admin/packages", adminToken,
            new { name = $"Pkg {Guid.CreateVersion7():N}", creditAmount, price = 9.99, currency = "EUR", active = true, stripePriceId }));
        var packageId = (await PlatformApiFactory.ReadData(create)).GetProperty("id").GetGuid();

        var (_, userToken) = await RegisterBuyerInAdminTenantAsync(adminToken);
        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/billing/packages/{packageId}/checkout", userToken));
        var checkoutData = await PlatformApiFactory.ReadData(checkout);
        var purchaseId = checkoutData.GetProperty("purchaseId").GetGuid();
        var providerPaymentId = checkoutData.GetProperty("checkoutSessionId").GetString()!;

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}'", 1);

        return (purchaseId, providerPaymentId, tenantId);
    }

    private async Task<string> SeedCheckoutSessionEventAsync(
        string type, string sessionId, string paymentStatus, CheckoutSessionSpec spec)
    {
        var eventId = $"evt_{Guid.CreateVersion7():N}";
        Fake.SeedEvent(new Event
        {
            Id = eventId,
            Type = type,
            Data = new EventData
            {
                Object = new global::Stripe.Checkout.Session
                {
                    Id = sessionId,
                    PaymentStatus = paymentStatus,
                    Metadata = spec.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
                },
            },
        });
        return eventId;
    }

    private async Task PublishAsync(object message)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(message);
    }

    private Task<HttpResponseMessage> SendSubscriptionEventAsync(string type, string subscriptionId)
    {
        var eventId = $"evt_{Guid.CreateVersion7():N}";
        Fake.SeedEvent(new Event
        {
            Id = eventId,
            Type = type,
            Data = new EventData { Object = new Subscription { Id = subscriptionId } },
        });
        return PostSignedWebhookAsync(eventId, type);
    }

    /// <summary>Posts a minimal signed webhook body; the worker refetches the REAL payload through the seam.</summary>
    private async Task<HttpResponseMessage> PostSignedWebhookAsync(string eventId, string type)
    {
        var json = $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{CompatibleApiVersion}}",
          "type": "{{type}}",
          "data": { "object": { "object": "unused", "metadata": {} } }
        }
        """;
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(string.Empty));
        var signature = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{json}"))).ToLowerInvariant();

        var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", $"t={t},v1={signature}");
        return await fixture.Client.SendAsync(request);
    }
}
