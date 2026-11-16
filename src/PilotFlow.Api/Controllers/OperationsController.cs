using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Concurrency;
using PilotFlow.Application.Catalogs;
using PilotFlow.Domain.Catalogs;

namespace PilotFlow.Api.Controllers;

[Route("/api/v1/analyses/{analysisId}/versions/{versionId}/operations")]
public sealed class OperationsController(IVersionCatalog store, CatalogCommands commands) : VersionCatalogController
{
    [HttpGet(Name = "listOperations")]
    public async Task<IResult> ListAsync(
        Guid analysisId, Guid versionId, [FromQuery] PageQuery query, CancellationToken cancellationToken)
    {
        var result = await store.ListOperationsAsync(Scope(analysisId, versionId), query.ToPage(), cancellationToken);
        return Read(versionId, result, static page => Results.Ok(page));
    }

    [HttpGet("{operationId}", Name = "getOperation")]
    public async Task<IResult> GetAsync(Guid analysisId, Guid versionId, Guid operationId, CancellationToken cancellationToken)
    {
        var result = await store.GetOperationAsync(Scope(analysisId, versionId), operationId, cancellationToken);
        return Read(versionId, result, static operation => Results.Ok(operation));
    }

    [RequireIfMatch]
    [HttpPost(Name = "createOperation")]
    public async Task<IResult> CreateAsync(Guid analysisId, Guid versionId, OperationData data, CancellationToken cancellationToken)
    {
        var result = await commands.SaveOperationAsync(Change(analysisId, versionId), null, data, cancellationToken);
        return Write(versionId, result, operation => Results.Created(
            Url.RouteUrl("getOperation", new { analysisId, versionId, operationId = operation.Id }), operation));
    }

    [RequireIfMatch]
    [HttpPut("{operationId}", Name = "replaceOperation")]
    public async Task<IResult> ReplaceAsync(Guid analysisId, Guid versionId, Guid operationId, OperationData data,
        CancellationToken cancellationToken)
    {
        var result = await commands.SaveOperationAsync(Change(analysisId, versionId), operationId, data, cancellationToken);
        return Write(versionId, result, static operation => Results.Ok(operation));
    }

    [RequireIfMatch]
    [HttpDelete("{operationId}", Name = "deleteOperation")]
    public async Task<IResult> DeleteAsync(Guid analysisId, Guid versionId, Guid operationId, CancellationToken cancellationToken)
    {
        var result = await store.DeleteOperationAsync(Change(analysisId, versionId), operationId, cancellationToken);
        return Write(versionId, result, static _ => Results.NoContent());
    }
}
