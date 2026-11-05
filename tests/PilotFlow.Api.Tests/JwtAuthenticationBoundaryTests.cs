using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PilotFlow.Api.Tests;

public sealed class JwtAuthenticationBoundaryTests
{
    [Fact]
    public async Task JwtChallenge_WithUnsupportedAccept_ReturnsUnauthorizedProblem()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(CancellationToken.None));
        var problem = document.RootElement;
        Assert.Equal("UNAUTHORIZED", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal(JsonValueKind.Array, problem.GetProperty("issues").ValueKind);
    }

    [Fact]
    public void Production_WithNonHttpsAuthority_StopsDuringStartup()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Jwt:Authority", "http://issuer.example.invalid");
                builder.UseSetting("Jwt:Audience", "pilot-flow");
            });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("HTTPS", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
