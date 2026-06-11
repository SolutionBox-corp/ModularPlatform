using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Secrets;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class SecretProtectorTests
{
    private static string Key(byte fill) => Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray());

    private static LocalMasterKeySecretProtector Protector(params (int version, byte fill)[] keys)
    {
        var options = new SecretsOptions { ActiveKeyVersion = keys.Length == 0 ? 1 : keys[^1].version };
        foreach (var (version, fill) in keys)
        {
            options.MasterKeys[version.ToString()] = Key(fill);
        }

        return new LocalMasterKeySecretProtector(Options.Create(options));
    }

    [Fact]
    public async Task Protect_then_reveal_round_trips()
    {
        var protector = Protector((1, 9));
        var tenant = Guid.NewGuid();

        var sealed1 = await protector.ProtectAsync(tenant, "stripe.secret_key", "sk_live_secret");
        var revealed = await protector.RevealAsync(tenant, "stripe.secret_key", sealed1);

        revealed.ShouldBe("sk_live_secret");
        sealed1.KeyVersion.ShouldBe(1);
        sealed1.WrappedDek.ShouldBeNull();
    }

    [Fact]
    public async Task Reveal_with_a_different_purpose_fails_aad_authentication()
    {
        var protector = Protector((1, 9));
        var tenant = Guid.NewGuid();

        var sealed1 = await protector.ProtectAsync(tenant, "stripe.secret_key", "sk_live");

        await Should.ThrowAsync<CryptographicException>(
            () => protector.RevealAsync(tenant, "stripe.webhook_secret", sealed1));
    }

    [Fact]
    public async Task Reveal_with_a_different_tenant_fails_aad_authentication()
    {
        var protector = Protector((1, 9));

        var sealed1 = await protector.ProtectAsync(Guid.NewGuid(), "stripe.secret_key", "sk_live");

        await Should.ThrowAsync<CryptographicException>(
            () => protector.RevealAsync(Guid.NewGuid(), "stripe.secret_key", sealed1));
    }

    [Fact]
    public async Task Null_tenant_is_platform_scoped_and_round_trips()
    {
        var protector = Protector((1, 9));

        var sealed1 = await protector.ProtectAsync(null, "platform.stripe.api_key", "sk_platform");

        (await protector.RevealAsync(null, "platform.stripe.api_key", sealed1)).ShouldBe("sk_platform");
        await Should.ThrowAsync<CryptographicException>(
            () => protector.RevealAsync(Guid.NewGuid(), "platform.stripe.api_key", sealed1));
    }

    [Fact]
    public async Task A_secret_sealed_under_an_old_version_still_reveals_after_rotation()
    {
        // Seal with v1 active, then rotate: v2 active but v1 retained for old rows.
        var v1Only = Protector((1, 9));
        var tenant = Guid.NewGuid();
        var oldSecret = await v1Only.ProtectAsync(tenant, "p", "old");

        var afterRotation = Protector((1, 9), (2, 5));
        oldSecret.KeyVersion.ShouldBe(1);
        (await afterRotation.RevealAsync(tenant, "p", oldSecret)).ShouldBe("old");
        (await afterRotation.ProtectAsync(tenant, "p", "new")).KeyVersion.ShouldBe(2);
    }

    [Fact]
    public async Task Reveal_with_a_retired_key_version_throws()
    {
        var sealedUnderV1 = await Protector((1, 9)).ProtectAsync(null, "p", "x");
        var onlyV2 = Protector((2, 5));

        await Should.ThrowAsync<InvalidOperationException>(() => onlyV2.RevealAsync(null, "p", sealedUnderV1));
    }

    [Fact]
    public void Active_key_version_without_a_matching_key_throws()
    {
        var options = new SecretsOptions { ActiveKeyVersion = 3 };
        options.MasterKeys["1"] = Key(9);

        Should.Throw<InvalidOperationException>(() => new LocalMasterKeySecretProtector(Options.Create(options)));
    }

    [Fact]
    public void Validator_allows_the_dev_placeholder_in_development()
    {
        var validator = new SecretsOptionsValidator(new TestHostEnvironment("Development"));
        validator.Validate(null, new SecretsOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validator_rejects_a_missing_or_placeholder_key_outside_development()
    {
        var validator = new SecretsOptionsValidator(new TestHostEnvironment("Production"));

        validator.Validate(null, new SecretsOptions()).Failed.ShouldBeTrue();

        var placeholder = new SecretsOptions();
        placeholder.MasterKeys["1"] = SecretsOptions.DevPlaceholderMasterKey;
        validator.Validate(null, placeholder).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validator_accepts_a_real_active_key_outside_development()
    {
        var validator = new SecretsOptionsValidator(new TestHostEnvironment("Production"));
        var options = new SecretsOptions();
        options.MasterKeys["1"] = Key(42);

        validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }
}
