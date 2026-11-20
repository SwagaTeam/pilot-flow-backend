namespace PilotFlow.Api.Concurrency;

internal static class DecisionEtag
{
    internal static string Format(string snapshotSha256) => $"\"sha256:{snapshotSha256}\"";
}
