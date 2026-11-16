using PilotFlow.Api.Endpoints.Catalog;

namespace PilotFlow.Api.Endpoints;

public static class EndpointCatalog
{
    public static IReadOnlyList<EndpointDescriptor> All { get; } =
    [
        .. AnalysisVersionCatalog.Endpoints,
        .. FamilyRouteCatalog.Endpoints,
        .. EvaluationCatalog.Endpoints,
        .. ProtocolSelectionCatalog.Endpoints,
        .. DecisionExportCatalog.Endpoints,
    ];
}
