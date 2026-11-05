using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using PilotFlow.ContractTests.OpenApi;

namespace PilotFlow.ContractTests;

public sealed class OpenApiEndpointParityTests
{
    private static readonly string ContractPath = Path.Combine(
        FindRepositoryRoot(),
        "contracts",
        "openapi.json");

    [Fact]
    public void RegisteredEndpoints_ExactlyMatchOpenApiOperations()
    {
        var expected = OpenApiOperationReader.Read(ContractPath)
            .OrderBy(Key, StringComparer.Ordinal)
            .ToArray();
        var actual = RegisteredEndpointReader.Read()
            .OrderBy(Key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(45, expected.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RegisteredEndpoints_HaveUniqueMethodAndPath()
    {
        var duplicates = RegisteredEndpointReader.Read()
            .GroupBy(operation => $"{operation.Method} {operation.Path}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void RegisteredEndpoints_HaveUniqueOperationIds()
    {
        var duplicates = RegisteredEndpointReader.Read()
            .GroupBy(operation => operation.OperationId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Reader_UnnamedBusinessRoute_IsRejected()
    {
        var builder = new RouteEndpointBuilder(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/me"),
            order: 0);
        builder.Metadata.Add(new HttpMethodMetadata([HttpMethods.Get]));

        var exception = Assert.Throws<InvalidDataException>(
            () => RegisteredEndpointReader.Read([builder.Build()]));

        Assert.Contains("endpoint name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string Key(ApiOperation operation) =>
        $"{operation.Method} {operation.Path} {operation.OperationId}";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }
}
