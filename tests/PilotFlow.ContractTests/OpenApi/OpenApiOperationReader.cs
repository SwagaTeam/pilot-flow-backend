using System.Text.Json;

namespace PilotFlow.ContractTests.OpenApi;

public static class OpenApiOperationReader
{
    private static readonly HashSet<string> HttpMethods =
        new(StringComparer.OrdinalIgnoreCase) { "get", "post", "put", "patch", "delete" };

    public static IReadOnlyList<ApiOperation> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var operations = new List<ApiOperation>();

        foreach (var route in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var candidate in route.Value.EnumerateObject())
            {
                if (!HttpMethods.Contains(candidate.Name))
                {
                    continue;
                }

                operations.Add(new ApiOperation(
                    candidate.Name.ToUpperInvariant(),
                    "/api/v1" + route.Name,
                    candidate.Value.GetProperty("operationId").GetString()
                        ?? throw new InvalidDataException($"Operation {candidate.Name} {route.Name} has no operationId.")));
            }
        }

        return operations;
    }
}
