using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModularPlatform.Gdpr.Security;
using Shouldly;

namespace ModularPlatform.Gdpr.Tests;

public sealed class BlindIndexHasherTests
{
    [Fact]
    public void Hash_is_deterministic_for_same_normalized_value_and_changes_for_other_values()
    {
        var hasher = new HmacBlindIndexHasher(Options.Create(new GdprEncryptionOptions
        {
            BlindIndexKey = "real-blind-index-key-32-characters",
        }));

        var first = hasher.Hash("USER@EXAMPLE.COM");
        var same = hasher.Hash("USER@EXAMPLE.COM");
        var other = hasher.Hash("OTHER@EXAMPLE.COM");

        same.ShouldBe(first);
        other.ShouldNotBe(first);
        first.Length.ShouldBe(44);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData(GdprEncryptionOptions.DevKeyPlaceholder)]
    public void Validator_rejects_missing_placeholder_or_short_key_outside_development(string key)
    {
        var validator = new GdprEncryptionOptionsValidator(new StubEnvironment(Environments.Production));

        var result = validator.Validate(null, new GdprEncryptionOptions { BlindIndexKey = key });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validator_allows_dev_placeholder_in_development_only()
    {
        var validator = new GdprEncryptionOptionsValidator(new StubEnvironment(Environments.Development));

        var result = validator.Validate(null, new GdprEncryptionOptions
        {
            BlindIndexKey = GdprEncryptionOptions.DevKeyPlaceholder,
        });

        result.Succeeded.ShouldBeTrue();
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ModularPlatform.Gdpr.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
