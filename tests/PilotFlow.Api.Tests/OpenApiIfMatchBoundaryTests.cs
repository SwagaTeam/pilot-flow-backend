using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.Api.Tests;

public sealed partial class OpenApiIfMatchBoundaryTests(PilotFlowWebApplicationFactory factory)
    : IClassFixture<PilotFlowWebApplicationFactory>
{
    private const string ValidEtag = "\"v:00000000-0000-4000-8000-000000000011:r:1\"";
    private readonly PilotFlowWebApplicationFactory factory = factory;

    [Fact]
    public async Task EveryOpenApiIfMatchOperation_EnforcesHeaderBoundary()
    {
        var operations = ReadRequiredOperations();
        Assert.Equal(17, operations.Count);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, "test-subject");

        foreach (var operation in operations)
        {
            using var missing = await SendAsync(client, operation, ifMatch: null);
            Assert.True(
                missing.StatusCode == (HttpStatusCode)428,
                $"{operation.Method} {operation.Path} returned {(int)missing.StatusCode} without If-Match.");

            using var malformed = await SendAsync(client, operation, "not-an-etag");
            Assert.True(
                malformed.StatusCode == HttpStatusCode.BadRequest,
                $"{operation.Method} {operation.Path} returned {(int)malformed.StatusCode} for malformed If-Match.");

            using var valid = await SendAsync(client, operation, ValidEtag);
            var expected = operation.OperationId switch
            {
                "replaceVersionData" or "createProduct" or "replaceProduct" or "replaceProductRoute"
                    or "createOperation" or "replaceOperation" or "replaceFamilyMetrics"
                    or "forkVersion" or "createFamily" or "replaceFamily" or "assignProducts"
                    or "replaceProtocol" or "replaceSelection" => HttpStatusCode.BadRequest,
                "deleteProduct" or "deleteOperation" or "deleteFamily" or "finalizeDecision" => HttpStatusCode.ServiceUnavailable,
                _ => HttpStatusCode.NotImplemented,
            };
            Assert.True(
                valid.StatusCode == expected,
                $"{operation.Method} {operation.Path} returned {(int)valid.StatusCode} instead of {(int)expected}.");
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        RequiredOperation operation,
        string? ifMatch)
    {
        var path = RouteParameterRegex().Replace(operation.Path, "00000000-0000-4000-8000-000000000001");
        using var request = new HttpRequestMessage(new HttpMethod(operation.Method), path)
        {
            Content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request, CancellationToken.None);
    }

    private static List<RequiredOperation> ReadRequiredOperations()
    {
        var path = Path.Combine(FindRepositoryRoot(), "contracts", "openapi.json");
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var operations = new List<RequiredOperation>();

        foreach (var route in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var candidate in route.Value.EnumerateObject())
            {
                if (!IsHttpMethod(candidate.Name) || !RequiresIfMatch(candidate.Value))
                {
                    continue;
                }

                operations.Add(new RequiredOperation(
                    candidate.Name.ToUpperInvariant(),
                    "/api/v1" + route.Name,
                    candidate.Value.GetProperty("operationId").GetString()!));
            }
        }

        return operations;
    }

    private static bool RequiresIfMatch(JsonElement operation) =>
        operation.TryGetProperty("parameters", out var parameters)
        && parameters.EnumerateArray().Any(parameter =>
            parameter.GetProperty("name").GetString() == "If-Match"
            && parameter.GetProperty("required").GetBoolean());

    private static bool IsHttpMethod(string value) =>
        value is "get" or "post" or "put" or "patch" or "delete";

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

    private sealed record RequiredOperation(string Method, string Path, string OperationId);

    [GeneratedRegex("\\{[^}]+\\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteParameterRegex();
}
