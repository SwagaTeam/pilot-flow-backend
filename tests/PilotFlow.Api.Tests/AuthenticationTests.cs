using System.Net;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed class AuthenticationTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private readonly PilotFlowWebApplicationFactory factory = factory;

    [Fact]
    public async Task ProtectedEndpoint_WithoutIdentity_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/_scaffold-probe", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTestIdentity_ReachesEndpoint()
    {
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/v1/_scaffold-probe", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task UnknownEndpoint_WithIdentity_ReturnsNotFound()
    {
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/v1/not-a-real-endpoint", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, "test-subject");
        return client;
    }
}
