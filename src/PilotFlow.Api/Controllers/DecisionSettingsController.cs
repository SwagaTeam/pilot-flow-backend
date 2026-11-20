using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Concurrency;
using PilotFlow.Application.Decisions;
using PilotFlow.Domain.Decisions;

namespace PilotFlow.Api.Controllers;

[Route("/api/v1/analyses/{analysisId}/versions/{versionId}")]
public sealed class DecisionSettingsController(IDecisionSettingsStore settings) : VersionCatalogController
{
    [HttpGet("protocol", Name = "getProtocol")]
    public async Task<IResult> GetProtocolAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken) =>
        Read(versionId, await settings.GetProtocolAsync(Scope(analysisId, versionId), cancellationToken), static data => Results.Ok(data));

    [RequireIfMatch]
    [HttpPut("protocol", Name = "replaceProtocol")]
    public async Task<IResult> ReplaceProtocolAsync(Guid analysisId, Guid versionId, ProtocolData data, CancellationToken cancellationToken) =>
        Write(versionId, await settings.ReplaceProtocolAsync(Change(analysisId, versionId), data, cancellationToken),
            static saved => Results.Ok(saved));

    [HttpGet("selection", Name = "getSelection")]
    public async Task<IResult> GetSelectionAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken) =>
        Read(versionId, await settings.GetSelectionAsync(Scope(analysisId, versionId), cancellationToken), static data => Results.Ok(data));

    [RequireIfMatch]
    [HttpPut("selection", Name = "replaceSelection")]
    public async Task<IResult> ReplaceSelectionAsync(Guid analysisId, Guid versionId, SelectionData data, CancellationToken cancellationToken) =>
        Write(versionId, await settings.ReplaceSelectionAsync(Change(analysisId, versionId), data, cancellationToken),
            static saved => Results.Ok(saved));
}
