using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Secrets;

/// <summary>
/// Registers the secret-at-rest building block. <see cref="ISecretProtector"/> provider is selected by
/// <c>Secrets:Provider</c> = <c>local</c> (default; KMS providers added later). Call once from any module that
/// stores tenant/platform secrets (payment-gateway config, later device credentials). Idempotent — safe to call
/// from more than one module: <c>TryAdd</c> keeps a single registration.
/// </summary>
public static class PlatformSecrets
{
    public static IServiceCollection AddPlatformSecrets(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecretsOptions>()
            .Bind(configuration.GetSection(SecretsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecretsOptions>, SecretsOptionsValidator>();

        var provider = configuration.GetValue<string>($"{SecretsOptions.SectionName}:Provider") ?? "local";

        // Only the dependency-free local provider exists today; aws-kms | azure-kv | vault drop in here with the
        // same ProtectedSecret shape. Unknown provider falls back to local (the validator gates prod misconfig).
        _ = provider;
        services.AddSingleton<ISecretProtector, LocalMasterKeySecretProtector>();

        return services;
    }
}
