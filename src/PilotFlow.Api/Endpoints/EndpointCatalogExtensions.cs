using PilotFlow.Api.Authentication;

namespace PilotFlow.Api.Endpoints;

public static class EndpointCatalogExtensions
{
    public static IEndpointRouteBuilder MapScaffoldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        foreach (var descriptor in EndpointCatalog.All)
        {
            var operationId = descriptor.OperationId;
            endpoints
                .MapMethods(
                    descriptor.Pattern,
                    [descriptor.Method],
                    (HttpContext context) => NotImplementedEndpoint.Handle(context, operationId))
                .WithName(operationId)
                .WithMetadata(descriptor)
                .RequireAuthorization(AuthenticationExtensions.AnalysisUserPolicy);
        }

        return endpoints;
    }
}
