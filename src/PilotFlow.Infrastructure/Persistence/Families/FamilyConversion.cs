using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Families;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;
using PilotFlow.Domain.Families;
using PilotFlow.Infrastructure.Persistence.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Families;

internal static class FamilyConversion
{
    internal static async Task<FamilyEntry[]> ReadFamilyEntriesAsync(
        AnalysisDbContext db, Guid versionId, FamilyRow[] families, CancellationToken cancellationToken)
    {
        if (families.Length == 0)
        {
            return [];
        }

        var ids = families.Select(row => row.Id).ToArray();
        var memberRows = await db.Set<FamilyProductRow>().AsNoTracking()
            .Where(row => row.VersionId == versionId && ids.Contains(row.FamilyId))
            .OrderBy(row => row.ProductId).ToArrayAsync(cancellationToken);
        var stepRows = await db.Set<FamilyRouteStepRow>().AsNoTracking()
            .Where(row => row.VersionId == versionId && ids.Contains(row.FamilyId))
            .OrderBy(row => row.Position).ToArrayAsync(cancellationToken);
        var members = memberRows.ToLookup(row => row.FamilyId, row => row.ProductId);
        var steps = stepRows.ToLookup(row => row.FamilyId, ToStep);
        return families.Select(row => ToEntry(row, members[row.Id].ToArray(), steps[row.Id].ToArray())).ToArray();
    }

    internal static RouteStepData ToStep(FamilyRouteStepRow row) => new()
    {
        OccurrenceId = row.OccurrenceId,
        OperationId = row.OperationId,
        Position = row.Position,
        DurationHours = row.DurationHours,
        WorkplaceName = row.WorkplaceName,
        Evidence = StoredJson.Read<AnalysisEvidence>(row.Evidence),
    };

    internal static FamilyEntry ToEntry(FamilyRow row, IReadOnlyList<Guid> members, IReadOnlyList<RouteStepData> steps) =>
        new(row.Id, row.VersionId, new FamilyData
        {
            Name = row.Name,
            StartBoundary = row.StartBoundary,
            EndBoundary = row.EndBoundary,
            CompositionReason = row.CompositionReason,
            Confirmed = row.Confirmed,
            ConfirmationEvidence = StoredJson.Read<AnalysisEvidence>(row.ConfirmationEvidence),
            Route = new RouteData { Steps = steps },
        }, members);
}
