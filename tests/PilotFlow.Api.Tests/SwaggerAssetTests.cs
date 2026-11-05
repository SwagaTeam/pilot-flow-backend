using System.Net;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed class SwaggerAssetTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private readonly PilotFlowWebApplicationFactory factory = factory;

    [Fact]
    public async Task SwaggerPage_LoadsWithoutAuthentication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/swagger.html", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ServedOpenApiJson_EqualsRepositoryContract()
    {
        using var client = factory.CreateClient();
        var expected = await File.ReadAllBytesAsync(
            Path.Combine(FindRepositoryRoot(), "contracts", "openapi.json"),
            CancellationToken.None);

        using var response = await client.GetAsync("/swagger/openapi.json", CancellationToken.None);
        var actual = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ServedOpenApiYaml_EqualsRepositoryContract()
    {
        using var client = factory.CreateClient();
        var expected = await File.ReadAllBytesAsync(
            Path.Combine(FindRepositoryRoot(), "contracts", "openapi.yaml"),
            CancellationToken.None);

        using var response = await client.GetAsync("/swagger/openapi.yaml", CancellationToken.None);
        var actual = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/yaml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SwaggerPage_LoadsServedContractForCurrentOrigin()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/swagger/swagger.html", CancellationToken.None);

        Assert.Contains("fetch(\"openapi.json\")", html, StringComparison.Ordinal);
        Assert.Contains("window.location.origin", html, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost:5080", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/swagger/swagger-ui.css", "text/css")]
    [InlineData("/swagger/swagger-ui-bundle.js", "text/javascript")]
    public async Task SwaggerAssets_AreAvailableOffline(string path, string mediaType)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PilotFlow.Backend.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("PilotFlow.Backend.slnx was not found above the test output directory.");
    }
}
