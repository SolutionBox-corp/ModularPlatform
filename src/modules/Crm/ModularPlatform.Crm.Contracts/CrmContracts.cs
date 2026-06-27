namespace ModularPlatform.Crm.Contracts;

/// <summary>
/// Marker for the CRM module's public contract surface. Integration events and shared DTOs live in this
/// assembly (which references ONLY <c>ModularPlatform.Cqrs</c>). Other modules reference CRM exclusively
/// through this assembly — never its Core. Concrete events (e.g. MeetingScheduled, OutreachActionDue) are
/// added here as the corresponding features land.
/// </summary>
public static class CrmContracts
{
    /// <summary>The module key used by <c>Modules:Crm:Enabled</c> and <c>.RequireModule("crm")</c>.</summary>
    public const string ModuleKey = "crm";
}
