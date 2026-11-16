using PilotFlow.Domain.Analyses;

namespace PilotFlow.Application.Analyses;

public interface IAnalysisStore
{
    Task<AnalysisDetails> CreateAsync(string owner, AnalysisData data, CancellationToken cancellationToken);

    Task<Page<AnalysisDetails>> ListAsync(string owner, PageRequest page, CancellationToken cancellationToken);

    Task<AnalysisDetails?> GetAsync(string owner, Guid analysisId, CancellationToken cancellationToken);

    Task<Page<AnalysisVersionDetails>?> ListVersionsAsync(
        string owner, Guid analysisId, PageRequest page, CancellationToken cancellationToken);

    Task<AnalysisVersionDetails?> GetVersionAsync(
        string owner, Guid analysisId, Guid versionId, CancellationToken cancellationToken);

    Task<AnalysisWriteResult<AnalysisVersionDetails>> ReplaceVersionAsync(
        string owner, VersionUpdate target, AnalysisData data, CancellationToken cancellationToken);
}
