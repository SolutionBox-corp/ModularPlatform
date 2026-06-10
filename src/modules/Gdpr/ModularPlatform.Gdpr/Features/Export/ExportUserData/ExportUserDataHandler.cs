using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Export.ExportUserData;

/// <summary>
/// Read slice. Injects every module's <see cref="IExportPersonalData"/> implementation and calls each
/// one's <c>ExportAsync</c>, assembling a single document keyed by <see cref="IExportPersonalData.ModuleName"/>.
/// This is boundary-clean: it depends only on the Abstractions port, never on another module's Core.
/// Per-exporter resilience: if one module's exporter throws, that section is recorded as
/// <c>{ "error": "export_failed" }</c> and the remaining exporters still run (partial export beats a 500).
/// </summary>
internal sealed class ExportUserDataHandler(
    IEnumerable<IExportPersonalData> exporters,
    ILogger<ExportUserDataHandler> logger)
    : IQueryHandler<ExportUserDataQuery, Dictionary<string, object?>>
{
    public async Task<Dictionary<string, object?>> Handle(ExportUserDataQuery query, CancellationToken ct)
    {
        var document = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var exporter in exporters)
        {
            try
            {
                var section = await exporter.ExportAsync(query.UserId, ct);
                document[exporter.ModuleName] = section;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Personal-data export failed for module {Module}, user {UserId}. Partial export continues.",
                    exporter.ModuleName, query.UserId);

                document[exporter.ModuleName] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["error"] = "export_failed",
                };
            }
        }

        return document;
    }
}
