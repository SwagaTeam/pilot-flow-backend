using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Decisions;

namespace PilotFlow.Application.Evaluation;

public sealed record EvaluationIssue(
    string Code, string Severity, string Path, string Message, string Suggestion, Guid? EntityId);

public sealed record EvaluationResult(
    Guid VersionId, long Revision, string RulesVersion, DateTimeOffset CalculatedAt,
    string? ReconciledRevenue, string? RemainderRevenue, IReadOnlyList<EvaluationIssue> Issues,
    IReadOnlyList<FamilyEvaluationResult> Families, IReadOnlyList<Guid> LeaderFamilyIds,
    bool CanRecommend, bool CanFinalizeRanked);

public sealed record FamilyEvaluationResult(
    Guid FamilyId, string? Revenue, string? RawMaterialCost, string? MarginIncome,
    string? RevenueSharePercent, string? MarginPercent, string? MaxCalendarDurationDays,
    string Admission, IReadOnlyList<EvaluationIssue> Issues, IReadOnlyList<CriterionResult> Scores,
    string KnownScoreSum, int CalculatedCriterionCount, string? TotalScore, int? Rank,
    bool IsProvisional, bool CanSelectRanked);

public sealed record CriterionResult(
    string Criterion, string? Value, string Unit, string? Score, string Status, AnalysisEvidence Evidence,
    IReadOnlyList<EvaluationIssue> Issues, string RuleCode, string Explanation);

public sealed record FamilyMatrixResult(
    Guid VersionId, long Revision, IReadOnlyList<ProductFinancialResult> Products,
    IReadOnlyList<FamilyCycleResult> Cycles, IReadOnlyList<EvaluationIssue> Issues);

public sealed record ProductFinancialResult(
    Guid ProductId, Guid? FamilyId, string? MarginIncome, string? MarginIncomePerUnit,
    string? MarginPercent, string? RevenueSharePercent, string? RouteDurationHours, int OperationOccurrenceCount);

public sealed record FamilyCycleResult(
    Guid FamilyId, string? CycleDifferencePercent, string Status, IReadOnlyList<EvaluationIssue> Issues);

public sealed record DirectSelectionOptionsResult(long Revision, IReadOnlyList<DirectSelectionOption> Options);

public sealed record DirectSelectionOption(
    Guid FamilyId, string Mode, Guid? DominantProductId, string Status, IReadOnlyList<EvaluationIssue> Issues);

public sealed record VersionValidationResult(
    long Revision, bool CanFinalize, bool CanExportComplete, IReadOnlyList<EvaluationIssue> Issues);

public sealed record EvaluationAggregate(
    AnalysisVersionDetails Version, EvaluationResult Evaluation, FamilyMatrixResult FamilyMatrix,
    DirectSelectionOptionsResult DirectSelectionOptions, VersionValidationResult Validation,
    ProtocolData Protocol, SelectionData? Selection)
{
    public IReadOnlyList<EvaluationIssue> SelectionIssues(SelectionData selection, bool forSave = false) =>
        SelectionPolicy.Issues(Evaluation, DirectSelectionOptions, selection, forSave);
}

public interface IEvaluationStore
{
    Task<Versioned<EvaluationAggregate>?> GetAsync(VersionScope scope, CancellationToken cancellationToken);
}
