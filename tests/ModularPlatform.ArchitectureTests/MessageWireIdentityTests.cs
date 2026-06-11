using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModularPlatform.Cqrs;
using Xunit;

namespace ModularPlatform.ArchitectureTests;

/// <summary>
/// Freezes the WIRE IDENTITY of every cross-module integration event. Wolverine identifies a durable message by
/// its .NET full type name by default (no <c>[MessageIdentity]</c> alias — adding one would force a WolverineFx
/// reference into <c>*.Contracts</c>, which the boundary law forbids: Contracts may reference only Cqrs). So
/// renaming an event type OR moving its namespace silently changes its on-the-wire name and ORPHANS every
/// in-flight / scheduled durable envelope of the old name across a rolling deploy (a saga timeout can hang the
/// whole checkout window). This snapshot makes such a change a deliberate, reviewed act: if it fails, you renamed
/// or moved an integration event — restore the name, or knowingly accept the wire break and update the snapshot
/// (and plan to drain the old envelopes before deploying).
/// </summary>
public sealed class MessageWireIdentityTests
{
    // The frozen wire names. ADD a new event here when you introduce one; never silently RENAME/MOVE an existing one.
    private static readonly HashSet<string> FrozenWireNames =
    [
        "ModularPlatform.Billing.Contracts.CreditPurchaseCompletedIntegrationEvent",
        "ModularPlatform.Billing.Contracts.CreditsSpentIntegrationEvent",
        "ModularPlatform.Billing.Contracts.CreditsToppedUpIntegrationEvent",
        "ModularPlatform.Billing.Contracts.SubscriptionActivatedIntegrationEvent",
        "ModularPlatform.Billing.Contracts.SubscriptionCanceledIntegrationEvent",
        "ModularPlatform.Gdpr.Contracts.UserErasureRequested",
        "ModularPlatform.Identity.Contracts.UserRegisteredIntegrationEvent",
        "ModularPlatform.Notifications.Contracts.EmailDeliveryRequested",
        "ModularPlatform.Notifications.Contracts.PushDeliveryRequested",
    ];

    // Anchor types whose assemblies hold the integration-event contracts.
    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(ModularPlatform.Identity.Contracts.UserRegisteredIntegrationEvent).Assembly,
        typeof(ModularPlatform.Billing.Contracts.CreditsToppedUpIntegrationEvent).Assembly,
        typeof(ModularPlatform.Notifications.Contracts.EmailDeliveryRequested).Assembly,
        typeof(ModularPlatform.Gdpr.Contracts.UserErasureRequested).Assembly,
    ];

    [Fact]
    public void Integration_event_wire_names_are_frozen()
    {
        var actual = ContractAssemblies
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .ToHashSet();

        var renamedOrRemoved = FrozenWireNames.Except(actual).ToList();
        Assert.True(renamedOrRemoved.Count == 0,
            "These integration events lost their frozen wire name (renamed/moved/removed) — a breaking change for "
            + "in-flight durable envelopes: " + string.Join(", ", renamedOrRemoved));

        var added = actual.Except(FrozenWireNames).ToList();
        Assert.True(added.Count == 0,
            "New integration event(s) detected. Add them to FrozenWireNames to lock their wire identity: "
            + string.Join(", ", added));
    }
}
