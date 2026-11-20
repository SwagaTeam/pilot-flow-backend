using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed partial class EndpointAuthorizationTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private readonly PilotFlowWebApplicationFactory factory = factory;

    [Fact]
    public async Task EveryContractEndpoint_WithoutIdentity_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("api/v1/", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(45, routes.Length);
        foreach (var route in routes)
        {
            var pattern = route.RoutePattern.RawText!;
            var method = route.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods.Single();
            var path = "/" + RouteParameterRegex().Replace(pattern.TrimStart('/'), "00000000-0000-4000-8000-000000000001");
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            using var response = await client.SendAsync(request, CancellationToken.None);

            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"{method} {pattern} returned {(int)response.StatusCode} instead of 401.");
        }
    }

    [Fact]
    public async Task UnknownEndpoint_WithIdentity_ReturnsNotFound()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, "test-subject");

        using var response = await client.GetAsync("/api/v1/unknown", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [GeneratedRegex("\\{[^}]+\\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteParameterRegex();
}
