using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Audit.GetUserAuditTrail;

/// <summary>Admin forensics: a user's Identity audit trail with personal-data values decrypted (until erasure).</summary>
public sealed record GetUserAuditTrailQuery(Guid UserId, bool CrossTenant = false) : IQuery<UserAuditTrailResponse>;

public sealed record UserAuditTrailResponse(IReadOnlyList<AuditTrailEntryResponse> Entries);

/// <summary>
/// One audit row. <see cref="Values"/> maps each captured column to its value — personal-data columns are
/// decrypted to plaintext, or surfaced as <c>[erased]</c> once the subject's key has been shredded.
/// </summary>
public sealed record AuditTrailEntryResponse(
    Guid Id,
    string Action,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string?> Values);
