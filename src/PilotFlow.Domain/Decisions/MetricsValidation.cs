using System.Numerics;
using PilotFlow.Domain.Analyses;
using static PilotFlow.Domain.Decisions.DecisionInputValidation;

namespace PilotFlow.Domain.Decisions;

public static class MetricsValidation
{
    private const decimal NumericExclusiveLimit = 1_000_000_000_000_000_000m;
    private const decimal StorageScale = 1_000_000m;

    public static (MetricsData Data, IReadOnlyList<AnalysisIssue> Issues) Prepare(MetricsData data)
    {
        var issues = new List<AnalysisIssue>();
        Evidence(issues, data.SalesGrowthEvidence, "/salesGrowthEvidence");
        Evidence(issues, data.LoadEvidence, "/loadEvidence");
        Evidence(issues, data.ProductionTypeEvidence, "/productionTypeEvidence");
        Evidence(issues, data.LaborEvidence, "/laborEvidence");
        Text(issues, data.LoadUnit, 50, "/loadUnit");
        Text(issues, data.LaborUnitCode, 30, "/laborUnitCode");
        Text(issues, data.ComparabilityExplanation, 2000, "/comparabilityExplanation");

        if ((data.LoadActual is null) != (data.LoadCapacity is null))
        {
            issues.Add(new("LOAD_PAIR_REQUIRED", "/loadActual", "Загрузка требует фактического объёма и мощности.",
                "Заполните оба значения или оставьте оба пустыми."));
        }

        var effectiveLoad = data.LoadPercent;
        if (data.LoadActual is { } actual && data.LoadCapacity is > 0 and { } capacity)
        {
            var denominator = Units(capacity);
            var numerator = Units(actual) * (int)(100 * StorageScale);
            var rounded = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (remainder * 2 >= denominator)
            {
                rounded++;
            }

            if (rounded >= Units(NumericExclusiveLimit))
            {
                issues.Add(new("NUMERIC_RANGE", "/loadPercent", "Расчётный процент загрузки превышает допустимый диапазон.",
                    "Проверьте фактический объём и мощность."));
            }
            else if (effectiveLoad is { } supplied && BigInteger.Abs(Units(supplied) * denominator - numerator) > denominator)
            {
                issues.Add(new("LOAD_PERCENT_MISMATCH", "/loadPercent", "Процент не совпадает с расчётной загрузкой.",
                    "Передайте расчётный процент или оставьте поле пустым."));
            }
            else
            {
                effectiveLoad = (decimal)rounded / StorageScale;
            }

            if (string.IsNullOrWhiteSpace(data.LoadUnit))
            {
                issues.Add(new("LOAD_UNIT_REQUIRED", "/loadUnit", "Для фактической загрузки нужна единица измерения.",
                    "Укажите общую единицу объёма и мощности."));
            }
        }

        var aboveCapacity = data.LoadActual is { } actualLoad && data.LoadCapacity is > 0 and { } ratedCapacity
            ? actualLoad > ratedCapacity
            : effectiveLoad > 100;
        if (aboveCapacity && string.IsNullOrWhiteSpace(data.LoadEvidence.Basis))
        {
            issues.Add(new("LOAD_BASIS_REQUIRED", "/loadEvidence/basis", "Загрузка выше 100% требует обоснования.",
                "Поясните, почему загрузка превышает мощность."));
        }

        if (data.ProductionType is not (null or "largeSeries" or "mediumSeries" or "smallSeries" or "project"))
        {
            issues.Add(new("INVALID_PRODUCTION_TYPE", "/productionType", "Неизвестный тип производства.", "Укажите тип из справочника."));
        }

        switch (data.LaborMode)
        {
            case "manual" when data.DirectLaborHours is not null || data.OutputQuantity is not null:
                issues.Add(new("INVALID_LABOR_INPUT", "/directLaborHours", "В ручном режиме не используются часы и объём выпуска.",
                    "Оставьте показатели расчётного режима пустыми."));
                break;
            case "calculated" when data.LaborHoursPerUnit is not null || data.LaborBaseQuantity is not null:
                issues.Add(new("INVALID_LABOR_INPUT", "/laborHoursPerUnit", "В расчётном режиме не используются трудоёмкость и базовое количество.",
                    "Оставьте показатели ручного режима пустыми."));
                break;
            case not ("manual" or "calculated"):
                issues.Add(new("INVALID_LABOR_MODE", "/laborMode", "Неизвестный способ расчёта трудоёмкости.",
                    "Выберите ручной или расчётный режим."));
                break;
        }

        Positive(data.LoadCapacity, "/loadCapacity");
        Positive(data.OutputQuantity, "/outputQuantity");
        Positive(data.LaborBaseQuantity, "/laborBaseQuantity");
        return (data with { LoadPercent = effectiveLoad }, issues);

        void Positive(decimal? value, string path)
        {
            if (value <= 0)
            {
                issues.Add(new("POSITIVE_VALUE_REQUIRED", path, "Значение должно быть больше нуля.", "Укажите положительное значение."));
            }
        }
    }

    private static BigInteger Units(decimal value) => new(value * StorageScale);
}
