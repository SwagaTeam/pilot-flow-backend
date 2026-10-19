using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace PilotFlow.Api.Authentication;

public static class AuthenticationExtensions
{
    public const string AnalysisUserPolicy = "analysis-user";

    public static IServiceCollection AddPilotFlowAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (environment.IsProduction()
            && (string.IsNullOrWhiteSpace(jwt.Authority) || string.IsNullOrWhiteSpace(jwt.Audience)))
        {
            throw new InvalidOperationException("Jwt:Authority and Jwt:Audience are required in Production.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = EmptyToNull(jwt.Authority);
                options.Audience = EmptyToNull(jwt.Audience);
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                };
            });

        services
            .AddAuthorizationBuilder()
            .AddPolicy(AnalysisUserPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim("sub"));

        return services;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
