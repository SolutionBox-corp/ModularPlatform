using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Messaging;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Contracts;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Verifies the cross-module event flow end-to-end: registering a user in Identity publishes
/// <c>UserRegisteredIntegrationEvent</c> via the outbox; Billing's handler consumes it and idempotently
/// provisions a credit account. If Wolverine handler discovery or the durable relay is broken, no account
/// ever appears — this locks that down.
/// </summary>
[Collection("Integration")]
public sealed class CrossModuleEventTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Registering_a_user_provisions_a_credit_account_via_the_event()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync(
            $"event-{Guid.CreateVersion7():N}@example.com", "Sup3rSecret!");

        long accounts = 0;
        for (var attempt = 0; attempt < 60 && accounts == 0; attempt++)
        {
            accounts = await fixture.ScalarAsync<long>(
                $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'");
            if (accounts == 0)
            {
                await Task.Delay(500);
            }
        }

        accounts.ShouldBe(1);

        // The account carries the user's tenant — stamped EXPLICITLY by the provisioning handler from the event,
        // because the Worker runs in the SYSTEM context where the tenant-stamping interceptor does not fire.
        var userTenant = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
        var accountTenant = await fixture.ScalarAsync<Guid>(
            $"SELECT \"TenantId\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        accountTenant.ShouldBe(userTenant);

        // The same UserRegisteredIntegrationEvent has multiple subscribers. Billing provisions the account and
        // Notifications creates the welcome row; each subscriber must stay idempotent because Wolverine retries the
        // whole event envelope together in the current combined-handler mode.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM notifications WHERE \"UserId\" = '{userId}' AND \"TemplateKey\" = 'welcome'",
            1);
    }

    [Fact]
    public async Task Public_event_handler_dispatches_internal_provision_command_idempotently()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var message = new UserRegisteredIntegrationEvent(
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow,
            userId,
            tenantId,
            $"provision-{Guid.CreateVersion7():N}@example.com",
            DisplayName: null);

        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var handler = new ProvisionCreditAccountHandler();

        await handler.Handle(message, dispatcher, CancellationToken.None);
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_accounts SET \"Posted\" = 123, \"Available\" = 123 WHERE \"UserId\" = '{userId}'");

        await handler.Handle(message, dispatcher, CancellationToken.None);

        var accounts = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        accounts.ShouldBe(1);
        var posted = await fixture.ScalarAsync<long>(
            $"SELECT \"Posted\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        posted.ShouldBe(123);
        var accountTenant = await fixture.ScalarAsync<Guid>(
            $"SELECT \"TenantId\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        accountTenant.ShouldBe(tenantId);
    }
}
