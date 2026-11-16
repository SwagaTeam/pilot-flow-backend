using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Rules;
using PilotFlow.Infrastructure.Persistence.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Rules;

internal sealed class PostgresRuleSetStore(AnalysisDbContext db) : IRuleSetStore
{
    public async Task<RuleSet?> GetAsync(string version, CancellationToken cancellationToken)
    {
        if (version != InitialRuleSetPolicies.Version)
        {
            return null;
        }

        db.EnsureConfigured();
        var row = await db.Database.SqlQuery<RuleSetRow>($"""
            SELECT version AS "Version", status AS "Status", specification->>'source' AS "Source"
            FROM pilot.rule_set
            WHERE version = {version}
            """).SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new RuleSet(row.Version, row.Status, row.Source, InitialRuleSetPolicies.Policies);
    }

    private sealed record RuleSetRow(string Version, string Status, string Source);
}
