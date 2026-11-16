using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Authentication;

namespace PilotFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationExtensions.AnalysisUserPolicy)]
[Route("/api/v1/me")]
public sealed class IdentityController : ControllerBase
{
    [HttpGet(Name = "getMe")]
    public IResult Get()
    {
        var subject = User.FindFirst("sub")!.Value;
        var name = User.FindFirst("name")?.Value;
        var displayName = name is { Length: <= 300 } && !string.IsNullOrWhiteSpace(name)
            ? name
            : subject;
        return Results.Ok(new { subject, displayName });
    }
}
