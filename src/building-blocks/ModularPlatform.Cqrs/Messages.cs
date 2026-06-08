namespace ModularPlatform.Cqrs;

/// <summary>
/// A command mutates state. Commands flow through the full pipeline (validation,
/// idempotency, transaction + outbox, concurrency-retry) and are the only messages
/// allowed to open a transaction or publish events.
/// </summary>
public interface ICommand<out TResult>;

/// <summary>A command that returns no value.</summary>
public interface ICommand : ICommand<Unit>;

/// <summary>
/// A query reads state. Queries never open a transaction, never touch the bus,
/// and run only the read-safe behaviors (telemetry, logging, validation).
/// </summary>
public interface IQuery<out TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}

/// <summary>Convenience base for commands returning <see cref="Unit"/>.</summary>
public interface ICommandHandler<in TCommand> : ICommandHandler<TCommand, Unit>
    where TCommand : ICommand<Unit>;

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting step wrapping a handler. Registered as an open generic; DI resolution
/// order is execution order (outer-most first). Behaviors that must only run for commands
/// also implement <see cref="ICommandOnlyBehavior"/> so the dispatcher skips them for queries.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

/// <summary>
/// Marker for behaviors that must run ONLY for commands (idempotency, transaction+outbox,
/// concurrency-retry). The dispatcher filters these out of the query pipeline.
/// </summary>
public interface ICommandOnlyBehavior;
