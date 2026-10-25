using PilotFlow.Api.Errors;

namespace PilotFlow.Api.Endpoints;

public static class NotImplementedEndpoint
{
    public static IResult Handle(HttpContext context, string operationId) =>
        ProblemResults.NotImplemented(context, operationId);
}
