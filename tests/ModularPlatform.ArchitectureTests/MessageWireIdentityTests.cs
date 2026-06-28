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
        "ModularPlatform.Tenancy.Contracts.TenantProvisionedIntegrationEvent",
    ];

    // Durable Wolverine SAGA messages live in module Core (NOT *.Contracts) and carry no IIntegrationEvent marker, so
    // the scan below can't see them — yet they are persisted by full .NET type name in saga + SCHEDULED (timeout)
    // envelopes just the same. Renaming/moving one orphans an in-flight checkout: the CreditPurchaseTimeout never
    // fires and a paid CreditPurchaseConfirmed dead-letters. Freeze them explicitly. ADD a new saga message here.
    private static readonly string[] FrozenSagaMessageNames =
    [
        "ModularPlatform.Billing.Sagas.CreditPurchaseStarted",
        "ModularPlatform.Billing.Sagas.CreditPurchaseConfirmed",
        "ModularPlatform.Billing.Sagas.CreditPurchaseTimeout",
    ];

    // Durable envelopes are not crypto-shreddable like encrypted DB rows. These are the only currently accepted
    // PII-bearing event fields, all needed by the delivery/signup path and bounded by PlatformMessaging retention.
    private static readonly HashSet<string> AllowedPiiBearingEventFields =
    [
        "ModularPlatform.Identity.Contracts.UserRegisteredIntegrationEvent.Email",
        "ModularPlatform.Identity.Contracts.UserRegisteredIntegrationEvent.DisplayName",
        "ModularPlatform.Notifications.Contracts.EmailDeliveryRequested.ToAddress",
        "ModularPlatform.Notifications.Contracts.EmailDeliveryRequested.Subject",
        "ModularPlatform.Notifications.Contracts.EmailDeliveryRequested.Body",
        "ModularPlatform.Notifications.Contracts.PushDeliveryRequested.Title",
        "ModularPlatform.Notifications.Contracts.PushDeliveryRequested.Body",
    ];

    private static readonly string[] SuspiciousDurablePayloadFieldNames =
    [
        "Email",
        "Address",
        "DisplayName",
        "Subject",
        "Title",
        "Body",
        "Content",
        "Note",
        "Raw",
        "Payload",
        "Json",
    ];

    // Anchor types whose assemblies hold the integration-event contracts.
    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(ModularPlatform.Identity.Contracts.UserRegisteredIntegrationEvent).Assembly,
        typeof(ModularPlatform.Billing.Contracts.CreditsToppedUpIntegrationEvent).Assembly,
        typeof(ModularPlatform.Notifications.Contracts.EmailDeliveryRequested).Assembly,
        typeof(ModularPlatform.Gdpr.Contracts.UserErasureRequested).Assembly,
        typeof(ModularPlatform.Tenancy.Contracts.TenantProvisionedIntegrationEvent).Assembly,
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

    [Fact]
    public void Saga_durable_message_wire_names_are_frozen()
    {
        var billingCore = typeof(ModularPlatform.Billing.Sagas.CreditPurchaseSaga).Assembly;
        var missing = FrozenSagaMessageNames.Where(name => billingCore.GetType(name) is null).ToList();
        Assert.True(missing.Count == 0,
            "These durable saga messages lost their frozen wire name (renamed/moved) — a breaking change for in-flight "
            + "checkout envelopes: " + string.Join(", ", missing));
    }

    [Fact]
    public void Integration_events_do_not_gain_pii_shaped_fields_without_an_explicit_allowlist()
    {
        var offenders = ContractAssemblies
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => SuspiciousDurablePayloadFieldNames.Any(name =>
                    p.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .Where(field => !AllowedPiiBearingEventFields.Contains(field))
            .OrderBy(x => x)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Integration events should carry IDs/status, not PII/raw payload fields that can sit in durable "
            + "envelopes. Either remove these fields or add a reviewed allowlist entry with a retention reason: "
            + string.Join(", ", offenders));
    }
}
