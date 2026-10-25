namespace PilotFlow.Api.Errors;

public sealed record ApiProblem(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string Code,
    string TraceId,
    IReadOnlyList<ApiIssue> Issues);
