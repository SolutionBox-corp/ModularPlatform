namespace ModularPlatform.Web;

/// <summary>
/// Trust configuration for the <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> middleware. The forwarded client IP
/// is the basis for both the audit <c>IpAddress</c> and the per-IP auth rate-limit partition, so an EMPTY trust
/// list behind a proxy is a security hole: ASP.NET then trusts forwarded headers from ANY upstream, letting a
/// client spoof its IP to poison audit rows and dodge the brute-force limiter. Production therefore MUST declare
/// the proxies/networks it sits behind (enforced by <see cref="ForwardedHeadersSettingsValidator"/>).
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Apply the forwarded-headers middleware at all. Disable only for a host with no reverse proxy.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Exact upstream proxy IPs to trust (e.g. the load balancer addresses).</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Trusted upstream networks in CIDR form (e.g. <c>10.0.0.0/8</c>).</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>How many proxy hops to unwrap. 1 (the immediate LB) is the safe default; raise only for chained proxies.</summary>
    public int ForwardLimit { get; set; } = 1;

    public bool HasTrustList => KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}
