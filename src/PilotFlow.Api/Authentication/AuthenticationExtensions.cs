using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PilotFlow.Api.Errors;

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

        if (environment.IsProduction())
        {
            ValidateProductionOptions(jwt);
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
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = static context =>
                    {
                        if (!HasSubject(context.Principal))
                        {
                            context.Fail("A valid subject claim is required.");
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = static async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                        await ProblemResults.Unauthorized(context.HttpContext)
                            .ExecuteAsync(context.HttpContext);
                    },
                };
            });

        services
            .AddAuthorizationBuilder()
            .AddPolicy(AnalysisUserPolicy, policy => policy.RequireAuthenticatedUser()
                .RequireAssertion(context => HasSubject(context.User)));

        return services;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool HasSubject(ClaimsPrincipal? principal) =>
        principal?.FindFirst("sub")?.Value is { Length: <= 300 } subject
        && !string.IsNullOrWhiteSpace(subject) && !subject.Contains('\0', StringComparison.Ordinal);

    private static void ValidateProductionOptions(JwtOptions jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt.Authority) || string.IsNullOrWhiteSpace(jwt.Audience))
        {
            throw new InvalidOperationException("Jwt:Authority and Jwt:Audience are required in Production.");
        }

        if (!Uri.TryCreate(jwt.Authority, UriKind.Absolute, out var authority)
            || authority.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Jwt:Authority must be an absolute HTTPS URI in Production.");
        }
    }
}
