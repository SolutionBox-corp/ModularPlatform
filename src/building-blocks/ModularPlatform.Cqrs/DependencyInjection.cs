using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ModularPlatform.Cqrs;

public static class CqrsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dispatcher and scans the given assemblies for every
    /// <see cref="ICommandHandler{TCommand,TResult}"/> and <see cref="IQueryHandler{TQuery,TResult}"/>,
    /// binding each closed interface to its implementation (scoped). Call once per module assembly.
    /// </summary>
    public static IServiceCollection AddCqrs(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.TryAddScoped<IDispatcher, Dispatcher>();

        foreach (var assembly in assemblies)
        {
            RegisterHandlers(services, assembly, typeof(ICommandHandler<,>));
            RegisterHandlers(services, assembly, typeof(IQueryHandler<,>));
        }

        return services;
    }

    /// <summary>
    /// Adds a pipeline behavior as an open generic. Registration order IS execution order
    /// (outer-most first). Behaviors implementing <see cref="ICommandOnlyBehavior"/> are skipped for queries.
    /// </summary>
    public static IServiceCollection AddPipelineBehavior(this IServiceCollection services, Type openGenericBehavior)
    {
        if (!openGenericBehavior.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"{openGenericBehavior.Name} must be an open generic type definition, e.g. typeof(ValidationBehavior<,>).",
                nameof(openGenericBehavior));
        }

        // TryAddEnumerable dedups by (serviceType, implementationType): a behavior pulled in by EACH module's
        // AddPlatformPersistence (e.g. ConcurrencyRetryBehavior) is registered ONCE, not once per module — six
        // nested retry layers would multiply into 5^6 retries on a sustained conflict. Distinct behaviors are all
        // kept; the first occurrence's position (= execution order) is preserved.
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), openGenericBehavior));
        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly, Type openHandlerInterface)
    {
        foreach (var implementation in assembly.GetTypes())
        {
            if (implementation is { IsAbstract: false, IsInterface: false })
            {
                foreach (var service in implementation.GetInterfaces())
                {
                    if (service.IsGenericType && service.GetGenericTypeDefinition() == openHandlerInterface)
                    {
                        services.AddScoped(service, implementation);
                    }
                }
            }
        }
    }
}
