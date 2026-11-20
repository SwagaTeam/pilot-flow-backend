using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed class NotImplementedEndpointTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private const string FamiliesPath =
        "/api/v1/analyses/00000000-0000-4000-8000-000000000001/versions/00000000-0000-4000-8000-000000000002/export";
    private readonly PilotFlowWebApplicationFactory factory = factory;

    [Fact]
    public async Task Stub_WithIdentity_ReturnsNotImplementedProblem()
    {
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync(FamiliesPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));
        var root = document.RootElement;
        Assert.Equal("NOT_IMPLEMENTED", root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("issues").ValueKind);
        Assert.Empty(root.GetProperty("issues").EnumerateArray());
        Assert.Contains("exportDraft", root.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("stack", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stub_WithAnyAcceptHeader_ReturnsProblemJson()
    {
        using var client = CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await client.GetAsync(FamiliesPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, "test-subject");
        return client;
    }
}
