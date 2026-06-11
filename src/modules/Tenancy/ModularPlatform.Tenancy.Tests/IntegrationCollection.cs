using ModularPlatform.IntegrationTesting;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// Shares ONE <see cref="PlatformApiFactory"/> (one Testcontainers Postgres + one Api host) across this
/// assembly's integration test classes. Tests use unique emails/subdomains so a shared host/DB is safe.
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<PlatformApiFactory>;
