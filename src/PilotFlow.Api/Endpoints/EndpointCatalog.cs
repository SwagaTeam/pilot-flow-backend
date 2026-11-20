using PilotFlow.Api.Endpoints.Catalog;

namespace PilotFlow.Api.Endpoints;

public static class EndpointCatalog
{
    public static IReadOnlyList<EndpointDescriptor> All { get; } =
    [
        .. DecisionExportCatalog.Endpoints,
    ];
}
