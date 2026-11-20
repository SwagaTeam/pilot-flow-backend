using Microsoft.AspNetCore.Mvc;
using PilotFlow.Application.Evaluation;

namespace PilotFlow.Api.Controllers;

[Route("/api/v1/analyses/{analysisId}/versions/{versionId}")]
public sealed class VersionEvaluationController(IEvaluationStore store) : VersionCatalogController
{
    [HttpGet("evaluation", Name = "evaluateVersion")]
    public async Task<IResult> EvaluationAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken) =>
        Read(versionId, await store.GetAsync(Scope(analysisId, versionId), cancellationToken),
            static result => Results.Ok(result.Evaluation));

    [HttpGet("family-matrix", Name = "getFamilyMatrix")]
    public async Task<IResult> MatrixAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken) =>
        Read(versionId, await store.GetAsync(Scope(analysisId, versionId), cancellationToken),
            static result => Results.Ok(result.FamilyMatrix));

    [HttpGet("validation", Name = "validateVersion")]
    public async Task<IResult> ValidationAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken) =>
        Read(versionId, await store.GetAsync(Scope(analysisId, versionId), cancellationToken),
            static result => Results.Ok(result.Validation));

    [HttpGet("direct-selection-options", Name = "getDirectSelectionOptions")]
    public async Task<IResult> DirectSelectionOptionsAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken) =>
        Read(versionId, await store.GetAsync(Scope(analysisId, versionId), cancellationToken),
            static result => Results.Ok(result.DirectSelectionOptions));
}
