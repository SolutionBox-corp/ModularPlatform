using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModularPlatform.Cqrs;
using ModularPlatform.Cqrs.Behaviors;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Behaviors;
using ModularPlatform.Telemetry;
using ModularPlatform.Web;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class TelemetryBehaviorTests
{
    [Fact]
    public async Task ModularPlatformException_tags_activity_with_error_code()
    {
        Activity? stopped = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PlatformTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped = activity,
        };
        ActivitySource.AddActivityListener(listener);

        var behavior = new TelemetryBehavior<TestCommand, Unit>();
        var thrown = await Should.ThrowAsync<BusinessRuleException>(() =>
            behavior.Handle(
                new TestCommand(),
                () => throw new BusinessRuleException("test.rule_broken", "Rule broken."),
                CancellationToken.None));

        thrown.ErrorCode.ShouldBe("test.rule_broken");
        stopped.ShouldNotBeNull();
        stopped.OperationName.ShouldBe("cqrs TestCommand");
        stopped.Status.ShouldBe(ActivityStatusCode.Error);
        stopped.StatusDescription.ShouldBe("test.rule_broken");
        stopped.Tags.ShouldContain(t => t.Key == "cqrs.request" && t.Value == nameof(TestCommand));
        stopped.Tags.ShouldContain(t => t.Key == "cqrs.error_code" && t.Value == "test.rule_broken");
    }

    [Fact]
    public void Platform_registration_keeps_telemetry_behavior_outer_most()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Development));

        services.AddPlatformTelemetry("Tests", configuration, new TestHostEnvironment(Environments.Development));
        services.AddPlatformWeb(configuration);
        services.AddPlatformPersistence();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToArray();

        behaviors.ShouldBe([
            typeof(TelemetryBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(ConcurrencyRetryBehavior<,>)
        ]);
    }

    [Fact]
    public void Platform_telemetry_requires_an_explicit_otlp_endpoint_outside_development()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestHostEnvironment(Environments.Production);

        var exception = Should.Throw<InvalidOperationException>(
            () => services.AddPlatformTelemetry("Tests", configuration, environment));

        exception.Message.ShouldContain("OpenTelemetry OTLP endpoint must be configured");
        exception.Message.ShouldContain("OpenTelemetry:Otlp:Endpoint");
    }

    [Fact]
    public void Platform_telemetry_accepts_a_configured_otlp_endpoint_outside_development()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Otlp:Endpoint"] = "http://collector:4317",
            })
            .Build();
        var environment = new TestHostEnvironment(Environments.Production);

        services.AddPlatformTelemetry("Tests", configuration, environment);

        services.ShouldContain(d => d.ServiceType == typeof(IPipelineBehavior<,>)
            && d.ImplementationType == typeof(TelemetryBehavior<,>));
    }

    private sealed record TestCommand;
}
