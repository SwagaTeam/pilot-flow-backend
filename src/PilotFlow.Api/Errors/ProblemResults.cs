using System.Diagnostics;

namespace PilotFlow.Api.Errors;

public static class ProblemResults
{
    public static IResult NotImplemented(HttpContext context, string operationId)
    {
        var problem = new ApiProblem(
            "urn:pilot:problem:not-implemented",
            "Операция ещё не реализована",
            StatusCodes.Status501NotImplemented,
            $"Операция {operationId} зарегистрирована контрактом, но её обработчик ещё не реализован.",
            context.Request.Path,
            "NOT_IMPLEMENTED",
            Activity.Current?.Id ?? context.TraceIdentifier,
            []);

        return Results.Json(
            problem,
            statusCode: StatusCodes.Status501NotImplemented,
            contentType: "application/problem+json");
    }
}
