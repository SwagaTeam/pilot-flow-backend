using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Evaluation;
using PilotFlow.Infrastructure.Persistence.Catalogs;

namespace PilotFlow.Infrastructure.Persistence.Evaluation;

internal sealed class PostgresEvaluationStore(
    VersionCatalogTransactions versions, EvaluationAggregateReader reader) : IEvaluationStore
{
    public Task<Versioned<EvaluationAggregate>?> GetAsync(VersionScope scope, CancellationToken cancellationToken) =>
        versions.ReadAsync(scope, token => reader.ReadAsync(scope, token), cancellationToken);
}
