using PilotFlow.Domain.Analyses;

namespace PilotFlow.Domain.Decisions;

public sealed record MetricsData
{
    public required decimal? SalesGrowthPercent { get; init; }
    public required AnalysisEvidence SalesGrowthEvidence { get; init; }
    public required decimal? LoadPercent { get; init; }
    public required decimal? LoadActual { get; init; }
    public required decimal? LoadCapacity { get; init; }
    public required string? LoadUnit { get; init; }
    public required AnalysisEvidence LoadEvidence { get; init; }
    public required string? ProductionType { get; init; }
    public required AnalysisEvidence ProductionTypeEvidence { get; init; }
    public required string LaborMode { get; init; }
    public required decimal? LaborHoursPerUnit { get; init; }
    public required decimal? DirectLaborHours { get; init; }
    public required decimal? OutputQuantity { get; init; }
    public required string? LaborUnitCode { get; init; }
    public required decimal? LaborBaseQuantity { get; init; }
    public required AnalysisEvidence LaborEvidence { get; init; }
    public required string? ComparabilityExplanation { get; init; }

    public static MetricsData Empty { get; } = new()
    {
        SalesGrowthPercent = null,
        SalesGrowthEvidence = DecisionInputValidation.EmptyEvidence,
        LoadPercent = null,
        LoadActual = null,
        LoadCapacity = null,
        LoadUnit = null,
        LoadEvidence = DecisionInputValidation.EmptyEvidence,
        ProductionType = null,
        ProductionTypeEvidence = DecisionInputValidation.EmptyEvidence,
        LaborMode = "manual",
        LaborHoursPerUnit = null,
        DirectLaborHours = null,
        OutputQuantity = null,
        LaborUnitCode = null,
        LaborBaseQuantity = null,
        LaborEvidence = DecisionInputValidation.EmptyEvidence,
        ComparabilityExplanation = null,
    };
}
