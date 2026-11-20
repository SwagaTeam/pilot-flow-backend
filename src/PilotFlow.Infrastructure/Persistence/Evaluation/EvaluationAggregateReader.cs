using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Evaluation;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Decisions;
using PilotFlow.Infrastructure.Persistence.Analyses;
using PilotFlow.Infrastructure.Persistence.Catalogs;
using PilotFlow.Infrastructure.Persistence.Decisions;
using PilotFlow.Infrastructure.Persistence.Families;

namespace PilotFlow.Infrastructure.Persistence.Evaluation;

internal sealed class EvaluationAggregateReader(AnalysisDbContext db, DecisionSettingsReader settings)
{
    internal async Task<EvaluationAggregate?> ReadAsync(VersionScope scope, CancellationToken cancellationToken)
    {
        var version = await db.OwnedVersions(scope.Owner, scope.AnalysisId)
            .AsNoTracking().SingleOrDefaultAsync(row => row.Id == scope.VersionId, cancellationToken);
        if (version is null)
        {
            return null;
        }

        var versionId = version.Id;
        var memberships = await db.Set<FamilyProductRow>().AsNoTracking()
            .Where(row => row.VersionId == versionId).ToDictionaryAsync(row => row.ProductId, row => row.FamilyId, cancellationToken);
        var productRows = await db.Set<ProductRow>().AsNoTracking().Where(row => row.VersionId == versionId)
            .OrderBy(row => row.Name).ThenBy(row => row.Id).ToArrayAsync(cancellationToken);
        var routeSteps = await db.Set<RouteStepRow>().AsNoTracking().Where(row => row.VersionId == versionId)
            .OrderBy(row => row.Position).Select(row => new { row.ProductId, row.DurationHours }).ToArrayAsync(cancellationToken);
        var durations = routeSteps.ToLookup(row => row.ProductId, row => row.DurationHours);
        var products = productRows.Select(row => new EvaluationProduct(
            row.Id, memberships.TryGetValue(row.Id, out var familyId) ? familyId : null, row.IsOther, row.Revenue,
            row.RawMaterialCost, row.SalesVolume, row.CalendarDurationDays, row.InProductionPlan, Evidence(row.FinancialEvidence),
            Evidence(row.DurationEvidence), Evidence(row.PlanEvidence), durations[row.Id].ToArray())).ToArray();

        var familiesWithRoutes = (await db.Set<FamilyRouteStepRow>().AsNoTracking()
            .Where(row => row.VersionId == versionId).Select(row => row.FamilyId).Distinct().ToArrayAsync(cancellationToken)).ToHashSet();
        var metrics = (await PostgresFamilyMetricsStore.ReadAllAsync(db, versionId, cancellationToken))
            .ToDictionary(entry => entry.FamilyId, entry => entry.Data);
        var familyRows = await db.Set<FamilyRow>().AsNoTracking().Where(row => row.VersionId == versionId)
            .OrderBy(row => row.Name).ThenBy(row => row.Id).ToArrayAsync(cancellationToken);
        var families = familyRows.Select(row => new EvaluationFamily(row.Id, row.Confirmed,
            row.StartBoundary, row.EndBoundary, row.CompositionReason, Evidence(row.ConfirmationEvidence),
            familiesWithRoutes.Contains(row.Id), metrics.GetValueOrDefault(row.Id) ?? MetricsData.Empty)).ToArray();
        var protocol = await settings.GetProtocolAsync(versionId, cancellationToken);
        var selection = await settings.GetSelectionAsync(versionId, cancellationToken);

        return EvaluationCalculator.Calculate(new(version.ToDetails(), products, families, protocol, selection), version.UpdatedAt);
    }

    private static AnalysisEvidence Evidence(string json) => StoredJson.Read<AnalysisEvidence>(json);
}
