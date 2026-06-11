using ModularPlatform.Persistence.Audit;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class AuditIpMaskingTests
{
    [Fact]
    public void Full_keeps_the_address_verbatim()
    {
        AuditIpMasking.Apply("203.0.113.42", AuditIpStorageMode.Full).ShouldBe("203.0.113.42");
        AuditIpMasking.Apply("2001:db8:1:2:3:4:5:6", AuditIpStorageMode.Full).ShouldBe("2001:db8:1:2:3:4:5:6");
    }

    [Fact]
    public void None_drops_the_address()
    {
        AuditIpMasking.Apply("203.0.113.42", AuditIpStorageMode.None).ShouldBeNull();
    }

    [Fact]
    public void Truncated_zeroes_the_ipv4_host_octet()
    {
        AuditIpMasking.Apply("203.0.113.42", AuditIpStorageMode.Truncated).ShouldBe("203.0.113.0");
    }

    [Fact]
    public void Truncated_keeps_only_the_ipv6_routing_prefix()
    {
        // /48: first three hextets survive, the rest are zeroed.
        AuditIpMasking.Apply("2001:db8:abcd:1234:5678:9abc:def0:1111", AuditIpStorageMode.Truncated)
            .ShouldBe("2001:db8:abcd::");
    }

    [Fact]
    public void Truncated_normalizes_an_ipv4_mapped_ipv6_and_applies_the_ipv4_rule()
    {
        // Dual-stack Kestrel reports IPv4 clients as ::ffff:a.b.c.d — must mask as IPv4 /24, not collapse to "::".
        AuditIpMasking.Apply("::ffff:203.0.113.77", AuditIpStorageMode.Truncated).ShouldBe("203.0.113.0");
    }

    [Fact]
    public void Truncated_drops_an_unparseable_or_empty_address()
    {
        AuditIpMasking.Apply("not-an-ip", AuditIpStorageMode.Truncated).ShouldBeNull();
        AuditIpMasking.Apply(null, AuditIpStorageMode.Truncated).ShouldBeNull();
        AuditIpMasking.Apply("", AuditIpStorageMode.Truncated).ShouldBe("");
    }
}
