using Microsoft.Extensions.Configuration;

namespace ModularPlatform.Persistence;

public static class ModuleConnectionStrings
{
    public static (string Write, string Read) GetWriteAndRead(IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");

        var configuredRead = configuration.GetConnectionString("Read");
        var read = string.IsNullOrWhiteSpace(configuredRead) ? write : configuredRead;

        return (write, read);
    }
}
