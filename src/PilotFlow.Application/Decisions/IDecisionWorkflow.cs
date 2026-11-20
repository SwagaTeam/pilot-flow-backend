using System.Text.Json;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Evaluation;
using PilotFlow.Application.Families;
using PilotFlow.Domain.Catalogs;
using PilotFlow.Domain.Decisions;

namespace PilotFlow.Application.Decisions;

public sealed record VersionSnapshot(
    string SchemaVersion, AnalysisVersionDetails Version,
    IReadOnlyList<ProductDocument> Products, IReadOnlyList<OperationEntry> Operations,
    IReadOnlyList<ProductRouteDocument> ProductRoutes, IReadOnlyList<FamilyEntry> Families,
    IReadOnlyList<FamilyMetricsEntry> Metrics, EvaluationResult Evaluation,
    ProtocolData Protocol, SelectionData? Selection, FamilyMatrixResult FamilyMatrix);

public sealed record ProductRouteDocument(Guid ProductId, RouteData Route);

public sealed record DecisionDraft(
    long Revision, bool CanExportComplete, bool HasSelection, IReadOnlyList<EvaluationIssue> Issues, JsonElement Snapshot);

public interface IDecisionWorkflow
{
    Task<DecisionDraft?> GetDraftAsync(VersionScope scope, CancellationToken cancellationToken);

    Task<AnalysisWriteResult<Versioned<DecisionDetails>>> FinalizeAsync(
        CatalogChange change, string operatorName, CancellationToken cancellationToken);
}
