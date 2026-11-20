using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Authentication;
using PilotFlow.Api.Concurrency;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Analyses;

namespace PilotFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationExtensions.AnalysisUserPolicy)]
[Route("/api/v1/analyses/{analysisId}/decisions")]
public sealed class DecisionsController(IDecisionStore decisions) : ControllerBase
{
    private string Owner => User.FindFirst("sub")!.Value;

    [HttpGet(Name = "listDecisions")]
    public async Task<IResult> ListAsync(Guid analysisId, [FromQuery] PageQuery query, CancellationToken cancellationToken)
    {
        var page = await decisions.ListAsync(Owner, analysisId, query.ToPage(), cancellationToken);
        return page is null ? ProblemResults.NotFound(HttpContext) : Results.Ok(page);
    }

    [HttpGet("{decisionId}", Name = "getDecision")]
    public async Task<IResult> GetAsync(Guid analysisId, Guid decisionId, CancellationToken cancellationToken)
    {
        var decision = await decisions.GetAsync(Owner, analysisId, decisionId, cancellationToken);
        if (decision is null)
        {
            return ProblemResults.NotFound(HttpContext);
        }

        Response.Headers.ETag = DecisionEtag.Format(decision.SnapshotSha256);
        return Results.Ok(decision);
    }
}
