using JasperFx.CodeGeneration.Model;
using ModularPlatform.Messaging;
using Shouldly;
using Wolverine;
using Wolverine.Runtime.Routing;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class PlatformMessagingPolicyTests
{
    [Fact]
    public void Multiple_subscribers_are_combined_until_we_make_an_explicit_separated_decision()
    {
        var options = new WolverineOptions();

        PlatformMessaging.Configure(
            options,
            "Host=localhost;Database=modularplatform;Username=postgres;Password=postgres",
            modules: [],
            soloMode: true,
            listen: true);

        options.MultipleHandlerBehavior.ShouldBe(MultipleHandlerBehavior.ClassicCombineIntoOneLogicalHandler);
        options.ServiceLocationPolicy.ShouldBe(ServiceLocationPolicy.AlwaysAllowed);
        options.Durability.DeadLetterQueueExpirationEnabled.ShouldBeTrue();
    }
}
