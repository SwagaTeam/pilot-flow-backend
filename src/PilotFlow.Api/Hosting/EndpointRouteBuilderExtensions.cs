using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PilotFlow.Api.Hosting;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapPilotFlowApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/health/live", static () => Results.Ok(new { status = "ok" }));
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            ResponseWriter = static (context, report) => context.Response.WriteAsJsonAsync(
                new { status = report.Status == HealthStatus.Healthy ? "ready" : "unavailable" },
                context.RequestAborted),
        });
        endpoints.MapGet("/swagger", static () => Results.LocalRedirect("~/swagger/swagger.html"));
        endpoints.MapControllers();

        return endpoints;
    }
}
