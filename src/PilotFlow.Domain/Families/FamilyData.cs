using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;

namespace PilotFlow.Domain.Families;

public sealed record FamilyData
{
    public required string Name { get; init; }
    public required string? StartBoundary { get; init; }
    public required string? EndBoundary { get; init; }
    public required string? CompositionReason { get; init; }
    public required bool Confirmed { get; init; }
    public required AnalysisEvidence ConfirmationEvidence { get; init; }
    public required RouteData Route { get; init; }
}

public sealed record Assignment
{
    public required Guid ProductId { get; init; }
    public required Guid? FamilyId { get; init; }
}

public sealed record AssignmentsData
{
    public required IReadOnlyList<Assignment> Assignments { get; init; }
    public required string Reason { get; init; }
}
