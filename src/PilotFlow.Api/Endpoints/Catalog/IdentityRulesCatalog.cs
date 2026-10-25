namespace PilotFlow.Api.Endpoints.Catalog;

public static class IdentityRulesCatalog
{
    public static IReadOnlyList<EndpointDescriptor> Endpoints { get; } =
    [
        new(HttpMethods.Get, "/api/v1/me", "getMe"),
        new(HttpMethods.Get, "/api/v1/rules/{ruleId}", "getRules"),
    ];
}
