using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ModularPlatform.Web;

/// <summary>
/// Fails the host at startup (<c>ValidateOnStart</c>) if the forwarded-headers middleware is enabled in Production
/// with NO trust list. An empty <c>KnownProxies</c>/<c>KnownNetworks</c> tells ASP.NET to trust forwarded headers
/// from every caller, so a direct client could spoof <c>X-Forwarded-For</c> to forge its audit IP and shard the
/// auth rate-limiter across fake IPs. Development is exempt (no proxy locally; loopback defaults stay in effect).
/// Mirrors <see cref="JwtOptionsValidator"/>.
/// </summary>
public sealed class ForwardedHeadersSettingsValidator(IHostEnvironment environment)
    : IValidateOptions<ForwardedHeadersSettings>
{
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
    {
        // Malformed trust-list entries are a config error in ANY environment — catch them here with a clear message
        // instead of letting IPAddress/IPNetwork.Parse throw an opaque FormatException deep in middleware setup.
        var badProxy = options.KnownProxies.FirstOrDefault(p => !IPAddress.TryParse(p, out _));
        if (badProxy is not null)
        {
            return ValidateOptionsResult.Fail($"ForwardedHeaders:KnownProxies has an invalid IP address: '{badProxy}'.");
        }

        var badNetwork = options.KnownNetworks.FirstOrDefault(n => !IPNetwork.TryParse(n, out _));
        if (badNetwork is not null)
        {
            return ValidateOptionsResult.Fail($"ForwardedHeaders:KnownNetworks has an invalid CIDR network: '{badNetwork}'.");
        }

        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (options.Enabled && !options.HasTrustList)
        {
            return ValidateOptionsResult.Fail(
                "ForwardedHeaders:KnownProxies or :KnownNetworks must be configured outside Development — an empty "
                + "trust list trusts forwarded headers from any upstream, letting clients spoof their IP (poisoning "
                + "audit rows and the auth rate-limiter). Set the load balancer's address/network, or set "
                + "ForwardedHeaders:Enabled=false for a host with no reverse proxy.");
        }

        return ValidateOptionsResult.Success;
    }
}
