using JasperFx.CodeGeneration.Model;
using ModularPlatform.Messaging;
using Shouldly;
using Wolverine;
using Wolverine.Runtime.Routing;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class PlatformMessagingPolicyTests
{
    private const string PostgresConnectionString = "Host=localhost;Database=modularplatform;Username=postgres;Password=postgres";

    [Fact]
    public void Multiple_subscribers_are_combined_until_we_make_an_explicit_separated_decision()
    {
        var options = new WolverineOptions();

        PlatformMessaging.Configure(
            options,
            PostgresConnectionString,
            modules: [],
            soloMode: true,
            listen: true);

        options.MultipleHandlerBehavior.ShouldBe(MultipleHandlerBehavior.ClassicCombineIntoOneLogicalHandler);
        options.ServiceLocationPolicy.ShouldBe(ServiceLocationPolicy.AlwaysAllowed);
        options.Durability.DeadLetterQueueExpirationEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Durable_queue_polling_is_fast_enough_for_event_driven_work()
    {
        var options = new WolverineOptions();

        PlatformMessaging.Configure(
            options,
            PostgresConnectionString,
            modules: []);

        options.Durability.ScheduledJobPollingTime.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Solo_mode_is_enabled_only_for_single_node_hosts()
    {
        var solo = new WolverineOptions();
        PlatformMessaging.Configure(
            solo,
            PostgresConnectionString,
            modules: [],
            soloMode: true);

        solo.Durability.Mode.ShouldBe(DurabilityMode.Solo);

        var balanced = new WolverineOptions();
        PlatformMessaging.Configure(
            balanced,
            PostgresConnectionString,
            modules: [],
            soloMode: false);

        balanced.Durability.Mode.ShouldBe(DurabilityMode.Balanced);
    }
}
