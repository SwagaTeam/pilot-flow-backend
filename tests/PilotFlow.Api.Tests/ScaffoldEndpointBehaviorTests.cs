using System.Net;
using System.Text.RegularExpressions;
using PilotFlow.Api.Endpoints;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed partial class ScaffoldEndpointBehaviorTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private readonly PilotFlowWebApplicationFactory factory = factory;

    [Fact]
    public async Task EveryScaffoldEndpoint_WithoutIdentity_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        foreach (var descriptor in EndpointCatalog.All)
        {
            using var request = CreateRequest(descriptor);
            using var response = await client.SendAsync(request, CancellationToken.None);

            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"{descriptor.OperationId} returned {(int)response.StatusCode} instead of 401.");
        }
    }

    [Fact]
    public async Task ScaffoldEndpointsWithoutIfMatch_WithIdentity_ReturnNotImplemented()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, "test-subject");

        foreach (var descriptor in EndpointCatalog.All.Where(static endpoint => !endpoint.RequiresIfMatch))
        {
            using var request = CreateRequest(descriptor);
            using var response = await client.SendAsync(request, CancellationToken.None);

            Assert.True(
                response.StatusCode == HttpStatusCode.NotImplemented,
                $"{descriptor.OperationId} returned {(int)response.StatusCode} instead of 501.");
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

    private static HttpRequestMessage CreateRequest(EndpointDescriptor descriptor)
    {
        var path = RouteParameterRegex().Replace(
            descriptor.Pattern,
            "00000000-0000-4000-8000-000000000001");
        return new HttpRequestMessage(new HttpMethod(descriptor.Method), path);
    }

    [GeneratedRegex("\\{[^}]+\\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteParameterRegex();
}
