using ModularPlatform.IntegrationTesting;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Shares ONE <see cref="PlatformApiFactory"/> (one Testcontainers Postgres + one Api host) across the
/// integration test classes that use it, instead of a container per class. Tests use unique emails so a
/// shared host/DB is safe.
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<PlatformApiFactory>;
