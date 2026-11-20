namespace PilotFlow.Domain.Evaluation;

public static class RankingRules
{
    public const decimal RevenueMinimum = 10m;
    public const decimal DurationMaximum = 30m;

    public static decimal? SalesGrowth(decimal? percent) => percent switch
    {
        null or < 0m => null,
        > 30m => 1.5m,
        >= 10m => 1m,
        _ => 0.5m,
    };

    public static decimal? Production(string? type) => type switch
    {
        "largeSeries" => 3m,
        "mediumSeries" => 2m,
        "smallSeries" => 1m,
        "project" => 0m,
        _ => null,
    };

    public static decimal RankedScore(int rank, decimal other) => rank switch
    {
        1 => 3m,
        2 => 2m,
        _ => other,
    };

    public static Dictionary<T, int> DenseRanks<T>(IEnumerable<T?> values, bool ascending = false)
        where T : struct, IComparable<T>
    {
        var distinct = values.OfType<T>().Distinct();
        var ordered = ascending ? distinct.Order() : distinct.OrderDescending();
        return ordered.Select((value, index) => (value, rank: index + 1))
            .ToDictionary(item => item.value, item => item.rank);
    }

    public static ExactRatio? Percentage(decimal? amount, decimal? total) => ExactRatio.Divide(amount, total, 100);
}
