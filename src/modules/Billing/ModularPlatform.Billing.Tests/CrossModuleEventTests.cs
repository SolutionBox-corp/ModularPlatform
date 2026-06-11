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
    }
}
