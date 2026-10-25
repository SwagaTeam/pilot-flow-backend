namespace PilotFlow.Api.Errors;

public sealed record ApiIssue(
    string Code,
    string Severity,
    string Path,
    string Message,
    string Suggestion,
    Guid? EntityId);
