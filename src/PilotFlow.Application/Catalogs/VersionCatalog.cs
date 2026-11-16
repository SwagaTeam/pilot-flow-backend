using PilotFlow.Application.Analyses;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;

namespace PilotFlow.Application.Catalogs;

public sealed record VersionScope(string Owner, Guid AnalysisId, Guid VersionId);
public sealed record CatalogChange(VersionScope Scope, Guid ExpectedVersionId, long ExpectedRevision);
public sealed record Versioned<T>(long Revision, T Value);
public sealed record ProductEntry(Guid Id, Guid VersionId, ProductData Data, IReadOnlyList<string> Missing);
public sealed record OperationEntry(Guid Id, Guid VersionId, OperationData Data);
public sealed record ValidatedInput<T>(T Data, IReadOnlyList<AnalysisIssue> Issues);

public interface IVersionCatalog
{
    Task<Versioned<Page<ProductEntry>>?> ListProductsAsync(
        VersionScope scope, PageRequest page, string completeness, CancellationToken cancellationToken);
    Task<Versioned<ProductEntry>?> GetProductAsync(
        VersionScope scope, Guid productId, CancellationToken cancellationToken);
    Task<AnalysisWriteResult<Versioned<ProductEntry>>> SaveProductAsync(
        CatalogChange change, Guid? productId, ValidatedInput<ProductData> input, CancellationToken cancellationToken);
    Task<AnalysisWriteResult<Versioned<bool>>> DeleteProductAsync(
        CatalogChange change, Guid productId, CancellationToken cancellationToken);
    Task<Versioned<Page<OperationEntry>>?> ListOperationsAsync(
        VersionScope scope, PageRequest page, CancellationToken cancellationToken);
    Task<Versioned<OperationEntry>?> GetOperationAsync(
        VersionScope scope, Guid operationId, CancellationToken cancellationToken);
    Task<AnalysisWriteResult<Versioned<OperationEntry>>> SaveOperationAsync(
        CatalogChange change, Guid? operationId, ValidatedInput<OperationData> input, CancellationToken cancellationToken);
    Task<AnalysisWriteResult<Versioned<bool>>> DeleteOperationAsync(
        CatalogChange change, Guid operationId, CancellationToken cancellationToken);
    Task<Versioned<RouteData>?> GetRouteAsync(
        VersionScope scope, Guid productId, CancellationToken cancellationToken);
    Task<AnalysisWriteResult<Versioned<RouteData>>> ReplaceRouteAsync(
        CatalogChange change, Guid productId, ValidatedInput<RouteData> input, CancellationToken cancellationToken);
}

public sealed class CatalogCommands(IVersionCatalog catalog)
{
    public Task<AnalysisWriteResult<Versioned<ProductEntry>>> SaveProductAsync(
        CatalogChange change, Guid? productId, ProductData data, CancellationToken cancellationToken) =>
        catalog.SaveProductAsync(change, productId, new(data, CatalogValidation.Validate(data)), cancellationToken);

    public Task<AnalysisWriteResult<Versioned<OperationEntry>>> SaveOperationAsync(
        CatalogChange change, Guid? operationId, OperationData data, CancellationToken cancellationToken) =>
        catalog.SaveOperationAsync(change, operationId, new(data, CatalogValidation.Validate(data)), cancellationToken);

    public Task<AnalysisWriteResult<Versioned<RouteData>>> ReplaceRouteAsync(
        CatalogChange change, Guid productId, RouteData data, CancellationToken cancellationToken) =>
        catalog.ReplaceRouteAsync(change, productId, new(data, CatalogValidation.Validate(data)), cancellationToken);
}
