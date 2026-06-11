using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ModularPlatform.BuildingBlocks.Tests;

/// <summary>Minimal <see cref="IHostEnvironment"/> stub so option validators can be unit-tested per environment.</summary>
internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
