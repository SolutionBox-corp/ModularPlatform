using ModularPlatform.IntegrationTesting;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// Shares ONE <see cref="PlatformApiFactory"/> (one Testcontainers Postgres + one Api host) across the CRM
/// integration test classes. Tests use unique users so a shared host/DB is safe.
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<PlatformApiFactory>;
