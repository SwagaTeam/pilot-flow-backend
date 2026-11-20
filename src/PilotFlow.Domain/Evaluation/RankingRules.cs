namespace PilotFlow.Domain.Evaluation;

public static class RankingRules
{
    public const decimal RevenueMinimum = 10m;
    public const decimal DurationMaximum = 30m;
    public const decimal CycleRecommendation = 20m;

    public static decimal? SalesGrowth(decimal? percent) => percent switch
    {
        null or < 0m => null,
        > 30m => 1.5m,
        >= 10m => 1m,
        _ => 0.5m,
    };

    public static decimal? Load(decimal? percent) => percent switch
    {
        null => null,
        > 80m => 1m,
        >= 50m => 2m,
        _ => 3m,
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

    public static Dictionary<decimal, int> DenseRanks(IEnumerable<decimal?> values, bool ascending = false)
    {
        var distinct = values.OfType<decimal>().Distinct();
        var ordered = ascending ? distinct.Order() : distinct.OrderDescending();
        return ordered.Select((value, index) => (value, rank: index + 1))
            .ToDictionary(item => item.value, item => item.rank);
    }

    public static decimal? Percentage(decimal? amount, decimal? total)
    {
        if (amount is not { } numerator || total is not > 0)
        {
            return null;
        }

        // Multiply first to preserve precision at the admission and scoring thresholds.
        // Clamp only ratios beyond decimal's range, which are unrepresentable on the wire too.
        try
        {
            return numerator * 100m / total;
        }
        catch (OverflowException)
        {
            return numerator < 0 ? decimal.MinValue : decimal.MaxValue;
        }
    }
}
