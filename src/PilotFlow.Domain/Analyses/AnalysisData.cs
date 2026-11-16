namespace PilotFlow.Domain.Analyses;

public sealed record AnalysisData
{
    public required string Title { get; init; }
    public required string? EnterpriseName { get; init; }
    public required string? Region { get; init; }
    public required string? Activity { get; init; }
    public required DateOnly? ProjectStartDate { get; init; }
    public required AnalysisPeriod? FinancialPeriod { get; init; }
    public required AnalysisPeriod? ActivitiesPeriod { get; init; }
    public required AnalysisPeriod? ForecastPeriod { get; init; }
    public required decimal? TotalRevenue { get; init; }
    public required string Currency { get; init; }
    public required string? AccountingBasis { get; init; }
    public required AnalysisEvidence RevenueEvidence { get; init; }
    public required string? RemainderExplanation { get; init; }
    public required bool NomenclatureComplete { get; init; }
    public required AnalysisEvidence CompletenessEvidence { get; init; }
    public required IReadOnlyList<AnalysisParticipant> Participants { get; init; }
    public required string? EnterpriseInn { get; init; }
    public required string? FamilyConclusions { get; init; }
    public required string? RankingConclusions { get; init; }
    public required AnalysisPeriod? LaborPeriod { get; init; }
}

public sealed record AnalysisPeriod
{
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }
}

public sealed record AnalysisEvidence
{
    public required string? Source { get; init; }
    public required string? Basis { get; init; }
    public required AnalysisPeriod? Period { get; init; }
    public required DateOnly? AssessedOn { get; init; }
    public required string? MissingReason { get; init; }
}

public sealed record AnalysisParticipant
{
    public required string? FullName { get; init; }
    public required string? Position { get; init; }
    public required string Role { get; init; }
}
