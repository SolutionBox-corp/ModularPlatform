using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public void AddPlatformWeb_maps_configured_trust_list_to_forwarded_headers_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "modularplatform",
                ["Jwt:Audience"] = "modularplatform",
                ["Jwt:SigningKey"] = new string('k', 32),
                ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
                ["ForwardedHeaders:KnownNetworks:0"] = "10.10.0.0/16",
                ["ForwardedHeaders:ForwardLimit"] = "2",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production));
        services.AddPlatformWeb(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.ForwardLimit.ShouldBe(2);
        options.KnownProxies.ShouldContain(IPAddress.Parse("10.0.0.5"));
        options.KnownProxies.ShouldNotContain(IPAddress.Loopback);
        options.KnownIPNetworks.ShouldContain(n =>
            n.PrefixLength == 16
            && n.Contains(IPAddress.Parse("10.10.25.4"))
            && !n.Contains(IPAddress.Parse("10.11.0.1")));
    }
}
