using System.Net;
using System.Net.Sockets;

namespace ModularPlatform.Persistence.Audit;

/// <summary>How much of the client IP is recorded on each audit row.</summary>
public enum AuditIpStorageMode
{
    /// <summary>Store the full address (max forensic value; full personal data at rest).</summary>
    Full = 0,

    /// <summary>Store a network-truncated address (IPv4 /24, IPv6 /48) — GDPR data-minimization, coarse attribution.</summary>
    Truncated = 1,

    /// <summary>Store no IP at all (<c>null</c>).</summary>
    None = 2,
}

/// <summary>
/// Audit data-minimization policy. The client IP on an audit row is personal data; a deployment chooses how much
/// to keep — full (forensics) vs network-truncated (privacy) vs none — without code changes. Bound from the
/// <c>Audit</c> configuration section. Default <see cref="AuditIpStorageMode.Full"/> (this is a security base; an
/// operator opts into minimization). Note: only the AUDIT IP is masked; the rate-limit partition keeps the full
/// address (truncation there would collapse 256 clients into one bucket).
/// </summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    public AuditIpStorageMode IpStorage { get; set; } = AuditIpStorageMode.Full;
}

/// <summary>Pure IP-masking applied to the audit IP per <see cref="AuditIpStorageMode"/>.</summary>
public static class AuditIpMasking
{
    public static string? Apply(string? ip, AuditIpStorageMode mode)
    {
        if (mode == AuditIpStorageMode.None)
        {
            return null;
        }

        if (mode == AuditIpStorageMode.Full || string.IsNullOrEmpty(ip))
        {
            return ip;
        }

        // Truncated: unparseable input is dropped rather than stored verbatim (it would defeat minimization).
        if (!IPAddress.TryParse(ip, out var address))
        {
            return null;
        }

        // A dual-stack Kestrel socket reports IPv4 clients as IPv4-mapped IPv6 (::ffff:a.b.c.d). The real IPv4 lives
        // in the last 4 bytes — inside the /48 zeroing range — so without this it would collapse to "::" and destroy
        // all attribution. Normalize to IPv4 first, then apply the /24 rule.
        if (address is { AddressFamily: AddressFamily.InterNetworkV6, IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork: // IPv4 -> /24 (zero the host octet)
                bytes[3] = 0;
                break;
            case AddressFamily.InterNetworkV6: // IPv6 -> /48 (keep the routing prefix, zero the rest)
                for (var i = 6; i < bytes.Length; i++)
                {
                    bytes[i] = 0;
                }

                break;
            default:
                return null;
        }

        return new IPAddress(bytes).ToString();
    }
}
