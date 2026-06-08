using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Export.ExportUserData;

/// <summary>
/// Read slice. Injects every module's <see cref="IExportPersonalData"/> implementation and calls each
/// one's <c>ExportAsync</c>, assembling a single document keyed by <see cref="IExportPersonalData.ModuleName"/>.
/// This is boundary-clean: it depends only on the Abstractions port, never on another module's Core.
/// </summary>
internal sealed class ExportUserDataHandler(IEnumerable<IExportPersonalData> exporters)
    : IQueryHandler<ExportUserDataQuery, Dictionary<string, object?>>
{
    public async Task<Dictionary<string, object?>> Handle(ExportUserDataQuery query, CancellationToken ct)
    {
        var document = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var exporter in exporters)
        {
            var section = await exporter.ExportAsync(query.UserId, ct);
            document[exporter.ModuleName] = section;
        }

        return document;
    }
}
