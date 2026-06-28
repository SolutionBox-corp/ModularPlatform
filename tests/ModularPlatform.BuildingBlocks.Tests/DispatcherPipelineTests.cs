using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Cqrs;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class DispatcherPipelineTests
{
    [Fact]
    public async Task Command_pipeline_runs_behaviors_in_registration_order()
    {
        var recorder = new PipelineRecorder();
        await using var provider = BuildProvider(recorder);

        var result = await provider.GetRequiredService<IDispatcher>()
            .Send(new TestCommand());

        result.ShouldBe("command");
        recorder.Events.ShouldBe([
            "first:before",
            "second:before",
            "command-only:before",
            "handler:command",
            "command-only:after",
            "second:after",
            "first:after"
        ]);
    }

    [Fact]
    public async Task Query_pipeline_skips_command_only_behaviors_but_preserves_order()
    {
        var recorder = new PipelineRecorder();
        await using var provider = BuildProvider(recorder);

        var result = await provider.GetRequiredService<IDispatcher>()
            .Query(new TestQuery());

        result.ShouldBe("query");
        recorder.Events.ShouldBe([
            "first:before",
            "second:before",
            "handler:query",
            "second:after",
            "first:after"
        ]);
    }

    private static ServiceProvider BuildProvider(PipelineRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddCqrs(typeof(DispatcherPipelineTests).Assembly);
        services.AddPipelineBehavior(typeof(FirstBehavior<,>));
        services.AddPipelineBehavior(typeof(SecondBehavior<,>));
        services.AddPipelineBehavior(typeof(CommandOnlyBehavior<,>));
        return services.BuildServiceProvider();
    }

    public sealed record TestCommand : ICommand<string>;

    public sealed record TestQuery : IQuery<string>;

    public sealed class TestCommandHandler(PipelineRecorder recorder) : ICommandHandler<TestCommand, string>
    {
        public Task<string> Handle(TestCommand command, CancellationToken ct)
        {
            recorder.Events.Add("handler:command");
            return Task.FromResult("command");
        }
    }

    public sealed class TestQueryHandler(PipelineRecorder recorder) : IQueryHandler<TestQuery, string>
    {
        public Task<string> Handle(TestQuery query, CancellationToken ct)
        {
            recorder.Events.Add("handler:query");
            return Task.FromResult("query");
        }
    }

    public sealed class PipelineRecorder
    {
        public List<string> Events { get; } = [];
    }

    public abstract class RecordingBehavior<TRequest, TResponse>(
        PipelineRecorder recorder,
        string name) : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken ct)
        {
            recorder.Events.Add($"{name}:before");
            var response = await next();
            recorder.Events.Add($"{name}:after");
            return response;
        }
    }

    public sealed class FirstBehavior<TRequest, TResponse>(PipelineRecorder recorder)
        : RecordingBehavior<TRequest, TResponse>(recorder, "first");

    public sealed class SecondBehavior<TRequest, TResponse>(PipelineRecorder recorder)
        : RecordingBehavior<TRequest, TResponse>(recorder, "second");

    public sealed class CommandOnlyBehavior<TRequest, TResponse>(PipelineRecorder recorder)
        : RecordingBehavior<TRequest, TResponse>(recorder, "command-only"), ICommandOnlyBehavior;
}
