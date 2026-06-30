using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Marketing.Gdpr;

/// <summary>
/// GDPR data-portability for Marketing: returns the subject's marketing footprint as named JSON sections — the data
/// pulls they triggered (with their request params + provider error, but not the verbatim provider payload), the
/// normalized metric snapshots projected from them, the AI analyses, and their vibe-marketing chat threads + messages
/// (the subject's own free text). Read-only via the read factory, scoped to the subject's <c>UserId</c>; the Gdpr
/// module fans these into the one export document. EF / LINQ only.
/// </summary>
internal sealed class MarketingPersonalDataExporter(IReadDbContextFactory<MarketingDbContext> readFactory)
    : IExportPersonalData
{
    public string ModuleName => "Marketing";

    public async Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var pulls = await db.DataPulls
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => new
            {
                p.Id,
                Source = p.Source.ToString(),
                Status = p.Status.ToString(),
                p.ParamsJson,
                p.ErrorCode,
                p.ErrorDetail,
                p.CompletedAt,
                p.CreatedAt,
            })
            .ToListAsync(ct);

        var snapshots = await db.MetricSnapshots
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.RecordedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.DataPullId,
                Source = m.Source.ToString(),
                m.MetricName,
                m.Dimension,
                m.Value,
                m.DetailJson,
                m.RecordedAt,
            })
            .ToListAsync(ct);

        var analyses = await db.MarketingAnalyses
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AnalyzedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.DataPullId,
                Source = a.Source.ToString(),
                a.Summary,
                a.InsightsJson,
                a.AnalyzedAt,
                a.CreatedAt,
            })
            .ToListAsync(ct);

        var conversations = await db.VibeConversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.LastMessageAt,
                c.DeletedAt,
                c.CreatedAt,
            })
            .ToListAsync(ct);

        var messages = await db.VibeMessages
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.ConversationId,
                m.Role,
                m.Content,
                m.ToolCallsJson,
                m.CreatedAt,
            })
            .ToListAsync(ct);

        return new Dictionary<string, object?>
        {
            ["pulls"] = pulls,
            ["snapshots"] = snapshots,
            ["analyses"] = analyses,
            ["vibe_conversations"] = conversations,
            ["vibe_messages"] = messages,
        };
    }
}
