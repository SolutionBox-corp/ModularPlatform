using ModularPlatform.IntegrationTesting;

namespace ModularPlatform.Operations.Tests;

/// <summary>Shares ONE <see cref="PlatformApiFactory"/> across this assembly's integration tests (one container).</summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<PlatformApiFactory>;
