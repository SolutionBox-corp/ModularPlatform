using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Npgsql;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class ReadDbContextFactoryTests
{
    [Fact]
    public void AddModuleReadDbContext_uses_runtime_role_and_stamps_principal_when_rls_is_enabled()
    {
        const string adminConnectionString = "Host=localhost;Port=5432;Database=modularplatform;Username=admin;Password=admin-secret";
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new TestTenantContext(Guid.CreateVersion7(), Guid.CreateVersion7()));
        services.AddOptions<RlsOptions>().Configure(o =>
        {
            o.Enabled = true;
            o.RuntimeRole = "app_rls_test";
            o.RuntimePassword = "runtime-secret";
        });
        services.AddModuleReadDbContext<TestReadDbContext>(adminConnectionString);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IReadDbContextFactory<TestReadDbContext>>();

        using var db = factory.Create();

        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        connection.Username.ShouldBe("app_rls_test");
        connection.Password.ShouldBe("runtime-secret");
        db.ChangeTracker.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.NoTracking);

        var interceptors = db.GetService<IDbContextOptions>()
            .Extensions
            .OfType<CoreOptionsExtension>()
            .SelectMany(e => e.Interceptors ?? []);

        interceptors.ShouldContain(i => i is PrincipalSessionConnectionInterceptor);
        interceptors.ShouldContain(i => i.GetType().Name == "ReadOnlyGuardInterceptor");
    }

    [Fact]
    public void AddModuleReadDbContext_keeps_admin_connection_and_skips_principal_interceptor_when_rls_is_disabled()
    {
        const string adminConnectionString = "Host=localhost;Port=5432;Database=modularplatform;Username=admin;Password=admin-secret";
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new TestTenantContext(Guid.CreateVersion7(), Guid.CreateVersion7()));
        services.AddOptions<RlsOptions>().Configure(o =>
        {
            o.Enabled = false;
            o.RuntimeRole = "app_rls_test";
            o.RuntimePassword = "runtime-secret";
        });
        services.AddModuleReadDbContext<TestReadDbContext>(adminConnectionString);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IReadDbContextFactory<TestReadDbContext>>();

        using var db = factory.Create();

        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        connection.Username.ShouldBe("admin");
        connection.Password.ShouldBe("admin-secret");
        db.ChangeTracker.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.NoTracking);

        var interceptors = db.GetService<IDbContextOptions>()
            .Extensions
            .OfType<CoreOptionsExtension>()
            .SelectMany(e => e.Interceptors ?? []);

        interceptors.ShouldNotContain(i => i is PrincipalSessionConnectionInterceptor);
        interceptors.ShouldContain(i => i.GetType().Name == "ReadOnlyGuardInterceptor");
    }

    private sealed class TestTenantContext(Guid userId, Guid tenantId) : ITenantContext
    {
        public Guid? UserId => userId;
        public Guid? TenantId => tenantId;
        public bool IsSystem => false;
        public string? IpAddress => "127.0.0.1";
    }

    private sealed class TestReadDbContext(DbContextOptions<TestReadDbContext> options, ITenantContext tenant)
        : PlatformDbContext(options, tenant)
    {
        public override string ModuleName => "TestRead";
    }
}
