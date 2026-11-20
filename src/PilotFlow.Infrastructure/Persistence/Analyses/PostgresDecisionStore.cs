using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Analyses;

internal sealed class PostgresDecisionStore(AnalysisDbContext db) : IDecisionStore
{
    public async Task<Page<DecisionDetails>?> ListAsync(
        string owner, Guid analysisId, PageRequest page, CancellationToken cancellationToken)
    {
        db.EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (!await db.Analyses.AsNoTracking().AnyAsync(
            analysis => analysis.Id == analysisId && analysis.OwnerSubject == owner, cancellationToken))
        {
            return null;
        }

        var owned = Owned(owner, analysisId);
        var unfilteredTotal = await owned.CountAsync(cancellationToken);
        var filtered = owned;
        var total = unfilteredTotal;
        if (page.Query is { Length: > 0 } query)
        {
            var pattern = VersionPersistence.ContainsPattern(query);
            filtered = owned.Where(decision => decision.SelectedFamilyName != null
                && EF.Functions.ILike(decision.SelectedFamilyName, pattern, "\\"));
            total = await filtered.CountAsync(cancellationToken);
        }

        var items = await Details(filtered.OrderBy(decision => decision.CreatedAt).ThenBy(decision => decision.Id)
            .Skip(page.Offset).Take(page.Limit)).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(items, page.Offset, page.Limit, total, unfilteredTotal);
    }

    public Task<DecisionDetails?> GetAsync(string owner, Guid analysisId, Guid decisionId, CancellationToken cancellationToken)
    {
        db.EnsureConfigured();
        return Details(Owned(owner, analysisId).Where(decision => decision.Id == decisionId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<DecisionProjection> Owned(string owner, Guid analysisId) =>
        db.Database.SqlQuery<DecisionProjection>($$"""
            SELECT decision.id AS "Id", decision.version_id AS "VersionId",
                decision.created_at AS "CreatedAt", decision.created_by_subject AS "CreatedBySubject",
                decision.created_by_name AS "CreatedByName", decision.snapshot_sha256 AS "SnapshotSha256",
                decision.snapshot AS "Snapshot",
                jsonb_path_query_first(decision.snapshot,
                    '$.families[*] ? (@.id == $family).data.name',
                    jsonb_build_object('family', decision.snapshot #>> '{selection,familyId}')) #>> '{}'
                    AS "SelectedFamilyName"
            FROM pilot.decision AS decision
            JOIN pilot.analysis AS analysis ON analysis.id = decision.analysis_id
            WHERE decision.analysis_id = {{analysisId}} AND analysis.owner_subject = {{owner}}
            """);

    private static IQueryable<DecisionDetails> Details(IQueryable<DecisionProjection> decisions) =>
        decisions.Select(decision => new DecisionDetails(decision.Id, decision.VersionId, decision.CreatedAt,
            decision.CreatedBySubject, decision.CreatedByName, decision.SnapshotSha256, decision.Snapshot));

    private sealed class DecisionProjection
    {
        public Guid Id { get; init; }
        public Guid VersionId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public required string CreatedBySubject { get; init; }
        public required string CreatedByName { get; init; }
        public required string SnapshotSha256 { get; init; }
        public JsonElement Snapshot { get; init; }
        public string? SelectedFamilyName { get; init; }
    }
}
