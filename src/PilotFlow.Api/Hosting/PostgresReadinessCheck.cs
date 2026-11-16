using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace PilotFlow.Api.Hosting;

internal sealed class PostgresReadinessCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var dataSource = services.GetService<NpgsqlDataSource>();
        if (dataSource is null)
        {
            return HealthCheckResult.Unhealthy();
        }

        try
        {
            await using var command = dataSource.CreateCommand("""
                SELECT 1 FROM pilot.analysis LIMIT 0;
                SELECT 1 FROM pilot.product_completeness LIMIT 0;
                """);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
