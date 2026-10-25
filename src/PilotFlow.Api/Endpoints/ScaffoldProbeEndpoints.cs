using PilotFlow.Api.Authentication;

namespace PilotFlow.Api.Endpoints;

public static class ScaffoldProbeEndpoints
{
    public static IEndpointRouteBuilder MapScaffoldProbe(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/v1/_scaffold-probe",
                static (HttpContext context) => NotImplementedEndpoint.Handle(context, "scaffoldProbe"))
            .RequireAuthorization(AuthenticationExtensions.AnalysisUserPolicy);

        return endpoints;
    }
}
