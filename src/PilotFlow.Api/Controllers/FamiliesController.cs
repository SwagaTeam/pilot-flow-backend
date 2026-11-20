using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Concurrency;
using PilotFlow.Application.Families;
using PilotFlow.Domain.Families;

namespace PilotFlow.Api.Controllers;

[Route("/api/v1/analyses/{analysisId}/versions/{versionId}")]
public sealed class FamiliesController(IFamilyCatalog catalog, FamilyCommands commands) : VersionCatalogController
{
    [HttpGet("families", Name = "listFamilies")]
    public async Task<IResult> ListAsync(
        Guid analysisId, Guid versionId, [FromQuery] PageQuery query, CancellationToken cancellationToken)
    {
        var result = await catalog.ListFamiliesAsync(Scope(analysisId, versionId), query.ToPage(), cancellationToken);
        return Read(versionId, result, static page => Results.Ok(page));
    }

    [HttpGet("families/{familyId}", Name = "getFamily")]
    public async Task<IResult> GetAsync(Guid analysisId, Guid versionId, Guid familyId, CancellationToken cancellationToken)
    {
        var result = await catalog.GetFamilyAsync(Scope(analysisId, versionId), familyId, cancellationToken);
        return Read(versionId, result, static family => Results.Ok(family));
    }

    [RequireIfMatch]
    [HttpPost("families", Name = "createFamily")]
    public async Task<IResult> CreateAsync(Guid analysisId, Guid versionId, FamilyData data, CancellationToken cancellationToken)
    {
        var result = await commands.SaveFamilyAsync(Change(analysisId, versionId), null, data, cancellationToken);
        return Write(versionId, result, family => Results.Created(
            Url.RouteUrl("getFamily", new { analysisId, versionId, familyId = family.Id }), family));
    }

    [RequireIfMatch]
    [HttpPut("families/{familyId}", Name = "replaceFamily")]
    public async Task<IResult> ReplaceAsync(Guid analysisId, Guid versionId, Guid familyId, FamilyData data,
        CancellationToken cancellationToken)
    {
        var result = await commands.SaveFamilyAsync(Change(analysisId, versionId), familyId, data, cancellationToken);
        return Write(versionId, result, static family => Results.Ok(family));
    }

    [RequireIfMatch]
    [HttpDelete("families/{familyId}", Name = "deleteFamily")]
    public async Task<IResult> DeleteAsync(Guid analysisId, Guid versionId, Guid familyId, CancellationToken cancellationToken)
    {
        var result = await catalog.DeleteFamilyAsync(Change(analysisId, versionId), familyId, cancellationToken);
        return Write(versionId, result, static _ => Results.NoContent());
    }

    [HttpGet("family-suggestions", Name = "suggestFamilies")]
    public async Task<IResult> SuggestAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken)
    {
        var result = await catalog.SuggestFamiliesAsync(Scope(analysisId, versionId), cancellationToken);
        return Read(versionId, result, groups => Results.Ok(new { revision = result!.Revision, groups }));
    }

    [HttpGet("products/{productId}/assignment-options", Name = "getAssignmentOptions")]
    public async Task<IResult> GetAssignmentOptionsAsync(
        Guid analysisId, Guid versionId, Guid productId, CancellationToken cancellationToken)
    {
        var result = await catalog.GetAssignmentOptionsAsync(Scope(analysisId, versionId), productId, cancellationToken);
        return Read(versionId, result, options => Results.Ok(new
        {
            revision = result!.Revision,
            options.ProductId,
            options.Options,
            options.CanCreateSeparateFamily,
        }));
    }

    [RequireIfMatch]
    [HttpPut("assignments", Name = "assignProducts")]
    public async Task<IResult> AssignAsync(Guid analysisId, Guid versionId, AssignmentsData data, CancellationToken cancellationToken)
    {
        var result = await commands.AssignProductsAsync(Change(analysisId, versionId), data, cancellationToken);
        return Write(versionId, result, static version => Results.Ok(version));
    }
}
