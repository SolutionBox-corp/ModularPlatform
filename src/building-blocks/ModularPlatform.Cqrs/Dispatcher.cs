using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace ModularPlatform.Cqrs;

/// <summary>
/// Reflection-light dispatcher: it resolves a cached typed wrapper per concrete request type,
/// which then resolves the handler + behavior chain from DI. No per-call reflection on the hot path
/// beyond a dictionary lookup. ~Mediator behavior in ~150 LOC, fully owned and debuggable.
/// </summary>
internal sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> CommandWrappers = new();
    private static readonly ConcurrentDictionary<Type, object> QueryWrappers = new();

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (CommandWrapperBase<TResult>)CommandWrappers.GetOrAdd(
            command.GetType(),
            static t => Activator.CreateInstance(
                typeof(CommandWrapper<,>).MakeGenericType(t, typeof(TResult)))!);
        return wrapper.Handle(command, provider, ct);
    }

    public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var wrapper = (QueryWrapperBase<TResult>)QueryWrappers.GetOrAdd(
            query.GetType(),
            static t => Activator.CreateInstance(
                typeof(QueryWrapper<,>).MakeGenericType(t, typeof(TResult)))!);
        return wrapper.Handle(query, provider, ct);
    }

    private abstract class CommandWrapperBase<TResult>
    {
        public abstract Task<TResult> Handle(object command, IServiceProvider provider, CancellationToken ct);
    }

    private sealed class CommandWrapper<TCommand, TResult> : CommandWrapperBase<TResult>
        where TCommand : ICommand<TResult>
    {
        public override Task<TResult> Handle(object command, IServiceProvider provider, CancellationToken ct)
        {
            var typed = (TCommand)command;
            var handler = provider.GetRequiredService<ICommandHandler<TCommand, TResult>>();

            RequestHandlerDelegate<TResult> pipeline = () => handler.Handle(typed, ct);

            // Outer-most behavior runs first -> fold in reverse so registration order == execution order.
            var behaviors = provider.GetServices<IPipelineBehavior<TCommand, TResult>>().ToArray();
            for (var i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var next = pipeline;
                pipeline = () => behavior.Handle(typed, next, ct);
            }

            return pipeline();
        }
    }

    private abstract class QueryWrapperBase<TResult>
    {
        public abstract Task<TResult> Handle(object query, IServiceProvider provider, CancellationToken ct);
    }

    private sealed class QueryWrapper<TQuery, TResult> : QueryWrapperBase<TResult>
        where TQuery : IQuery<TResult>
    {
        public override Task<TResult> Handle(object query, IServiceProvider provider, CancellationToken ct)
        {
            var typed = (TQuery)query;
            var handler = provider.GetRequiredService<IQueryHandler<TQuery, TResult>>();

            RequestHandlerDelegate<TResult> pipeline = () => handler.Handle(typed, ct);

            // Queries skip command-only behaviors (idempotency, transaction+outbox, concurrency-retry).
            var behaviors = provider.GetServices<IPipelineBehavior<TQuery, TResult>>()
                .Where(b => b is not ICommandOnlyBehavior)
                .ToArray();
            for (var i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var next = pipeline;
                pipeline = () => behavior.Handle(typed, next, ct);
            }

            return pipeline();
        }
    }
}
