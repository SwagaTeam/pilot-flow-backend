using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace PilotFlow.ContractTests.OpenApi;

public static class RegisteredEndpointReader
{
    public static IReadOnlyList<ApiOperation> Read()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var dataSources = factory.Services.GetServices<EndpointDataSource>();

        return dataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(ToOperations)
            .ToArray();
    }

    private static IEnumerable<ApiOperation> ToOperations(RouteEndpoint endpoint)
    {
        var operationId = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        var pattern = endpoint.RoutePattern.RawText;

        if (operationId is null || methods is null || pattern is null)
        {
            yield break;
        }

        foreach (var method in methods)
        {
            yield return new ApiOperation(method.ToUpperInvariant(), pattern, operationId);
        }
    }
}
