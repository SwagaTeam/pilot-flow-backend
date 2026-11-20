using System.Data;
using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Decisions;
using PilotFlow.Application.Evaluation;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;
using PilotFlow.Infrastructure.Persistence.Analyses;
using PilotFlow.Infrastructure.Persistence.Catalogs;
using PilotFlow.Infrastructure.Persistence.Evaluation;
using PilotFlow.Infrastructure.Persistence.Families;

namespace PilotFlow.Infrastructure.Persistence.Decisions;

internal sealed class PostgresDecisionWorkflow(
    AnalysisDbContext db, VersionCatalogTransactions transactions, EvaluationAggregateReader evaluation) : IDecisionWorkflow
{
    public async Task<DecisionDraft?> GetDraftAsync(VersionScope scope, CancellationToken cancellationToken)
    {
        var result = await transactions.ReadAsync<DecisionDraft>(scope, async token =>
        {
            var aggregate = (await evaluation.ReadAsync(scope, token))!;
            var snapshot = await CaptureAsync(aggregate, token);
            return new(aggregate.Version.Revision, aggregate.Validation.CanExportComplete, aggregate.Selection is not null,
                aggregate.Validation.Issues, SnapshotJson.Serialize(snapshot));
        }, cancellationToken);
        return result?.Value;
    }

    public async Task<AnalysisWriteResult<Versioned<DecisionDetails>>> FinalizeAsync(
        CatalogChange change, string operatorName, CancellationToken cancellationToken)
    {
        db.EnsureConfigured();
        var scope = change.Scope;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var locked = await db.Database.SqlQuery<LockedVersion>($"""
            SELECT v.revision AS "Revision", v.state AS "State", transaction_timestamp() AS "Timestamp"
            FROM pilot.analysis_version v JOIN pilot.analysis a ON a.id = v.analysis_id
            WHERE a.owner_subject = {scope.Owner} AND a.id = {scope.AnalysisId} AND v.id = {scope.VersionId}
            FOR UPDATE OF v
            """).SingleOrDefaultAsync(cancellationToken);
        if (locked is null)
        {
            return new(AnalysisWriteStatus.NotFound);
        }
        if (locked.State == "fixed")
        {
            return new(AnalysisWriteStatus.Fixed);
        }
        if (scope.VersionId != change.ExpectedVersionId || locked.Revision != change.ExpectedRevision)
        {
            return new(AnalysisWriteStatus.RevisionConflict);
        }

        var aggregate = (await evaluation.ReadAsync(scope, cancellationToken))!;
        if (!aggregate.Validation.CanFinalize || aggregate.Selection is null)
        {
            return new(AnalysisWriteStatus.ValidationFailed, Issues: aggregate.Validation.Issues
                .Where(issue => issue.Severity == "error")
                .Select(issue => new AnalysisIssue(issue.Code, issue.Path, issue.Message, issue.Suggestion, issue.EntityId)).ToArray());
        }

        var revision = locked.Revision + 1;
        var final = aggregate with
        {
            Version = aggregate.Version with { State = "fixed", Revision = revision, UpdatedAt = locked.Timestamp },
            Evaluation = aggregate.Evaluation with { Revision = revision, CalculatedAt = locked.Timestamp },
            FamilyMatrix = aggregate.FamilyMatrix with { Revision = revision },
        };
        var snapshot = SnapshotJson.Serialize(await CaptureAsync(final, cancellationToken));
        var hash = SnapshotJson.Hash(snapshot);
        var decisionId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pilot.decision (
                id, version_id, analysis_id, family_id, created_at, created_by_subject, created_by_name,
                snapshot_schema_version, rules_version, snapshot, snapshot_sha256
            )
            VALUES (
                {decisionId}, {scope.VersionId}, {scope.AnalysisId}, {aggregate.Selection.FamilyId},
                {locked.Timestamp}, {scope.Owner}, {operatorName}, '1.0', {final.Version.RulesVersion},
                {snapshot.GetRawText()}::jsonb, {hash}
            )
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pilot.analysis_version SET state = 'fixed', revision = {revision} WHERE id = {scope.VersionId}
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(AnalysisWriteStatus.Success,
            new(revision, new(decisionId, scope.VersionId, locked.Timestamp, scope.Owner, operatorName, hash, snapshot)));
    }

    private async Task<VersionSnapshot> CaptureAsync(EvaluationAggregate aggregate, CancellationToken cancellationToken)
    {
        var versionId = aggregate.Version.Id;
        var products = await (
            from row in db.Set<ProductRow>().AsNoTracking()
            join quality in db.Set<ProductCompletenessRow>() on row.Id equals quality.ProductId
            where row.VersionId == versionId && quality.VersionId == versionId
            orderby row.Id
            select new { Row = row, quality.Missing }).ToArrayAsync(cancellationToken);
        var operations = await db.Set<OperationRow>().AsNoTracking().Where(row => row.VersionId == versionId)
            .OrderBy(row => row.Id).ToArrayAsync(cancellationToken);
        var routes = await db.Set<RouteStepRow>().AsNoTracking().Where(row => row.VersionId == versionId)
            .OrderBy(row => row.ProductId).ThenBy(row => row.Position).ThenBy(row => row.OccurrenceId).ToArrayAsync(cancellationToken);
        var routesByProduct = routes.ToLookup(row => row.ProductId, CatalogConversion.ToStep);
        var familyRows = await db.Set<FamilyRow>().AsNoTracking().Where(row => row.VersionId == versionId)
            .OrderBy(row => row.Id).ToArrayAsync(cancellationToken);
        var families = await FamilyConversion.ReadFamilyEntriesAsync(db, versionId, familyRows, cancellationToken);
        var metrics = await PostgresFamilyMetricsStore.ReadAllAsync(db, versionId, cancellationToken);

        return new("1.0", aggregate.Version,
            products.Select(item => SnapshotOrdering.Product(ProductDocument.From(CatalogConversion.ToProduct(item.Row, item.Missing))))
                .ToArray(),
            operations.Select(CatalogConversion.ToOperation).ToArray(),
            products.Select(item => new ProductRouteDocument(item.Row.Id, new RouteData
            {
                Steps = routesByProduct[item.Row.Id].ToArray(),
            })).ToArray(),
            families, metrics, SnapshotOrdering.Evaluation(aggregate.Evaluation), aggregate.Protocol, aggregate.Selection,
            SnapshotOrdering.Matrix(aggregate.FamilyMatrix));
    }

    private sealed class LockedVersion
    {
        public long Revision { get; init; }
        public required string State { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }
}
