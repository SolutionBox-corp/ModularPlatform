using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Behaviors;
using Xunit;

namespace ModularPlatform.ArchitectureTests;

/// <summary>
/// Pipeline-behavior registration invariants. Behaviors are an open-generic chain resolved as
/// <see cref="IPipelineBehavior{TRequest,TResponse}"/>; a behavior registered once PER MODULE would nest N times
/// and multiply its effect — for the retry behavior that means N-deep nested retries (5^N amplification on a
/// sustained conflict). Registration must therefore be idempotent across the per-module AddPlatformPersistence calls.
/// </summary>
public sealed class PipelineBehaviorRegistrationTests
{
    [Fact]
    public void ConcurrencyRetryBehavior_is_registered_once_regardless_of_module_count()
    {
        var services = new ServiceCollection();

        // Each module's AddModuleDbContext calls AddPlatformPersistence — simulate a six-module platform.
        for (var i = 0; i < 6; i++)
        {
            services.AddPlatformPersistence();
        }

        var registrations = services.Count(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(ConcurrencyRetryBehavior<,>));

        Assert.Equal(1, registrations);
    }
}
