using PilotFlow.Api.Endpoints.Catalog;

namespace PilotFlow.Api.Endpoints;

public static class EndpointCatalog
{
    public static IReadOnlyList<EndpointDescriptor> All { get; } =
    [
        .. IdentityRulesCatalog.Endpoints,
        .. AnalysisVersionCatalog.Endpoints,
        .. ProductOperationCatalog.Endpoints,
        .. FamilyRouteCatalog.Endpoints,
        .. EvaluationCatalog.Endpoints,
        .. ProtocolSelectionCatalog.Endpoints,
        .. DecisionExportCatalog.Endpoints,
    ];
}
