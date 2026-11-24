using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace PilotFlow.Api.Tests;

public sealed class CorsTests
{
    [Theory]
    [InlineData("https://frontend.example", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task Preflight_OnlyConfiguredOriginIsAllowed(string origin, bool allowed)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins:0", "https://frontend.example"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/analyses");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,if-match");
        using var response = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Contains("if-match", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UnauthorizedResponse_ExposesConcurrencyAndDownloadHeadersToAllowedOrigin()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins:0", "https://frontend.example"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://frontend.example");
        using var response = await client.GetAsync("/api/v1/analyses", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("ETag", exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Location", exposed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content-Disposition", exposed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithoutConfiguredOrigins_DoesNotAllowCrossOriginAccess()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://untrusted.example");
        using var response = await client.GetAsync("/health/live", CancellationToken.None);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
