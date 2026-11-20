using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Domain.Decisions;

namespace PilotFlow.Application.Decisions;

public sealed record FamilyMetricsEntry(Guid FamilyId, MetricsData Data);

public interface IFamilyMetricsStore
{
    Task<Versioned<MetricsData>?> GetAsync(VersionScope scope, Guid familyId, CancellationToken cancellationToken);

    Task<AnalysisWriteResult<Versioned<MetricsData>>> ReplaceAsync(
        CatalogChange change, Guid familyId, MetricsData data, CancellationToken cancellationToken);
}
