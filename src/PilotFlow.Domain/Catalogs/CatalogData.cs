using PilotFlow.Domain.Analyses;

namespace PilotFlow.Domain.Catalogs;

public sealed record ProductData
{
    public required string Name { get; init; }
    public required string UnitCode { get; init; }
    public required decimal? SalesVolume { get; init; }
    public required decimal? Revenue { get; init; }
    public required decimal? RawMaterialCost { get; init; }
    public required AnalysisEvidence FinancialEvidence { get; init; }
    public required decimal? CalendarDurationDays { get; init; }
    public required string? DurationStart { get; init; }
    public required string? DurationEnd { get; init; }
    public required bool? IncludesWaiting { get; init; }
    public required AnalysisEvidence DurationEvidence { get; init; }
    public required bool? InProductionPlan { get; init; }
    public required AnalysisEvidence PlanEvidence { get; init; }
    public required bool IsOther { get; init; }
    public required string? ProductCode { get; init; }
}

public sealed record OperationData
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required decimal? AggregationCoveragePercent { get; init; }
    public required AnalysisEvidence AggregationEvidence { get; init; }
    public required string? WorkplaceName { get; init; }
}

public sealed record RouteData
{
    public required IReadOnlyList<RouteStepData> Steps { get; init; }
}

public sealed record RouteStepData
{
    public required Guid OccurrenceId { get; init; }
    public required Guid OperationId { get; init; }
    public required int Position { get; init; }
    public required decimal? DurationHours { get; init; }
    public required AnalysisEvidence Evidence { get; init; }
    public required string? WorkplaceName { get; init; }
}
