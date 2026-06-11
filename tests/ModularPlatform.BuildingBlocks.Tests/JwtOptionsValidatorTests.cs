using Microsoft.Extensions.Hosting;
using ModularPlatform.Web;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class JwtOptionsValidatorTests
{
    private static JwtOptions Options(string? key = null, string issuer = "modularplatform", string audience = "modularplatform")
        => new()
        {
            SigningKey = key ?? new string('k', 32), // exactly the 32-byte minimum
            Issuer = issuer,
            Audience = audience,
        };

    [Fact]
    public void Production_with_a_missing_signing_key_fails_fast()
    {
        var validator = new JwtOptionsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(null, Options(key: ""));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("SigningKey");
    }

    [Fact]
    public void Production_with_a_too_short_signing_key_fails_fast()
    {
        var validator = new JwtOptionsValidator(new TestHostEnvironment(Environments.Production));

        validator.Validate(null, Options(key: new string('k', JwtOptionsValidator.MinimumKeyBytes - 1)))
            .Failed.ShouldBeTrue();
    }

    [Fact]
    public void Production_requires_issuer_and_audience()
    {
        var validator = new JwtOptionsValidator(new TestHostEnvironment(Environments.Production));

        validator.Validate(null, Options(issuer: "")).Failed.ShouldBeTrue();
        validator.Validate(null, Options(audience: "")).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Production_with_a_complete_config_succeeds()
    {
        var validator = new JwtOptionsValidator(new TestHostEnvironment(Environments.Production));

        validator.Validate(null, Options()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Development_is_exempt_even_with_an_empty_key()
    {
        var validator = new JwtOptionsValidator(new TestHostEnvironment(Environments.Development));

        validator.Validate(null, Options(key: "")).Succeeded.ShouldBeTrue();
    }
}
