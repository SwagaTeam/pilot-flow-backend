using System.Globalization;
using System.Text.RegularExpressions;

namespace PilotFlow.Api.Concurrency;

public sealed partial record AggregateEtag(Guid VersionId, long Revision)
{
    public const string HttpContextKey = "PilotFlow.AggregateEtag";

    public static bool TryParse(string? value, out AggregateEtag etag)
    {
        var match = StrongEtagRegex().Match(value ?? string.Empty);
        if (match.Success
            && Guid.TryParseExact(match.Groups[1].Value, "D", out var versionId)
            && long.TryParse(
                match.Groups[2].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var revision)
            && revision > 0)
        {
            etag = new AggregateEtag(versionId, revision);
            return true;
        }

        etag = null!;
        return false;
    }

    public override string ToString() =>
        FormattableString.Invariant($"\"v:{VersionId:D}:r:{Revision}\"");

    [GeneratedRegex(
        "^\"v:([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}):r:([1-9][0-9]*)\"$",
        RegexOptions.CultureInvariant)]
    private static partial Regex StrongEtagRegex();
}
