namespace PilotFlow.Infrastructure.Persistence.Catalogs;

internal sealed class ProductRow
{
    public Guid Id { get; init; }
    public Guid VersionId { get; init; }
    public required string Name { get; set; }
    public string? ProductCode { get; set; }
    public required string UnitCode { get; set; }
    public decimal? SalesVolume { get; set; }
    public decimal? Revenue { get; set; }
    public decimal? RawMaterialCost { get; set; }
    public required string FinancialEvidence { get; set; }
    public decimal? CalendarDurationDays { get; set; }
    public string? DurationStart { get; set; }
    public string? DurationEnd { get; set; }
    public bool? IncludesWaiting { get; set; }
    public required string DurationEvidence { get; set; }
    public bool? InProductionPlan { get; set; }
    public required string PlanEvidence { get; set; }
    public bool IsOther { get; set; }
}

internal sealed class OperationRow
{
    public Guid Id { get; init; }
    public Guid VersionId { get; init; }
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public string? WorkplaceName { get; set; }
    public decimal? AggregationCoveragePercent { get; set; }
    public required string AggregationEvidence { get; set; }
}

internal sealed class RouteStepRow
{
    public Guid OccurrenceId { get; init; }
    public Guid VersionId { get; init; }
    public Guid ProductId { get; init; }
    public Guid OperationId { get; set; }
    public int Position { get; set; }
    public decimal? DurationHours { get; set; }
    public string? WorkplaceName { get; set; }
    public required string Evidence { get; set; }
}

internal sealed class ProductCompletenessRow
{
    public Guid ProductId { get; init; }
    public Guid VersionId { get; init; }
    public required string[] Missing { get; init; }
}
