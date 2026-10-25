namespace PilotFlow.Api.Endpoints.Catalog;

public static class ProductOperationCatalog
{
    private const string Version = "/api/v1/analyses/{analysisId}/versions/{versionId}";

    public static IReadOnlyList<EndpointDescriptor> Endpoints { get; } =
    [
        new(HttpMethods.Get, $"{Version}/products", "listProducts"),
        new(HttpMethods.Post, $"{Version}/products", "createProduct", true),
        new(HttpMethods.Get, $"{Version}/products/{{productId}}", "getProduct"),
        new(HttpMethods.Put, $"{Version}/products/{{productId}}", "replaceProduct", true),
        new(HttpMethods.Delete, $"{Version}/products/{{productId}}", "deleteProduct", true),
        new(HttpMethods.Get, $"{Version}/operations", "listOperations"),
        new(HttpMethods.Post, $"{Version}/operations", "createOperation", true),
        new(HttpMethods.Get, $"{Version}/operations/{{operationId}}", "getOperation"),
        new(HttpMethods.Put, $"{Version}/operations/{{operationId}}", "replaceOperation", true),
        new(HttpMethods.Delete, $"{Version}/operations/{{operationId}}", "deleteOperation", true),
        new(HttpMethods.Get, $"{Version}/products/{{productId}}/route", "getProductRoute"),
        new(HttpMethods.Put, $"{Version}/products/{{productId}}/route", "replaceProductRoute", true),
    ];
}
