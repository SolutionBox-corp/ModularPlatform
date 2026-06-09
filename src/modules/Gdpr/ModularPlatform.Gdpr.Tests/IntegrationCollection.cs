using ModularPlatform.IntegrationTesting;

namespace ModularPlatform.Gdpr.Tests;

/// <summary>
/// Shares ONE <see cref="PlatformApiFactory"/> (one Testcontainers Postgres + one Api host) across every
/// integration test class in this assembly — running a container + full host per class exhausts resources and
/// flakes under parallelism. Tests use unique emails/keys, so a shared host/DB is safe.
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<PlatformApiFactory>;
