namespace ModularPlatform.Hosts.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OutOfProcessWorkerCollection
{
    public const string Name = "OutOfProcessWorker";
}
