using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;

namespace PilotFlow.Infrastructure.Persistence.Catalogs;

internal sealed partial class PostgresVersionCatalog
{
    public Task<Versioned<Page<OperationEntry>>?> ListOperationsAsync(
        VersionScope scope, PageRequest page, CancellationToken cancellationToken) =>
        transactions.ReadAsync<Page<OperationEntry>>(scope, async token =>
        {
            var all = Operations(scope.VersionId).AsNoTracking();
            var unfilteredTotal = await all.CountAsync(token);
            var filtered = string.IsNullOrEmpty(page.Query)
                ? all
                : all.Where(row => EF.Functions.ILike(row.Name, VersionPersistence.ContainsPattern(page.Query), "\\"));
            var total = string.IsNullOrEmpty(page.Query) ? unfilteredTotal : await filtered.CountAsync(token);
            var items = await filtered.OrderBy(row => row.Name).ThenBy(row => row.Id)
                .Skip(page.Offset).Take(page.Limit).ToArrayAsync(token);
            return new Page<OperationEntry>(items.Select(CatalogConversion.ToOperation).ToArray(),
                page.Offset, page.Limit, total, unfilteredTotal);
        }, cancellationToken);

    public Task<Versioned<OperationEntry>?> GetOperationAsync(
        VersionScope scope, Guid operationId, CancellationToken cancellationToken) =>
        transactions.ReadAsync<OperationEntry>(scope, async token =>
        {
            var row = await Operations(scope.VersionId).AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == operationId, token);
            return row is null ? null : CatalogConversion.ToOperation(row);
        }, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<OperationEntry>>> SaveOperationAsync(
        CatalogChange change, Guid? operationId, ValidatedInput<OperationData> input, CancellationToken cancellationToken) =>
        transactions.WriteAsync<OperationEntry>(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            var existing = operationId.HasValue
                ? await Operations(versionId).SingleOrDefaultAsync(row => row.Id == operationId.Value, token)
                : null;
            if (operationId.HasValue && existing is null)
            {
                return new(AnalysisWriteStatus.NotFound);
            }

            if (input.Issues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: input.Issues);
            }

            var row = CatalogConversion.NewOperation(operationId ?? Guid.NewGuid(), versionId, input.Data);
            if (existing is null)
            {
                db.Set<OperationRow>().Add(row);
            }
            else
            {
                var sectionChanged = row.Kind == "section"
                    && (existing.AggregationCoveragePercent != row.AggregationCoveragePercent
                        || StoredJson.Read<AnalysisEvidence>(existing.AggregationEvidence) != input.Data.AggregationEvidence);
                var scopeChanged = existing.Kind != row.Kind || sectionChanged;
                if (scopeChanged || existing.WorkplaceName != row.WorkplaceName)
                {
                    var families = await FamiliesUsingOperationAsync(versionId, existing.Id, inheritsWorkplaceOnly: !scopeChanged, token);
                    await InvalidateFamiliesAsync(versionId, families, compositionChanged: false, token);
                }

                db.Entry(existing).CurrentValues.SetValues(row);
            }

            await db.SaveChangesAsync(token);
            return new(AnalysisWriteStatus.Success, CatalogConversion.ToOperation(row));
        }, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<bool>>> DeleteOperationAsync(
        CatalogChange change, Guid operationId, CancellationToken cancellationToken) =>
        transactions.WriteAsync<bool>(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            var inUse = await db.Database.SqlQuery<bool>($"""
                SELECT (EXISTS (SELECT FROM pilot.product_route_step
                    WHERE version_id = {versionId} AND operation_id = {operationId})
                    OR EXISTS (SELECT FROM pilot.family_route_step
                    WHERE version_id = {versionId} AND operation_id = {operationId})) AS "Value"
                """).SingleAsync(token);
            if (inUse)
            {
                return new(AnalysisWriteStatus.StateConflict);
            }

            var deleted = await Operations(versionId).Where(row => row.Id == operationId).ExecuteDeleteAsync(token);
            return deleted == 0 ? new(AnalysisWriteStatus.NotFound) : new(AnalysisWriteStatus.Success, true);
        }, cancellationToken);

    private IQueryable<OperationRow> Operations(Guid versionId) =>
        db.Set<OperationRow>().Where(row => row.VersionId == versionId);

    private Task<Guid[]> FamiliesUsingOperationAsync(
        Guid versionId, Guid operationId, bool inheritsWorkplaceOnly, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<Guid>($"""
            SELECT membership.family_id AS "Value"
            FROM pilot.product_route_step AS step
            JOIN pilot.family_product AS membership
              ON membership.version_id = step.version_id AND membership.product_id = step.product_id
            WHERE step.version_id = {versionId} AND step.operation_id = {operationId}
              AND (NOT {inheritsWorkplaceOnly} OR step.workplace_name IS NULL)
            UNION
            SELECT family_id FROM pilot.family_route_step
            WHERE version_id = {versionId} AND operation_id = {operationId}
              AND (NOT {inheritsWorkplaceOnly} OR workplace_name IS NULL)
            """).ToArrayAsync(cancellationToken);

    private async Task InvalidateFamiliesAsync(
        Guid versionId, Guid[] familyIds, bool compositionChanged, CancellationToken cancellationToken)
    {
        if (familyIds.Length == 0)
        {
            return;
        }

        var evidence = StoredJson.Write(new AnalysisEvidence
        {
            Source = null,
            Basis = null,
            Period = null,
            AssessedOn = null,
            MissingReason = compositionChanged ? "COMPOSITION_CHANGED" : "ROUTE_CHANGED",
        });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pilot.family SET confirmed = false WHERE version_id = {versionId} AND id = ANY({familyIds})
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pilot.family_metrics
            SET load_percent = NULL, load_actual = NULL, load_capacity = NULL, load_unit = NULL,
                labor_hours_per_unit = NULL, direct_labor_hours = NULL, output_quantity = NULL,
                labor_unit_code = NULL, labor_base_quantity = NULL, comparability_explanation = NULL,
                load_evidence = {evidence}::jsonb, labor_evidence = {evidence}::jsonb
            WHERE version_id = {versionId} AND family_id = ANY({familyIds})
            """, cancellationToken);

        if (compositionChanged)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE pilot.family_metrics
                SET sales_growth_percent = NULL, production_type = NULL,
                    sales_growth_evidence = {evidence}::jsonb, production_type_evidence = {evidence}::jsonb
                WHERE version_id = {versionId} AND family_id = ANY({familyIds})
                """, cancellationToken);
        }
    }
}
