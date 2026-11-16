using System.Xml.Linq;

namespace PilotFlow.ContractTests.Architecture;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void ProjectReferences_MatchApprovedDependencyGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["src/PilotFlow.Domain/PilotFlow.Domain.csproj"] = [],
            ["src/PilotFlow.Application/PilotFlow.Application.csproj"] =
                ["src/PilotFlow.Domain/PilotFlow.Domain.csproj"],
            ["src/PilotFlow.DbMigrator/PilotFlow.DbMigrator.csproj"] =
                ["src/PilotFlow.Infrastructure/PilotFlow.Infrastructure.csproj"],
            ["src/PilotFlow.Infrastructure/PilotFlow.Infrastructure.csproj"] =
                [
                    "src/PilotFlow.Application/PilotFlow.Application.csproj",
                    "src/PilotFlow.Domain/PilotFlow.Domain.csproj",
                ],
            ["src/PilotFlow.Api/PilotFlow.Api.csproj"] =
                [
                    "src/PilotFlow.Application/PilotFlow.Application.csproj",
                    "src/PilotFlow.Infrastructure/PilotFlow.Infrastructure.csproj",
                ],
            ["tests/PilotFlow.ContractTests/PilotFlow.ContractTests.csproj"] =
                ["src/PilotFlow.Api/PilotFlow.Api.csproj"],
            ["tests/PilotFlow.Api.Tests/PilotFlow.Api.Tests.csproj"] =
                ["src/PilotFlow.Api/PilotFlow.Api.csproj"],
            ["tests/PilotFlow.IntegrationTests/PilotFlow.IntegrationTests.csproj"] =
                ["src/PilotFlow.Api/PilotFlow.Api.csproj", "src/PilotFlow.Infrastructure/PilotFlow.Infrastructure.csproj"],
        };

        foreach (var (projectPath, expectedReferences) in expected)
        {
            var fullProjectPath = Path.Combine(repositoryRoot, projectPath);
            var projectDirectory = Path.GetDirectoryName(fullProjectPath)
                ?? throw new InvalidOperationException($"Project has no directory: {projectPath}");
            var document = XDocument.Load(fullProjectPath);
            var actualReferences = document
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(static include => include is not null)
                .Select(include => Path.GetRelativePath(repositoryRoot, Path.GetFullPath(include!, projectDirectory)))
                .Select(NormalizePath)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

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

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
