namespace PilotFlow.Api.Endpoints.Catalog;

public static class FamilyRouteCatalog
{
    private const string Version = "/api/v1/analyses/{analysisId}/versions/{versionId}";

    public static IReadOnlyList<EndpointDescriptor> Endpoints { get; } =
    [
        new(HttpMethods.Get, $"{Version}/families", "listFamilies"),
        new(HttpMethods.Post, $"{Version}/families", "createFamily", true),
        new(HttpMethods.Get, $"{Version}/families/{{familyId}}", "getFamily"),
        new(HttpMethods.Put, $"{Version}/families/{{familyId}}", "replaceFamily", true),
        new(HttpMethods.Delete, $"{Version}/families/{{familyId}}", "deleteFamily", true),
        new(HttpMethods.Get, $"{Version}/family-suggestions", "suggestFamilies"),
        new(HttpMethods.Put, $"{Version}/assignments", "assignProducts", true),
        new(HttpMethods.Get, $"{Version}/families/{{familyId}}/metrics", "getFamilyMetrics"),
        new(HttpMethods.Put, $"{Version}/families/{{familyId}}/metrics", "replaceFamilyMetrics", true),
        new(HttpMethods.Get, $"{Version}/family-matrix", "getFamilyMatrix"),
        new(HttpMethods.Get, $"{Version}/products/{{productId}}/assignment-options", "getAssignmentOptions"),
    ];
}
