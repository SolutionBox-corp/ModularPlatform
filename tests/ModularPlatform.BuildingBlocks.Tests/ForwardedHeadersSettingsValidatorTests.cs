using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModularPlatform.Web;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class ForwardedHeadersSettingsValidatorTests
{
    [Fact]
    public void Production_with_an_empty_trust_list_fails_fast()
    {
        var validator = new ForwardedHeadersSettingsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(null, new ForwardedHeadersSettings { Enabled = true });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("KnownProxies");
    }

    [Fact]
    public void Production_with_a_known_proxy_succeeds()
    {
        var validator = new ForwardedHeadersSettingsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(null, new ForwardedHeadersSettings
        {
            Enabled = true,
            KnownProxies = ["10.0.0.5"],
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Production_with_a_known_network_succeeds()
    {
        var validator = new ForwardedHeadersSettingsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(null, new ForwardedHeadersSettings
        {
            Enabled = true,
            KnownNetworks = ["10.0.0.0/8"],
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Production_with_the_middleware_disabled_succeeds()
    {
        var validator = new ForwardedHeadersSettingsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(null, new ForwardedHeadersSettings { Enabled = false });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Development_with_an_empty_trust_list_is_exempt()
    {
        var validator = new ForwardedHeadersSettingsValidator(new TestHostEnvironment(Environments.Development));

        var result = validator.Validate(null, new ForwardedHeadersSettings { Enabled = true });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_malformed_proxy_ip_fails_in_any_environment()
    {
        var validator = new ForwardedHeadersSettingsValidator(new TestHostEnvironment(Environments.Development));

        var result = validator.Validate(null, new ForwardedHeadersSettings
        {
            Enabled = true,
            KnownProxies = ["10.0.0.x"],
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("10.0.0.x");
    }

    [Fact]
    public void A_malformed_network_cidr_fails()
    {
        var validator = new ForwardedHeadersSettingsValidator(new TestHostEnvironment(Environments.Production));

        var result = validator.Validate(null, new ForwardedHeadersSettings
        {
            Enabled = true,
            KnownNetworks = ["10.0.0.0/99"], // prefix length out of range for IPv4
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("10.0.0.0/99");
    }
}
