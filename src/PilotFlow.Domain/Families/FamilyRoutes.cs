using PilotFlow.Domain.Catalogs;
using PilotFlow.Domain.Evaluation;

namespace PilotFlow.Domain.Families;

public sealed record ProductFamilyRoute(Guid ProductId, RouteSignature Signature, decimal? Duration);

public sealed class RouteSignature : IEquatable<RouteSignature>
{
    private readonly (Guid OperationId, string? Workplace)[] occurrences;
    private readonly bool[] approvedSections;

    public RouteSignature(IEnumerable<RouteStepData> steps, IReadOnlyDictionary<Guid, OperationData> operations)
    {
        var ordered = steps.OrderBy(static step => step.Position).ToArray();
        occurrences = new (Guid, string?)[ordered.Length];
        approvedSections = new bool[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            var step = ordered[i];
            var operation = operations[step.OperationId];
            occurrences[i] = (step.OperationId, step.WorkplaceName ?? operation.WorkplaceName);
            approvedSections[i] = operation.Kind == "section" && operation.AggregationCoveragePercent >= 80
                && FamilyValidation.HasEvidence(operation.AggregationEvidence)
                && !string.IsNullOrWhiteSpace(operation.AggregationEvidence.Basis);
            HasUnconfirmedSection |= operation.Kind == "section" && !approvedSections[i];
        }
    }

    public int Count => occurrences.Length;
    public bool HasUnconfirmedSection { get; }
    public IReadOnlyList<Guid> OperationIds => occurrences.Select(static step => step.OperationId).ToArray();
    public bool Matches(RouteSignature other) => Count > 0 && Equals(other);

    public bool IsCompatibleWith(RouteSignature product, string? compositionReason)
    {
        if (Count == 0 || product.Count == 0)
        {
            return false;
        }

        if (Matches(product))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(compositionReason) && approvedSections.Contains(true)
            && MatchesWithSectionAggregation(product);
    }

    public bool Equals(RouteSignature? other) =>
        other is not null && occurrences.AsSpan().SequenceEqual(other.occurrences);

    public override bool Equals(object? obj) => obj is RouteSignature signature && Equals(signature);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var occurrence in occurrences)
        {
            hash.Add(occurrence);
        }

        return hash.ToHashCode();
    }

    private bool MatchesWithSectionAggregation(RouteSignature product)
    {
        // A documented section can cover a contiguous part of a product route. Other occurrences retain their sequence.
        var boundaries = new bool[product.Count + 1];
        boundaries[0] = true;
        for (var i = 0; i < Count; i++)
        {
            var next = new bool[product.Count + 1];
            var foundStart = false;
            for (var j = 0; j < product.Count; j++)
            {
                foundStart |= boundaries[j];
                next[j + 1] = approvedSections[i]
                    ? foundStart
                    : boundaries[j] && occurrences[i] == product.occurrences[j];
            }

            boundaries = next;
        }

        return boundaries[^1];
    }
}

public static class FamilyCycle
{
    private static readonly ExactRatio Recommendation = ExactRatio.From(20m);

    public static bool ExceedsRecommendation(ExactRatio? difference) => difference > Recommendation;

    public static decimal? RouteDuration(IEnumerable<RouteStepData> steps)
    {
        decimal? total = null;
        foreach (var step in steps)
        {
            if (step.DurationHours is null)
            {
                return null;
            }

            total = (total ?? 0) + step.DurationHours.Value;
        }

        return total;
    }

    public static ExactRatio? Difference(IEnumerable<ProductFamilyRoute> products) =>
        Difference(products.Select(static product => product.Duration));

    public static ExactRatio? Difference(IEnumerable<decimal?> durations)
    {
        decimal? min = null;
        decimal? max = null;
        foreach (var duration in durations)
        {
            if (duration is not { } hours)
            {
                return null;
            }

            min = min is { } shortest ? Math.Min(shortest, hours) : hours;
            max = max is { } longest ? Math.Max(longest, hours) : hours;
        }

        return RankingRules.Percentage(max - min, min);
    }
}
