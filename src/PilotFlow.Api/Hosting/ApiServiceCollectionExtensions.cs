using System.Text.Json.Serialization;
using PilotFlow.Api.Authentication;
using PilotFlow.Api.Controllers;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Analyses;
using PilotFlow.Infrastructure.Persistence;

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
        var connectionString = configuration.GetConnectionString("PilotFlow");
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:PilotFlow is required in Production.");
        }

        services.AddAnalysisPersistence(connectionString);
        services.AddScoped<AnalysisCommands>();
        services.AddHealthChecks().AddCheck<PostgresReadinessCheck>("postgres", timeout: TimeSpan.FromSeconds(5));
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new DecimalStringConverter()));
        services.AddControllers(options => options.Filters.Add<JsonInputProblemFilter>())
            .AddJsonOptions(options =>
            {
                var json = options.JsonSerializerOptions;
                json.Converters.Add(new DecimalStringConverter());
                json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
                json.PropertyNameCaseInsensitive = false;
                json.RespectNullableAnnotations = true;
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context => ProblemResults.InvalidRequest(context.HttpContext);
                options.SuppressMapClientErrors = true;
            });

        return services;
    }
}
