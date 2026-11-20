using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Concurrency;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Decisions;

namespace PilotFlow.Api.Controllers;

[Route("/api/v1/analyses/{analysisId}/versions/{versionId}")]
public sealed class DecisionWorkflowController(IDecisionWorkflow workflow) : VersionCatalogController
{
    [HttpGet("protocol-preview", Name = "previewProtocol")]
    public async Task<IResult> PreviewAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken)
    {
        var draft = await workflow.GetDraftAsync(Scope(analysisId, versionId), cancellationToken);
        if (draft is null)
        {
            return ProblemResults.NotFound(HttpContext);
        }

        if (!draft.HasSelection)
        {
            return ProblemResults.ValidationFailed(HttpContext,
                [new ApiIssue("SELECTION_REQUIRED", "error", "/selection", "Нужно выбрать пилотный поток.", "Сохраните выбор потока.", null)]);
        }

        Response.Headers.ETag = new AggregateEtag(versionId, draft.Revision).ToString();
        return Results.Ok(new { draft.Revision, Ready = draft.CanExportComplete, draft.Issues, draft.Snapshot });
    }

    [RequireIfMatch]
    [HttpPost("finalize", Name = "finalizeDecision")]
    public async Task<IResult> FinalizeAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken) =>
        Write(versionId, await workflow.FinalizeAsync(Change(analysisId, versionId), IdentityController.DisplayName(User), cancellationToken),
            decision => Results.Created(Url.RouteUrl("getDecision", new { analysisId, decisionId = decision.Id }), decision));
}
