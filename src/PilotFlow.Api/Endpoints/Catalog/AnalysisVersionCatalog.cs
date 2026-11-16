namespace PilotFlow.Api.Endpoints.Catalog;

public static class AnalysisVersionCatalog
{
    public static IReadOnlyList<EndpointDescriptor> Endpoints { get; } =
    [
        new(HttpMethods.Post, "/api/v1/analyses/{analysisId}/versions/{versionId}/fork", "forkVersion", true),
    ];
}
