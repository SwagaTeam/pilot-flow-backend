using PilotFlow.Application.Catalogs;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;

namespace PilotFlow.Infrastructure.Persistence.Catalogs;

internal static class CatalogConversion
{
    internal static ProductRow NewProduct(Guid id, Guid versionId, ProductData data) => new()
    {
        Id = id,
        VersionId = versionId,
        Name = data.Name,
        UnitCode = data.UnitCode,
        SalesVolume = data.SalesVolume,
        Revenue = data.Revenue,
        RawMaterialCost = data.RawMaterialCost,
        FinancialEvidence = StoredJson.Write(data.FinancialEvidence),
        CalendarDurationDays = data.CalendarDurationDays,
        DurationStart = data.DurationStart,
        DurationEnd = data.DurationEnd,
        IncludesWaiting = data.IncludesWaiting,
        DurationEvidence = StoredJson.Write(data.DurationEvidence),
        InProductionPlan = data.InProductionPlan,
        PlanEvidence = StoredJson.Write(data.PlanEvidence),
        IsOther = data.IsOther,
        ProductCode = data.ProductCode,
    };

    internal static ProductEntry ToProduct(ProductRow row, string[] missing) => new(
        row.Id, row.VersionId, new ProductData
        {
            Name = row.Name,
            UnitCode = row.UnitCode,
            SalesVolume = row.SalesVolume,
            Revenue = row.Revenue,
            RawMaterialCost = row.RawMaterialCost,
            FinancialEvidence = StoredJson.Read<AnalysisEvidence>(row.FinancialEvidence),
            CalendarDurationDays = row.CalendarDurationDays,
            DurationStart = row.DurationStart,
            DurationEnd = row.DurationEnd,
            IncludesWaiting = row.IncludesWaiting,
            DurationEvidence = StoredJson.Read<AnalysisEvidence>(row.DurationEvidence),
            InProductionPlan = row.InProductionPlan,
            PlanEvidence = StoredJson.Read<AnalysisEvidence>(row.PlanEvidence),
            IsOther = row.IsOther,
            ProductCode = row.ProductCode,
        }, missing);

    internal static OperationRow NewOperation(Guid id, Guid versionId, OperationData data) => new()
    {
        Id = id,
        VersionId = versionId,
        Name = data.Name,
        Kind = data.Kind,
        AggregationCoveragePercent = data.AggregationCoveragePercent,
        AggregationEvidence = StoredJson.Write(data.AggregationEvidence),
        WorkplaceName = data.WorkplaceName,
    };

    internal static OperationEntry ToOperation(OperationRow row) => new(
        row.Id, row.VersionId, new OperationData
        {
            Name = row.Name,
            Kind = row.Kind,
            AggregationCoveragePercent = row.AggregationCoveragePercent,
            AggregationEvidence = StoredJson.Read<AnalysisEvidence>(row.AggregationEvidence),
            WorkplaceName = row.WorkplaceName,
        });

    internal static RouteStepRow NewStep(Guid versionId, Guid productId, RouteStepData data) => new()
    {
        VersionId = versionId,
        ProductId = productId,
        OccurrenceId = data.OccurrenceId,
        OperationId = data.OperationId,
        Position = data.Position,
        DurationHours = data.DurationHours,
        Evidence = StoredJson.Write(data.Evidence),
        WorkplaceName = data.WorkplaceName,
    };

    internal static RouteStepData ToStep(RouteStepRow row) => new()
    {
        OccurrenceId = row.OccurrenceId,
        OperationId = row.OperationId,
        Position = row.Position,
        DurationHours = row.DurationHours,
        Evidence = StoredJson.Read<AnalysisEvidence>(row.Evidence),
        WorkplaceName = row.WorkplaceName,
    };
}
