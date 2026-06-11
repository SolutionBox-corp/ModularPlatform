using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ModularPlatform.Billing.Security;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// The in-memory FakeStripeGateway (empty webhook signature, mints credits with no real payment) is a test-only
/// seam. Enabling it in Production must fail the host at startup; non-production environments stay exempt so the
/// integration harness and local runs work.
/// </summary>
public sealed class StripeOptionsValidatorTests
{
    [Fact]
    public void Fake_gateway_in_production_fails_validation()
    {
        var validator = new StripeOptionsValidator(new StubEnv("Production"));

        var result = validator.Validate(null, new StripeOptions { UseFakeGateway = true });

        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void Fake_gateway_outside_production_is_allowed(string environmentName)
    {
        var validator = new StripeOptionsValidator(new StubEnv(environmentName));

        var result = validator.Validate(null, new StripeOptions { UseFakeGateway = true });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Real_gateway_in_production_is_allowed()
    {
        var validator = new StripeOptionsValidator(new StubEnv("Production"));

        var result = validator.Validate(null, new StripeOptions { UseFakeGateway = false });

        result.Succeeded.ShouldBeTrue();
    }

    private sealed class StubEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
