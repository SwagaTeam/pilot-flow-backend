namespace PilotFlow.Api.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Authority { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;
}
