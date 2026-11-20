using System.Security.Claims;
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
        return Results.Ok(new { subject, displayName = DisplayName(User) });
    }

    internal static string DisplayName(ClaimsPrincipal user)
    {
        var name = user.FindFirst("name")?.Value;
        return name is { Length: <= 300 } && !string.IsNullOrWhiteSpace(name) && !name.Contains('\0', StringComparison.Ordinal)
            ? name
            : user.FindFirst("sub")!.Value;
    }
}
