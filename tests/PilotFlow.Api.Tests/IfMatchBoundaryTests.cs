using System.Net;
using System.Text.Json;
using PilotFlow.Api.Concurrency;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed class IfMatchBoundaryTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private const string MutationPath =
        "/api/v1/analyses/00000000-0000-4000-8000-000000000001/versions/00000000-0000-4000-8000-000000000011";
    private const string ValidEtag = "\"v:00000000-0000-4000-8000-000000000011:r:1\"";
    private readonly PilotFlowWebApplicationFactory factory = factory;

    [Fact]
    public void AggregateEtag_WithStrongQuotedValue_ParsesComponents()
    {
        var parsed = AggregateEtag.TryParse(ValidEtag, out var etag);

        Assert.True(parsed);
        Assert.Equal(Guid.Parse("00000000-0000-4000-8000-000000000011"), etag.VersionId);
        Assert.Equal(1, etag.Revision);
    }

    [Theory]
    [InlineData("v:00000000-0000-4000-8000-000000000011:r:1")]
    [InlineData("\"x:00000000-0000-4000-8000-000000000011:r:1\"")]
    [InlineData("\"v:not-a-uuid:r:1\"")]
    [InlineData("\"v:00000000-0000-4000-8000-000000000011:r:0\"")]
    [InlineData("\"v:00000000-0000-4000-8000-000000000011:r:-1\"")]
    [InlineData("\"v:00000000-0000-4000-8000-000000000011:r:1\", \"v:00000000-0000-4000-8000-000000000011:r:2\"")]
    [InlineData("W/\"v:00000000-0000-4000-8000-000000000011:r:1\"")]
    public void AggregateEtag_WithInvalidValue_IsRejected(string value)
    {
        Assert.False(AggregateEtag.TryParse(value, out _));
    }

    [Fact]
    public async Task VersionMutation_WithoutIfMatch_ReturnsPreconditionRequired()
    {
        using var response = await SendMutationAsync(ifMatch: null);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Equal("ETAG_REQUIRED", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task VersionMutation_WithMalformedIfMatch_ReturnsInvalidRequest()
    {
        using var response = await SendMutationAsync("not-an-etag");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_ETAG", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task VersionMutation_WithValidIfMatch_ReachesStub()
    {
        using var response = await SendMutationAsync(ValidEtag);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task VersionMutation_WithoutIdentity_RemainsUnauthorized()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, MutationPath);

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendMutationAsync(string? ifMatch)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, "test-subject");
        if (ifMatch is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", ifMatch);
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, MutationPath);
        return await client.SendAsync(request, CancellationToken.None);
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
        return json.RootElement.GetProperty("code").GetString()!;
    }
}
