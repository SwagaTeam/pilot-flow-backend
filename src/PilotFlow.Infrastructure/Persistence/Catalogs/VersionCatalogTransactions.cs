using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Infrastructure.Persistence.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Catalogs;

internal sealed class VersionCatalogTransactions(AnalysisDbContext db)
{
    internal async Task<Versioned<T>?> ReadAsync<T>(
        VersionScope scope, Func<CancellationToken, Task<T?>> read, CancellationToken cancellationToken)
        where T : class
    {
        db.EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var revision = await Owned(scope).Select(row => (long?)row.Revision).SingleOrDefaultAsync(cancellationToken);
        if (revision is null)
        {
            return null;
        }

        var value = await read(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return value is null ? null : new(revision.Value, value);
    }

    internal async Task<AnalysisWriteResult<Versioned<T>>> WriteAsync<T>(
        CatalogChange change, Func<CancellationToken, Task<AnalysisWriteResult<T>>> write, CancellationToken cancellationToken)
    {
        db.EnsureConfigured();
        var scope = change.Scope;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pilot.analysis_version AS version
            SET revision = version.revision + 1
            FROM pilot.analysis AS analysis
            WHERE version.id = {scope.VersionId}
              AND version.id = {change.ExpectedVersionId}
              AND version.analysis_id = {scope.AnalysisId}
              AND analysis.id = version.analysis_id
              AND analysis.owner_subject = {scope.Owner}
              AND version.state = 'draft'
              AND version.revision = {change.ExpectedRevision}
            """, cancellationToken);
        if (changed == 0)
        {
            var state = await Owned(scope).Select(row => row.State).SingleOrDefaultAsync(cancellationToken);
            return new(state switch
            {
                null => AnalysisWriteStatus.NotFound,
                "fixed" => AnalysisWriteStatus.Fixed,
                _ => AnalysisWriteStatus.RevisionConflict,
            });
        }

        // This must precede edits that may remove the product referenced by a selection.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM pilot.selection WHERE version_id = {scope.VersionId}", cancellationToken);
        try
        {
            var result = await write(cancellationToken);
            if (result.Status != AnalysisWriteStatus.Success)
            {
                return new(result.Status, Issues: result.Issues);
            }

            await transaction.CommitAsync(cancellationToken);
            return new(AnalysisWriteStatus.Success, new(change.ExpectedRevision + 1, result.Value!));
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "product_route_step_pkey" })
        {
            return new(AnalysisWriteStatus.StateConflict);
        }
    }

    private IQueryable<AnalysisVersionRow> Owned(VersionScope scope) =>
        db.OwnedVersions(scope.Owner, scope.AnalysisId).Where(row => row.Id == scope.VersionId);
}
