using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Concurrency;
using PilotFlow.Application.Decisions;
using PilotFlow.Domain.Decisions;

namespace PilotFlow.Api.Controllers;

[Route("/api/v1/analyses/{analysisId}/versions/{versionId}/families/{familyId}/metrics")]
public sealed class FamilyMetricsController(IFamilyMetricsStore metrics) : VersionCatalogController
{
    [HttpGet(Name = "getFamilyMetrics")]
    public async Task<IResult> GetAsync(Guid analysisId, Guid versionId, Guid familyId, CancellationToken cancellationToken) =>
        Read(versionId, await metrics.GetAsync(Scope(analysisId, versionId), familyId, cancellationToken),
            static data => Results.Ok(data));

    [RequireIfMatch]
    [HttpPut(Name = "replaceFamilyMetrics")]
    public async Task<IResult> ReplaceAsync(
        Guid analysisId, Guid versionId, Guid familyId, MetricsData data, CancellationToken cancellationToken) =>
        Write(versionId, await metrics.ReplaceAsync(Change(analysisId, versionId), familyId, data, cancellationToken),
            static saved => Results.Ok(saved));
}
