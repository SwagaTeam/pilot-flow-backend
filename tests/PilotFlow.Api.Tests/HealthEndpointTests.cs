using System.Net;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed class HealthEndpointTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task LiveHealth_ReturnsOk()
    {
        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyHealth_WithoutDatabase_ReturnsServiceUnavailable()
    {
        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsNotFound()
    {
        using var response = await client.GetAsync("/does-not-exist", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
