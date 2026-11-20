namespace PilotFlow.Application.Analyses;

public interface IForkVersionStore
{
    Task<AnalysisWriteResult<AnalysisVersionDetails>> ForkAsync(
        string owner, VersionUpdate source, string reason, CancellationToken cancellationToken);
}
