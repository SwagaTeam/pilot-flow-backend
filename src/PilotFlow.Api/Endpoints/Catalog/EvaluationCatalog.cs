namespace PilotFlow.Api.Endpoints.Catalog;

public static class EvaluationCatalog
{
    private const string Version = "/api/v1/analyses/{analysisId}/versions/{versionId}";

    public static IReadOnlyList<EndpointDescriptor> Endpoints { get; } =
    [
        new(HttpMethods.Get, $"{Version}/evaluation", "evaluateVersion"),
        new(HttpMethods.Get, $"{Version}/validation", "validateVersion"),
        new(HttpMethods.Get, $"{Version}/direct-selection-options", "getDirectSelectionOptions"),
    ];
}
