using PilotFlow.Domain.Analyses;

namespace PilotFlow.Application.Analyses;

public sealed record AnalysisDetails(Guid Id, string OwnerSubject, DateTimeOffset CreatedAt, AnalysisVersionDetails Version);

public sealed record AnalysisVersionDetails(
    Guid Id,
    Guid AnalysisId,
    int VersionNumber,
    long Revision,
    string State,
    string RulesVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AnalysisData Data);

public sealed record PageRequest(int Offset, int Limit, string? Query);

public sealed record Page<T>(IReadOnlyList<T> Items, int Offset, int Limit, int Total, int UnfilteredTotal);

public sealed record VersionUpdate(Guid AnalysisId, Guid VersionId, Guid ExpectedVersionId, long ExpectedRevision);

public enum AnalysisWriteStatus
{
    Success,
    NotFound,
    Fixed,
    RevisionConflict,
    ValidationFailed,
    StateConflict,
}

public sealed record AnalysisWriteResult<T>(AnalysisWriteStatus Status, T? Value = default, IReadOnlyList<AnalysisIssue>? Issues = null);

public sealed class AnalysisStoreUnavailableException : InvalidOperationException
{
    public AnalysisStoreUnavailableException()
        : base("Analysis database is not configured.")
    {
    }
}
