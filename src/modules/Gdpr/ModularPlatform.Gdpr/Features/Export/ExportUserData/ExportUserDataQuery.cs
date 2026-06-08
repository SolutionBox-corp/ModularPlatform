using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Export.ExportUserData;

/// <summary>
/// Fans out across every module that holds PII (via the IExportPersonalData port) and assembles one
/// data-portability document keyed by module name. Boundary-clean: depends on the PORT, never on any
/// module's Core.
/// </summary>
public sealed record ExportUserDataQuery(Guid UserId) : IQuery<Dictionary<string, object?>>;
