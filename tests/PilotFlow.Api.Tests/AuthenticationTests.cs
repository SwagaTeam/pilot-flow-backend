using System.Net;
using System.Text.Json;
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

        using var response = await client.GetAsync("/api/v1/me", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithTestIdentity_ReturnsSubjectAndFallbackName()
    {
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/v1/me", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));
        Assert.Equal("test-subject", document.RootElement.GetProperty("subject").GetString());
        Assert.Equal("test-subject", document.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task UnknownEndpoint_WithIdentity_ReturnsNotFound()
    {
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/v1/not-a-real-endpoint", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RuleSet_WhenDatabaseIsNotConfigured_ReturnsServiceUnavailable()
    {
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/v1/rules/fck-2026-tz-v1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, "test-subject");
        return client;
    }
}
