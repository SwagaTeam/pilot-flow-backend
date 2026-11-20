namespace PilotFlow.Api.Endpoints.Catalog;

public static class DecisionExportCatalog
{
    private const string Analysis = "/api/v1/analyses/{analysisId}";

    public static IReadOnlyList<EndpointDescriptor> Endpoints { get; } =
    [
        new(HttpMethods.Get, $"{Analysis}/versions/{{versionId}}/export", "exportDraft"),
        new(HttpMethods.Get, $"{Analysis}/decisions/{{decisionId}}/export", "exportDecision"),
    ];
}
