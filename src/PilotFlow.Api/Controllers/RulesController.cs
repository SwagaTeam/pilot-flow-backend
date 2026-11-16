using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Authentication;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Rules;

namespace PilotFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationExtensions.AnalysisUserPolicy)]
[Route("/api/v1/rules")]
public sealed class RulesController(IRuleSetStore store) : ControllerBase
{
    [HttpGet("{ruleId}", Name = "getRules")]
    public async Task<IResult> GetAsync(
        [FromRoute, StringLength(100), RegularExpression(@"[^\u0000]*")] string ruleId,
        CancellationToken cancellationToken)
    {
        var rules = await store.GetAsync(ruleId, cancellationToken);
        return rules is null ? ProblemResults.NotFound(HttpContext) : Results.Ok(rules);
    }
}
