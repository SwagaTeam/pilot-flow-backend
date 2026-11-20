using System.Text.Json;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Evaluation;

namespace PilotFlow.Application.Decisions;

public enum DecisionDocument
{
    Bundle,
    Protocol,
    FamilyMatrix,
    SelectionMatrix,
}

public interface IDecisionPdfRenderer
{
    byte[] Render(JsonElement snapshot, DecisionDocument document, string? watermark);
}

public sealed record ExportedDecision(
    byte[]? Content, long? Revision, string? SnapshotHash, IReadOnlyList<EvaluationIssue> Issues);

public sealed class DecisionExport(IDecisionWorkflow workflow, IDecisionStore decisions, IDecisionPdfRenderer renderer)
{
    public async Task<ExportedDecision?> GetDraftAsync(
        VersionScope scope, DecisionDocument document, bool allowIncomplete, CancellationToken cancellationToken)
    {
        var draft = await workflow.GetDraftAsync(scope, cancellationToken);
        if (draft is null)
        {
            return null;
        }

        if (!draft.HasSelection && document is DecisionDocument.Bundle or DecisionDocument.Protocol)
        {
            return new(null, null, null, [new("SELECTION_REQUIRED", "error", "/selection",
                "Нужно выбрать пилотный поток.", "Сохраните выбор потока.", null)]);
        }

        if (!allowIncomplete && !draft.CanExportComplete)
        {
            return new(null, null, null, draft.Issues);
        }

        return new(renderer.Render(draft.Snapshot, document, allowIncomplete ? "ЧЕРНОВИК" : "ПРОЕКТ"), draft.Revision, null, []);
    }

    public async Task<ExportedDecision?> GetDecisionAsync(
        string owner, Guid analysisId, Guid decisionId, DecisionDocument document, CancellationToken cancellationToken)
    {
        var decision = await decisions.GetAsync(owner, analysisId, decisionId, cancellationToken);
        return decision is null
            ? null
            : new(renderer.Render(decision.Snapshot, document, null), null, decision.SnapshotSha256, []);
    }
}
