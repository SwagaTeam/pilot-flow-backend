namespace PilotFlow.Api.Endpoints;

public sealed record EndpointDescriptor(
    string Method,
    string Pattern,
    string OperationId,
    bool RequiresIfMatch = false);
