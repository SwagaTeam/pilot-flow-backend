using PilotFlow.Api.Authentication;

namespace PilotFlow.Api.Hosting;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddPilotFlowApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddPilotFlowAuthentication(configuration, environment);

        return services;
    }
}
