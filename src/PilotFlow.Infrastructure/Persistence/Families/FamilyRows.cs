namespace PilotFlow.Infrastructure.Persistence.Families;

internal sealed class FamilyRow
{
    public Guid Id { get; init; }
    public Guid VersionId { get; init; }
    public required string Name { get; set; }
    public string? StartBoundary { get; set; }
    public string? EndBoundary { get; set; }
    public string? CompositionReason { get; set; }
    public bool Confirmed { get; set; }
    public required string ConfirmationEvidence { get; set; }
}

internal sealed class FamilyProductRow
{
    public Guid ProductId { get; init; }
    public Guid VersionId { get; init; }
    public Guid FamilyId { get; init; }
}

internal sealed class FamilyRouteStepRow
{
    public Guid OccurrenceId { get; init; }
    public Guid VersionId { get; init; }
    public Guid FamilyId { get; init; }
    public Guid OperationId { get; init; }
    public int Position { get; init; }
    public decimal? DurationHours { get; init; }
    public string? WorkplaceName { get; init; }
    public required string Evidence { get; init; }
}
