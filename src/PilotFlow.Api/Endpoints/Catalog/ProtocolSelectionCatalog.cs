namespace PilotFlow.Api.Endpoints.Catalog;

public static class ProtocolSelectionCatalog
{
    private const string Version = "/api/v1/analyses/{analysisId}/versions/{versionId}";

    public static IReadOnlyList<EndpointDescriptor> Endpoints { get; } =
    [
        new(HttpMethods.Get, $"{Version}/protocol", "getProtocol"),
        new(HttpMethods.Put, $"{Version}/protocol", "replaceProtocol", true),
        new(HttpMethods.Get, $"{Version}/selection", "getSelection"),
        new(HttpMethods.Put, $"{Version}/selection", "replaceSelection", true),
        new(HttpMethods.Get, $"{Version}/protocol-preview", "previewProtocol"),
        new(HttpMethods.Post, $"{Version}/finalize", "finalizeDecision", true),
    ];
}
