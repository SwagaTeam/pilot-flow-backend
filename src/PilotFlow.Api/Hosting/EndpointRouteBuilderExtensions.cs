namespace PilotFlow.Api.Hosting;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapPilotFlowApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", static () => Results.Ok(new { status = "ok" }));
        endpoints.MapGet("/health/ready", static () => Results.Ok(new { status = "ready" }));

        return endpoints;
    }
}
