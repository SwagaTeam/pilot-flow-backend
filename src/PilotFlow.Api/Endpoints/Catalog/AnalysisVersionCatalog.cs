namespace PilotFlow.Api.Endpoints.Catalog;

public static class AnalysisVersionCatalog
{
    public static IReadOnlyList<EndpointDescriptor> Endpoints { get; } =
    [
        new(HttpMethods.Post, "/api/v1/analyses", "createAnalysis"),
        new(HttpMethods.Get, "/api/v1/analyses", "listAnalyses"),
        new(HttpMethods.Get, "/api/v1/analyses/{analysisId}", "getAnalysis"),
        new(HttpMethods.Get, "/api/v1/analyses/{analysisId}/versions", "listVersions"),
        new(HttpMethods.Get, "/api/v1/analyses/{analysisId}/versions/{versionId}", "getVersion"),
        new(HttpMethods.Put, "/api/v1/analyses/{analysisId}/versions/{versionId}", "replaceVersionData", true),
        new(HttpMethods.Post, "/api/v1/analyses/{analysisId}/versions/{versionId}/fork", "forkVersion", true),
    ];
}
