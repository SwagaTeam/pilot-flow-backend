using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PilotFlow.Application.Analyses;
using PilotFlow.Infrastructure.Persistence.Analyses;

namespace PilotFlow.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddAnalysisPersistence(this IServiceCollection services, string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(settings.Host))
            {
                throw new InvalidOperationException("The PilotFlow connection string must specify Host.");
            }

            services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        }

        services.AddDbContext<AnalysisDbContext>((provider, options) =>
        {
            if (provider.GetService<NpgsqlDataSource>() is { } dataSource)
            {
                options.UseNpgsql(dataSource);
            }
            else
            {
                options.UseNpgsql((string?)null);
            }
        });
        return services.AddScoped<IAnalysisStore, PostgresAnalysisStore>();
    }
}
