using System.Diagnostics;
using ModularPlatform.Cqrs;
using ModularPlatform.Telemetry;
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

    private sealed record TestCommand;
}
