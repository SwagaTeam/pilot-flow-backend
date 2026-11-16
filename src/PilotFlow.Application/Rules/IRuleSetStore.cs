namespace PilotFlow.Application.Rules;

public interface IRuleSetStore
{
    Task<RuleSet?> GetAsync(string version, CancellationToken cancellationToken);
}

public sealed record RuleSet(string Version, string Status, string Source, IReadOnlyList<RulePolicy> Policies);

public sealed record RulePolicy(string Code, string Description, string Status);
