using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed class DatabaseUnavailableEndpointTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private const string ExportPath =
        "/api/v1/analyses/00000000-0000-4000-8000-000000000001/versions/00000000-0000-4000-8000-000000000002/export";
    private readonly PilotFlowWebApplicationFactory factory = factory;

    [Fact]
    public async Task Export_WithoutDatabase_ReturnsServiceUnavailableProblem()
    {
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync(ExportPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));
        var root = document.RootElement;
        Assert.Equal("DATABASE_UNAVAILABLE", root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("issues").ValueKind);
        Assert.Empty(root.GetProperty("issues").EnumerateArray());
        Assert.DoesNotContain("stack", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_WithTextAcceptHeader_ReturnsProblemJson()
    {
        using var client = CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await client.GetAsync(ExportPath, CancellationToken.None);

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
