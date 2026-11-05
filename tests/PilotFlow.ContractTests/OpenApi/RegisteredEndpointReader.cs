using Microsoft.AspNetCore.Http;
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

        return Read(dataSources.SelectMany(static source => source.Endpoints));
    }

    public static IReadOnlyList<ApiOperation> Read(IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(ToOperations)
            .ToArray();
    }

    private static IEnumerable<ApiOperation> ToOperations(RouteEndpoint endpoint)
    {
        var pattern = endpoint.RoutePattern.RawText;
        if (pattern is null
            || (!pattern.Equals("/api/v1", StringComparison.Ordinal)
                && !pattern.StartsWith("/api/v1/", StringComparison.Ordinal)))
        {
            yield break;
        }

        var operationId = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            ?? throw new InvalidDataException($"Business route {pattern} has no endpoint name metadata.");
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        if (methods is null || methods.Count == 0)
        {
            throw new InvalidDataException($"Business route {pattern} has no HTTP method metadata.");
        }

        foreach (var method in methods)
        {
            yield return new ApiOperation(method.ToUpperInvariant(), pattern, operationId);
        }
    }
}
