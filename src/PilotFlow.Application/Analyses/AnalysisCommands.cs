using PilotFlow.Domain.Analyses;

namespace PilotFlow.Application.Analyses;

public sealed class AnalysisCommands(IAnalysisStore store)
{
    public async Task<AnalysisWriteResult<AnalysisDetails>> CreateAsync(
        string owner, AnalysisData data, CancellationToken cancellationToken)
    {
        var issues = AnalysisValidation.Validate(data);
        if (issues.Count > 0)
        {
            return new(AnalysisWriteStatus.ValidationFailed, Issues: issues);
        }

        var created = await store.CreateAsync(owner, data, cancellationToken);
        return new(AnalysisWriteStatus.Success, created);
    }

    public async Task<AnalysisWriteResult<AnalysisVersionDetails>> ReplaceAsync(
        string owner, VersionUpdate target, AnalysisData data, CancellationToken cancellationToken)
    {
        var issues = AnalysisValidation.Validate(data);
        if (issues.Count > 0)
        {
            var version = await store.GetVersionAsync(owner, target.AnalysisId, target.VersionId, cancellationToken);
            if (version is null)
            {
                return new(AnalysisWriteStatus.NotFound);
            }

            if (version.State == "fixed")
            {
                return new(AnalysisWriteStatus.Fixed);
            }

            if (version.Id != target.ExpectedVersionId || version.Revision != target.ExpectedRevision)
            {
                return new(AnalysisWriteStatus.RevisionConflict);
            }

            return new(AnalysisWriteStatus.ValidationFailed, Issues: issues);
        }

        return await store.ReplaceVersionAsync(owner, target, data, cancellationToken);
    }
}
