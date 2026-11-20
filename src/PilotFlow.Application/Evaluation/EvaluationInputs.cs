using PilotFlow.Application.Analyses;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Decisions;

namespace PilotFlow.Application.Evaluation;

public sealed record EvaluationProduct(
    Guid Id, Guid? FamilyId, bool IsOther, decimal? Revenue, decimal? RawMaterialCost,
    decimal? SalesVolume, decimal? CalendarDurationDays, bool? InProductionPlan, AnalysisEvidence FinancialEvidence,
    AnalysisEvidence DurationEvidence, AnalysisEvidence PlanEvidence, IReadOnlyList<decimal?> RouteDurations);

public sealed record EvaluationFamily(
    Guid Id, bool Confirmed, string? StartBoundary, string? EndBoundary,
    string? CompositionReason, AnalysisEvidence ConfirmationEvidence, bool HasRoute,
    MetricsData Metrics);

public sealed record EvaluationInputs(
    AnalysisVersionDetails Version, IReadOnlyList<EvaluationProduct> Products, IReadOnlyList<EvaluationFamily> Families,
    ProtocolData Protocol, SelectionData? Selection);
