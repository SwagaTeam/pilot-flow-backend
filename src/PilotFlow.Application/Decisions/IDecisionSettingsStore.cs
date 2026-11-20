using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Domain.Decisions;

namespace PilotFlow.Application.Decisions;

public interface IDecisionSettingsStore
{
    Task<Versioned<ProtocolData>?> GetProtocolAsync(VersionScope scope, CancellationToken cancellationToken);
    Task<Versioned<SelectionData>?> GetSelectionAsync(VersionScope scope, CancellationToken cancellationToken);

    Task<AnalysisWriteResult<Versioned<ProtocolData>>> ReplaceProtocolAsync(
        CatalogChange change, ProtocolData data, CancellationToken cancellationToken);

    Task<AnalysisWriteResult<Versioned<SelectionData>>> ReplaceSelectionAsync(
        CatalogChange change, SelectionData data, CancellationToken cancellationToken);
}
