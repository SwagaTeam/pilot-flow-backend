using PilotFlow.Domain.Evaluation;
using PilotFlow.Domain.Families;
using static PilotFlow.Application.Evaluation.EvaluationCalculator;

namespace PilotFlow.Application.Evaluation;

internal static class FamilyMatrixCalculator
{
    internal static FamilyMatrixResult Calculate(EvaluationInputs input, ILookup<Guid?, EvaluationProduct> members)
    {
        var routeDurations = input.Products.ToDictionary(product => product.Id,
            product => product.RouteDurations.Count == 0 ? null : SumKnown(product.RouteDurations));
        var products = input.Products.Select(product =>
        {
            var margin = product.Revenue - product.RawMaterialCost;
            return new ProductFinancialResult(product.Id, product.FamilyId, Wire(margin),
                product.SalesVolume is > 0 ? Wire(margin / product.SalesVolume) : null,
                Wire(RankingRules.Percentage(margin, product.Revenue)),
                Wire(RankingRules.Percentage(product.Revenue, input.Version.Data.TotalRevenue)),
                Wire(routeDurations[product.Id]), product.RouteDurations.Count);
        }).ToArray();

        var cycles = input.Families.Select(family =>
        {
            var durations = members[family.Id].Select(product => routeDurations[product.Id]).ToArray();
            var unknown = durations.Length == 0 || durations.Any(duration => duration is null);
            var shortest = durations.Min();
            var difference = FamilyCycle.Difference(durations);
            if (difference is null)
            {
                var reason = shortest == 0m && !unknown ? "Минимальное время цикла равно нулю."
                    : "Нет полного набора времён операций для расчёта циклов.";
                return new FamilyCycleResult(family.Id, null, "unknown",
                    [Issue("CYCLE_DIFFERENCE_UNKNOWN", "/cycles", reason,
                        "Уточните время каждого вхождения маршрутов.", family.Id, "warning")]);
            }

            var exceeds = difference > RankingRules.CycleRecommendation;
            return new FamilyCycleResult(family.Id, Wire(difference), exceeds ? "exceedsRecommendation" : "withinRecommendation",
                exceeds ? [Issue("CYCLE_DIFFERENCE_ABOVE_RECOMMENDATION", "/cycles", "Разница времени циклов выше 20%.",
                    "Укажите обоснование состава семейства.", family.Id, "warning")] : []);
        }).ToArray();

        var issues = input.Products.Where(product => !product.IsOther && routeDurations[product.Id] is null)
            .Select(product => Issue("ROUTE_DURATION_UNKNOWN", "/products/routeDurationHours",
                "Полное время маршрута неизвестно.", "Заполните время каждого вхождения маршрута.", product.Id, "warning"))
            .Concat(cycles.SelectMany(cycle => cycle.Issues)).Take(1000).ToArray();

        return new(input.Version.Id, input.Version.Revision, products, cycles, issues);
    }
}
