using System.Globalization;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Decisions;
using PilotFlow.Domain.Evaluation;

namespace PilotFlow.Application.Evaluation;

public static class EvaluationCalculator
{
    private static readonly ExactRatio Zero = ExactRatio.From(0m);
    private static readonly ExactRatio RevenueMinimum = ExactRatio.From(RankingRules.RevenueMinimum);
    private static readonly ExactRatio LoadMedium = ExactRatio.From(50m);
    private static readonly ExactRatio LoadHigh = ExactRatio.From(80m);
    private static readonly ExactRatio LoadFull = ExactRatio.From(100m);

    public static EvaluationAggregate Calculate(EvaluationInputs input, DateTimeOffset now)
    {
        var members = input.Products.ToLookup(product => product.FamilyId);
        var totals = input.Families.Select(family => Totals(family, members[family.Id].ToArray(), input.Version.Data)).ToArray();
        var admitted = totals.Where(item => item.Admission == "admitted").ToArray();
        var unknownAdmission = totals.Any(item => item.Admission == "unknown");
        var ranks = new ComparisonRanks(admitted, unknownAdmission, input.Version.Data.LaborPeriod);
        var scored = totals.Select(item => Score(item, ranks, input.Version.Data)).ToArray();
        var completeRanking = !unknownAdmission && admitted.Length > 0
            && scored.Where(item => item.Totals.Admission == "admitted").All(item => item.Complete);
        var overallRanks = RankingRules.DenseRanks(scored.Where(item => item.Complete).Select(item => (decimal?)item.Sum));
        var (revenue, remainder, issues) = Reconcile(input);
        var canRecommend = completeRanking && !issues.Any(IsError) && admitted.All(item => item.Family.Confirmed)
            && members[null].All(product => product.IsOther);
        var evaluations = scored.Select(item =>
        {
            var complete = completeRanking && item.Complete;
            int? rank = complete ? overallRanks[item.Sum] : null;
            var familyId = item.Totals.Family.Id;
            var path = $"/families/{familyId:D}";
            return new FamilyEvaluationResult(
                familyId, Wire(item.Totals.Revenue, path + "/revenue", familyId),
                Wire(item.Totals.Cost, path + "/rawMaterialCost", familyId),
                Wire(item.Totals.Revenue - item.Totals.Cost, path + "/marginIncome", familyId),
                Wire(item.Totals.Share, path + "/revenueSharePercent", familyId),
                Wire(item.Totals.Margin, path + "/marginPercent", familyId),
                Wire(item.Totals.MaxDays, path + "/maxCalendarDurationDays", familyId),
                item.Totals.Admission, item.Totals.Issues,
                item.Scores.Select(score => score.Result).ToArray(), Format(item.Sum), item.Count,
                complete ? Format(item.Sum) : null, rank,
                !complete && item.Totals.Admission != "excluded",
                canRecommend && item.Totals.Family.Confirmed && (rank == 1 || rank == 2 && item.Sum >= 7m));
        }).ToArray();
        var evaluation = new EvaluationResult(input.Version.Id, input.Version.Revision, input.Version.RulesVersion,
            now, Wire(revenue, "/reconciledRevenue"), Wire(remainder, "/remainderRevenue"), issues, evaluations,
            evaluations.Where(family => family.Rank == 1).Select(family => family.FamilyId).ToArray(),
            canRecommend, canRecommend);
        var matrix = FamilyMatrixCalculator.Calculate(input, members);
        var options = SelectionPolicy.DirectOptions(input, evaluations, revenue, remainder, members);
        var validation = VersionReadiness.Validate(input, evaluation, matrix, options);
        return new(input.Version, evaluation, matrix, options, validation, input.Protocol, input.Selection);
    }

    internal static string? Wire(decimal? number, string path, Guid? entityId = null)
    {
        if (number is not { } value)
        {
            return null;
        }

        var rounded = decimal.Round(value, 6, MidpointRounding.AwayFromZero);
        return rounded is <= -1_000_000_000_000_000_000m or >= 1_000_000_000_000_000_000m
            ? throw new EvaluationNumericRangeException(path, entityId) : Format(rounded);
    }

    internal static string? Wire(ExactRatio? number, string path, Guid? entityId = null) =>
        number is not { } ratio ? null : ratio.TryRoundForWire(out var rounded)
            ? Format(rounded) : throw new EvaluationNumericRangeException(path, entityId);

    private static string Format(decimal number) => number.ToString("0.######", CultureInfo.InvariantCulture);

    internal static decimal? SumKnown(IEnumerable<decimal?> numbers)
    {
        decimal sum = 0;
        foreach (var number in numbers)
        {
            if (number is null)
            {
                return null;
            }

            sum += number.Value;
        }

        return sum;
    }

    internal static bool IsError(EvaluationIssue issue) => issue.Severity == "error";
    internal static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
    internal static bool HasSource(AnalysisEvidence evidence) =>
        HasText(evidence.Source) && (evidence.Period is not null || evidence.AssessedOn is not null);

    internal static EvaluationIssue Issue(string code, string path, string message, string suggestion, Guid? entityId = null,
        string severity = "error") => new(code, severity, path, message, suggestion, entityId);

    private static (decimal? Revenue, decimal? Remainder, EvaluationIssue[] Issues) Reconcile(EvaluationInputs input)
    {
        var data = input.Version.Data;
        var revenue = SumKnown(input.Products.Select(product => product.Revenue));
        var remainder = data.TotalRevenue - revenue;
        var issues = new List<EvaluationIssue>();
        if (data.TotalRevenue is not > 0)
        {
            issues.Add(Issue("TOTAL_REVENUE_REQUIRED", "/data/totalRevenue", "Общая выручка должна быть положительной.",
                "Укажите общую выручку за финансовый период."));
        }

        if (revenue is null)
        {
            issues.Add(Issue("PRODUCT_REVENUE_MISSING", "/products", "Выручка части продукции неизвестна.",
                "Укажите выручку каждой позиции, включая «Прочее»."));
        }
        if (remainder < 0)
        {
            issues.Add(Issue("REVENUE_EXCEEDS_TOTAL", "/data/totalRevenue", "Сумма выручки продукции выше общей выручки.",
                "Сверьте выручку предприятия и выручку по позициям."));
        }
        else if (remainder > 0 && !HasText(data.RemainderExplanation) && !input.Products.Any(product => product.IsOther))
        {
            issues.Add(Issue("REVENUE_REMAINDER_UNEXPLAINED", "/data/remainderExplanation", "Остаток выручки не объяснён.",
                "Объясните остаток или отразите его отдельной строкой «Прочее»."));
        }

        return (revenue, remainder, issues.ToArray());
    }

    private static FamilyTotals Totals(EvaluationFamily family, EvaluationProduct[] products, AnalysisData data)
    {
        var revenue = products.Length > 0 ? SumKnown(products.Select(product => product.Revenue)) : null;
        var cost = products.Length > 0 ? SumKnown(products.Select(product => product.RawMaterialCost)) : null;
        var share = RankingRules.Percentage(revenue, data.TotalRevenue);
        var margin = RankingRules.Percentage(revenue - cost, revenue);
        var days = products.Select(product => product.CalendarDurationDays).Max();
        var issues = new List<EvaluationIssue>();
        var excluded = share < RevenueMinimum;
        var unknown = share is null;
        var familyPath = $"/families/{family.Id:D}";
        if (products.Length == 0)
        {
            issues.Add(Issue("EMPTY_FAMILY", familyPath, "В семействе нет продукции.", "Распределите продукцию.", family.Id));
        }

        if (share is null || excluded)
        {
            issues.Add(share is null
                ? Issue("REVENUE_SHARE_UNKNOWN", familyPath, "Доля выручки семейства неизвестна.",
                    "Укажите выручку членов семейства и общую выручку.", family.Id)
                : Issue("REVENUE_SHARE_BELOW_MINIMUM", familyPath, "Доля выручки семейства ниже 10%.",
                    "Проверьте выручку и состав семейства.", family.Id));
        }

        foreach (var product in products)
        {
            var productPath = $"/products/{product.Id:D}/data";
            if (product.CalendarDurationDays is not { } duration)
            {
                unknown = true;
                issues.Add(Issue("DURATION_UNKNOWN", productPath + "/calendarDurationDays", "Календарная длительность неизвестна.",
                    "Укажите длительность в календарных днях.", product.Id));
            }
            else if (duration > RankingRules.DurationMaximum)
            {
                excluded = true;
                issues.Add(Issue("DURATION_EXCEEDED", productPath + "/calendarDurationDays", "Длительность превышает 30 дней.",
                    "Проверьте границы и календарную длительность продукта.", product.Id));
            }

            if (product.InProductionPlan is null)
            {
                unknown = true;
                issues.Add(Issue("PRODUCTION_PLAN_UNKNOWN", productPath + "/inProductionPlan", "Нет данных о производственном плане.",
                    "Уточните, включён ли продукт в план.", product.Id));
            }
            else if (product.InProductionPlan == false)
            {
                excluded = true;
                issues.Add(Issue("NOT_IN_PRODUCTION_PLAN", productPath + "/inProductionPlan", "Продукт отсутствует в производственном плане.",
                    "Укажите данные утверждённого плана.", product.Id));
            }
        }

        return new(family, revenue, cost, share, margin, days,
            excluded ? "excluded" : unknown ? "unknown" : "admitted", issues.Take(1000).ToArray());
    }

    private static ScoredFamily Score(FamilyTotals totals, ComparisonRanks ranks, AnalysisData data)
    {
        var metric = totals.Family.Metrics;
        var labor = Labor(metric);
        var load = metric.LoadActual.HasValue && metric.LoadCapacity is > 0
            ? RankingRules.Percentage(metric.LoadActual, metric.LoadCapacity) : ExactRatio.From(metric.LoadPercent);
        var isApplicable = totals.Admission != "excluded";
        var eligibleForRank = totals.Admission == "admitted";
        var mayCompare = eligibleForRank && !ranks.UnknownAdmission;
        var shareRank = Rank(ranks.Share, totals.Share);
        var laborRank = Rank(ranks.Labor, labor);
        var hasMarginMaximum = ranks.BestMargin >= Zero;
        ScoreValue[] scores =
        [
            Criterion("revenueShare", totals.Share, "%", data.RevenueEvidence,
                eligibleForRank && shareRank != 0 ? RankingRules.RankedScore(shareRank, 1m) : null,
                shareRank == 0 ? "Доля выручки сравнивается среди допущенных потоков."
                    : FormattableString.Invariant($"Место {shareRank} по доле выручки среди допущенных потоков."),
                provisional: !mayCompare || !ranks.SharesComplete),
            Criterion("margin", totals.Margin, "%", data.RevenueEvidence,
                eligibleForRank && hasMarginMaximum && totals.Margin is { } margin
                    ? margin == ranks.BestMargin ? 3m : margin >= Zero ? 1.5m : 1m : null,
                totals.Margin switch
                {
                    { } value when value == ranks.BestMargin && value >= Zero => "Наибольшая маржинальность среди допущенных потоков.",
                    { } value when value >= Zero => "Неотрицательная маржинальность ниже максимума.",
                    { } value when value < Zero => "Отрицательная маржинальность.",
                    _ => "Маржинальность рассчитывается по суммам выручки и сырьевой себестоимости.",
                },
                provisional: !mayCompare || !ranks.MarginsComplete,
                unresolved: mayCompare && ranks.MarginsComplete && ranks.BestMargin < Zero),
            Criterion("salesGrowth", ExactRatio.From(metric.SalesGrowthPercent), "%", metric.SalesGrowthEvidence,
                RankingRules.SalesGrowth(metric.SalesGrowthPercent),
                metric.SalesGrowthPercent switch
                {
                    > 30 => "Прогноз роста выше 30%.",
                    >= 10 => "Прогноз роста от 10% до 30% включительно.",
                    >= 0 => "Прогноз роста от 0% до 10%, правая граница не включается.",
                    < 0 => "Прогноз снижения продаж.",
                    _ => "Прогноз роста продаж неизвестен.",
                }, unresolved: metric.SalesGrowthPercent < 0),
            Criterion("load", load, "%", metric.LoadEvidence,
                load is null ? null : load > LoadHigh ? 1m : load >= LoadMedium ? 2m : 3m,
                load switch
                {
                    { } value when value > LoadHigh => "Загрузка выше 80%.",
                    { } value when value >= LoadMedium => "Загрузка от 50% до 80% включительно.",
                    not null => "Загрузка ниже 50%.",
                    _ => "Загрузка неизвестна.",
                },
                extra: load > LoadFull ? Issue("LOAD_ABOVE_CAPACITY", "/metrics/loadPercent", "Загрузка выше 100%.",
                    "Проверьте основание расчёта загрузки.", totals.Family.Id, "warning") : null),
            Criterion("productionType", null, "type", metric.ProductionTypeEvidence,
                RankingRules.Production(metric.ProductionType),
                metric.ProductionType switch
                {
                    "largeSeries" => "Крупносерийное производство.",
                    "mediumSeries" => "Среднесерийное производство.",
                    "smallSeries" => "Мелкосерийное производство.",
                    "project" => "Проектное производство.",
                    _ => "Тип производства не указан.",
                }),
            Criterion("labor", labor, metric.LaborUnitCode is { } unit ? $"h/{unit}" : "h", metric.LaborEvidence,
                eligibleForRank && laborRank != 0 && ranks.CompatibleLabor ? RankingRules.RankedScore(laborRank, 0.5m) : null,
                laborRank == 0 ? "Сравнивается сопоставимая трудоёмкость на единицу продукции."
                    : FormattableString.Invariant($"Место {laborRank} по возрастанию трудоёмкости среди допущенных потоков."),
                provisional: !mayCompare || !ranks.LaborComplete, incompatible: labor.HasValue && !ranks.CompatibleLabor),
        ];
        return new(totals, scores, scores.Sum(score => score.Value ?? 0m), scores.Count(score => score.Value.HasValue));

        ScoreValue Criterion(string criterion, ExactRatio? value, string unit, AnalysisEvidence evidence, decimal? score,
            string explanation, bool provisional = false, bool unresolved = false, bool incompatible = false,
            EvaluationIssue? extra = null)
        {
            var status = !isApplicable ? "notApplicable" : unresolved ? "unresolved"
                : incompatible || provisional && (score.HasValue || value.HasValue) ? "provisional"
                : score is null ? "missing" : "complete";
            var issues = new List<EvaluationIssue>();
            var path = $"/metrics/{totals.Family.Id:D}/data/{criterion}";
            switch (status)
            {
                case "missing":
                    issues.Add(Issue("CRITERION_MISSING", path, "Недостаточно данных для балла.",
                        "Заполните исходные данные критерия.", totals.Family.Id));
                    break;
                case "unresolved":
                    issues.Add(Issue("CRITERION_UNRESOLVED", path, "Действующие правила не определяют балл для отрицательного значения.",
                        "Проверьте исходные данные или рассмотрите подтверждённое прямое основание.", totals.Family.Id));
                    break;
                case "provisional":
                    issues.Add(incompatible
                        ? Issue("LABOR_NOT_COMPARABLE", path, "Трудоёмкость задана в несопоставимых единицах или периодах.",
                            "Приведите показатели к одному периоду, единице и базе 1.", totals.Family.Id, "warning")
                        : Issue("COMPARISON_PROVISIONAL", path, "Состав сравнения или отдельные значения ещё неизвестны.",
                            "Уточните допуск и значения у сравниваемых потоков.", totals.Family.Id, "warning"));
                    break;
            }

            if (extra is not null && isApplicable)
            {
                issues.Add(extra);
            }

            var applicableScore = isApplicable ? score : null;
            var pathToValue = FormattableString.Invariant($"/families/{totals.Family.Id:D}/scores/{criterion}/value");
            return new(applicableScore, new(criterion, Wire(value, pathToValue, totals.Family.Id),
                unit, applicableScore is { } points ? Format(points) : null, status, evidence,
                issues, $"FCK_{criterion.ToUpperInvariant()}", explanation));
        }
    }

    private static ExactRatio? Labor(MetricsData metrics) => metrics.LaborMode switch
    {
        "manual" => ExactRatio.Divide(metrics.LaborHoursPerUnit, metrics.LaborBaseQuantity),
        "calculated" => ExactRatio.Divide(metrics.DirectLaborHours, metrics.OutputQuantity),
        _ => null,
    };

    private static int Rank(Dictionary<ExactRatio, int> ranks, ExactRatio? value) =>
        value is { } number ? ranks.GetValueOrDefault(number) : 0;

    private sealed record FamilyTotals(
        EvaluationFamily Family, decimal? Revenue, decimal? Cost, ExactRatio? Share, ExactRatio? Margin, decimal? MaxDays,
        string Admission, IReadOnlyList<EvaluationIssue> Issues);

    private sealed record ScoreValue(decimal? Value, CriterionResult Result);

    private sealed record ScoredFamily(FamilyTotals Totals, ScoreValue[] Scores, decimal Sum, int Count)
    {
        public bool Complete => Totals.Admission == "admitted" && Scores.All(score => score.Result.Status == "complete");
    }

    private sealed class ComparisonRanks(FamilyTotals[] admitted, bool unknownAdmission, AnalysisPeriod? laborPeriod)
    {
        public bool UnknownAdmission { get; } = unknownAdmission;
        public Dictionary<ExactRatio, int> Share { get; } = RankingRules.DenseRanks(admitted.Select(item => item.Share));
        public Dictionary<ExactRatio, int> Labor { get; } =
            RankingRules.DenseRanks(admitted.Select(item => EvaluationCalculator.Labor(item.Family.Metrics)), ascending: true);
        public ExactRatio? BestMargin { get; } = admitted.Select(item => item.Margin).Max();
        public bool SharesComplete { get; } = admitted.All(item => item.Share.HasValue);
        public bool MarginsComplete { get; } = admitted.All(item => item.Margin.HasValue);
        public bool LaborComplete { get; } = admitted.All(item => EvaluationCalculator.Labor(item.Family.Metrics).HasValue);
        public bool CompatibleLabor { get; } = admitted.Select(item => item.Family.Metrics).Where(metric => EvaluationCalculator.Labor(metric).HasValue)
            .All(metric => (metric.LaborMode == "calculated" || metric.LaborBaseQuantity == 1m) && HasText(metric.LaborUnitCode)
                && laborPeriod is not null && metric.LaborEvidence.Period == laborPeriod)
            && admitted.Select(item => item.Family.Metrics).Where(metric => EvaluationCalculator.Labor(metric).HasValue)
                .Select(metric => metric.LaborUnitCode).Distinct(StringComparer.Ordinal).Take(2).Count() <= 1;
    }
}
