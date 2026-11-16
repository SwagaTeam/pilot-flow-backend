using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Concurrency;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Domain.Catalogs;
using RouteData = PilotFlow.Domain.Catalogs.RouteData;

namespace PilotFlow.Api.Controllers;

[Route("/api/v1/analyses/{analysisId}/versions/{versionId}/products")]
public sealed class ProductsController(IVersionCatalog store, CatalogCommands commands) : VersionCatalogController
{
    [HttpGet(Name = "listProducts")]
    public async Task<IResult> ListAsync(
        Guid analysisId, Guid versionId, [FromQuery] ProductPageQuery query, CancellationToken cancellationToken)
    {
        var result = await store.ListProductsAsync(Scope(analysisId, versionId), query.ToPage(), query.Completeness, cancellationToken);
        return Read(versionId, result, static page => Results.Ok(new Page<ProductResponse>(
            page.Items.Select(ToResponse).ToArray(), page.Offset, page.Limit, page.Total, page.UnfilteredTotal)));
    }

    [HttpGet("{productId}", Name = "getProduct")]
    public async Task<IResult> GetAsync(Guid analysisId, Guid versionId, Guid productId, CancellationToken cancellationToken)
    {
        var result = await store.GetProductAsync(Scope(analysisId, versionId), productId, cancellationToken);
        return Read(versionId, result, static product => Results.Ok(ToResponse(product)));
    }

    [RequireIfMatch]
    [HttpPost(Name = "createProduct")]
    public async Task<IResult> CreateAsync(Guid analysisId, Guid versionId, ProductData data, CancellationToken cancellationToken)
    {
        var result = await commands.SaveProductAsync(Change(analysisId, versionId), null, data, cancellationToken);
        return Write(versionId, result, product => Results.Created(
            Url.RouteUrl("getProduct", new { analysisId, versionId, productId = product.Id }), ToResponse(product)));
    }

    [RequireIfMatch]
    [HttpPut("{productId}", Name = "replaceProduct")]
    public async Task<IResult> ReplaceAsync(Guid analysisId, Guid versionId, Guid productId, ProductData data,
        CancellationToken cancellationToken)
    {
        var result = await commands.SaveProductAsync(Change(analysisId, versionId), productId, data, cancellationToken);
        return Write(versionId, result, static product => Results.Ok(ToResponse(product)));
    }

    [RequireIfMatch]
    [HttpDelete("{productId}", Name = "deleteProduct")]
    public async Task<IResult> DeleteAsync(Guid analysisId, Guid versionId, Guid productId, CancellationToken cancellationToken)
    {
        var result = await store.DeleteProductAsync(Change(analysisId, versionId), productId, cancellationToken);
        return Write(versionId, result, static _ => Results.NoContent());
    }

    [HttpGet("{productId}/route", Name = "getProductRoute")]
    public async Task<IResult> GetRouteAsync(Guid analysisId, Guid versionId, Guid productId, CancellationToken cancellationToken)
    {
        var result = await store.GetRouteAsync(Scope(analysisId, versionId), productId, cancellationToken);
        return Read(versionId, result, static route => Results.Ok(route));
    }

    [RequireIfMatch]
    [HttpPut("{productId}/route", Name = "replaceProductRoute")]
    public async Task<IResult> ReplaceRouteAsync(Guid analysisId, Guid versionId, Guid productId, RouteData route,
        CancellationToken cancellationToken)
    {
        var result = await commands.ReplaceRouteAsync(Change(analysisId, versionId), productId, route, cancellationToken);
        return Write(versionId, result, static saved => Results.Ok(saved));
    }

    private static ProductResponse ToResponse(ProductEntry product) => new(
        product.Id, product.VersionId, product.Data, product.Missing.Count == 0 ? "complete" : "incomplete",
        product.Missing.Select(field => new ApiIssue("PRODUCT_FIELD_MISSING", "warning", "/data/" + field,
            "Не хватает данных для проверки продукции.", "Заполните значение или укажите источник с датой или периодом.", product.Id))
            .ToArray());

    private sealed record ProductResponse(
        Guid Id, Guid VersionId, ProductData Data, string Completeness, IReadOnlyList<ApiIssue> Issues);
}

public sealed class ProductPageQuery : PageQuery
{
    [RegularExpression("all|complete|incomplete")]
    public string Completeness { get; init; } = "all";
}
