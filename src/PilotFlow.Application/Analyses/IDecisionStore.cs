using System.Text.Json;

namespace PilotFlow.Application.Analyses;

public interface IDecisionStore
{
    Task<Page<DecisionDetails>?> ListAsync(string owner, Guid analysisId, PageRequest page, CancellationToken cancellationToken);

    Task<DecisionDetails?> GetAsync(string owner, Guid analysisId, Guid decisionId, CancellationToken cancellationToken);
}

public sealed record DecisionDetails(
    Guid Id, Guid VersionId, DateTimeOffset CreatedAt, string CreatedBySubject,
    string CreatedByName, string SnapshotSha256, JsonElement Snapshot);
