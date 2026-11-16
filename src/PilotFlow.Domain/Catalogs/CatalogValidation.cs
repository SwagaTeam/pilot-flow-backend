using PilotFlow.Domain.Analyses;

namespace PilotFlow.Domain.Catalogs;

public static class CatalogValidation
{
    public static IReadOnlyList<AnalysisIssue> Validate(ProductData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var issues = new List<AnalysisIssue>();
        Text(issues, data.Name, "/name", 300, required: true);
        Text(issues, data.UnitCode, "/unitCode", 30, required: true);
        Text(issues, data.ProductCode, "/productCode", 100);
        Text(issues, data.DurationStart, "/durationStart", 300);
        Text(issues, data.DurationEnd, "/durationEnd", 300);
        Decimal(issues, data.SalesVolume, "/salesVolume");
        Decimal(issues, data.Revenue, "/revenue");
        Decimal(issues, data.RawMaterialCost, "/rawMaterialCost");
        Decimal(issues, data.CalendarDurationDays, "/calendarDurationDays");
        Evidence(issues, data.FinancialEvidence, "/financialEvidence");
        Evidence(issues, data.DurationEvidence, "/durationEvidence");
        Evidence(issues, data.PlanEvidence, "/planEvidence");
        return issues;
    }

    public static IReadOnlyList<AnalysisIssue> Validate(OperationData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var issues = new List<AnalysisIssue>();
        Text(issues, data.Name, "/name", 300, required: true);
        Text(issues, data.WorkplaceName, "/workplaceName", 300);
        if (data.Kind is not ("operation" or "equipment" or "workplace" or "section"))
        {
            issues.Add(new("INVALID_OPERATION_KIND", "/kind", "Неизвестный тип операции.", "Выберите тип из справочника."));
        }

        Decimal(issues, data.AggregationCoveragePercent, "/aggregationCoveragePercent");
        if (data.AggregationCoveragePercent > 100)
        {
            issues.Add(new("INVALID_COVERAGE", "/aggregationCoveragePercent",
                "Покрытие не может превышать 100%.", "Укажите процент в диапазоне от 0 до 100."));
        }

        Evidence(issues, data.AggregationEvidence, "/aggregationEvidence");
        return issues;
    }

    public static IReadOnlyList<AnalysisIssue> Validate(RouteData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Steps.Count > 500)
        {
            return [new("ROUTE_TOO_LONG", "/steps", "В маршруте больше 500 вхождений.", "Сократите маршрут.")];
        }

        var issues = new List<AnalysisIssue>();
        var ids = new HashSet<Guid>();
        var positions = new HashSet<int>();
        for (var i = 0; i < data.Steps.Count; i++)
        {
            var path = FormattableString.Invariant($"/steps/{i}");
            if (data.Steps[i] is not { } step)
            {
                issues.Add(new("INVALID_STEP", path, "Вхождение должно быть объектом.", "Укажите данные вхождения."));
                continue;
            }

            if (!ids.Add(step.OccurrenceId))
            {
                issues.Add(new("DUPLICATE_OCCURRENCE", path + "/occurrenceId",
                    "Идентификатор вхождения повторяется.", "Используйте отдельный UUID для каждого вхождения."));
            }

            if (step.Position < 1 || step.Position > data.Steps.Count || !positions.Add(step.Position))
            {
                issues.Add(new("INVALID_ROUTE_POSITION", path + "/position",
                    "Позиции должны образовывать последовательность от 1 до числа вхождений.", "Перенумеруйте вхождения без пропусков."));
            }

            Text(issues, step.WorkplaceName, path + "/workplaceName", 300);
            Decimal(issues, step.DurationHours, path + "/durationHours");
            Evidence(issues, step.Evidence, path + "/evidence");
        }

        // The public Problem schema limits the number of issues.
        return issues.Count <= 1000 ? issues : issues.Take(1000).ToArray();
    }

    private static void Text(List<AnalysisIssue> issues, string? value, string path, int maxLength, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new("FIELD_REQUIRED", path, "Поле не может быть пустым.", "Введите значение."));
        }
        else if (value is not null && (value.EnumerateRunes().Count() > maxLength || value.Contains('\0', StringComparison.Ordinal)))
        {
            issues.Add(new("INVALID_TEXT", path, "Недопустимое текстовое значение.",
                FormattableString.Invariant($"Используйте до {maxLength} символов без нулевых символов.")));
        }
    }

    private static void Decimal(List<AnalysisIssue> issues, decimal? value, string path)
    {
        if (value is { } number && (number < 0 || number >= 1_000_000_000_000_000_000m || decimal.Round(number, 6) != number))
        {
            issues.Add(new("INVALID_DECIMAL", path, "Число выходит за допустимый диапазон.",
                "Укажите неотрицательное число с точностью до шести дробных знаков."));
        }
    }

    private static void Evidence(List<AnalysisIssue> issues, AnalysisEvidence evidence, string path)
    {
        Text(issues, evidence.Source, path + "/source", 2000);
        Text(issues, evidence.Basis, path + "/basis", 2000);
        Text(issues, evidence.MissingReason, path + "/missingReason", 2000);
        if (evidence.Period is { } period && period.Start > period.End)
        {
            issues.Add(new("INVALID_PERIOD", path + "/period", "Начало периода позже его окончания.", "Исправьте даты периода."));
        }
    }
}
