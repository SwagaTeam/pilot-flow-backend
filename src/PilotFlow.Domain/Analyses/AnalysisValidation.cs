namespace PilotFlow.Domain.Analyses;

public sealed record AnalysisIssue(string Code, string Path, string Message, string Suggestion);

public static class AnalysisValidation
{
    public static IReadOnlyList<AnalysisIssue> Validate(AnalysisData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var issues = new List<AnalysisIssue>();

        if (string.IsNullOrWhiteSpace(data.Title))
        {
            Add("TITLE_REQUIRED", "/title", "Введите название анализа.", "Укажите непустое название.");
        }

        Text(data.Title, 300, "/title");
        Text(data.EnterpriseName, 300, "/enterpriseName");
        Text(data.Region, 200, "/region");
        Text(data.Activity, 500, "/activity");
        Text(data.AccountingBasis, 2000, "/accountingBasis");
        Text(data.RemainderExplanation, 2000, "/remainderExplanation");
        Text(data.FamilyConclusions, 10000, "/familyConclusions");
        Text(data.RankingConclusions, 10000, "/rankingConclusions");
        Period(data.FinancialPeriod, "/financialPeriod");
        Period(data.ActivitiesPeriod, "/activitiesPeriod");
        Period(data.ForecastPeriod, "/forecastPeriod");
        Period(data.LaborPeriod, "/laborPeriod");
        Evidence(data.RevenueEvidence, "/revenueEvidence");
        Evidence(data.CompletenessEvidence, "/completenessEvidence");

        if (data.Currency != "RUB")
        {
            Add("INVALID_CURRENCY", "/currency", "Допустимая валюта — RUB.", "Укажите RUB.");
        }

        if (data.TotalRevenue is < 0 or >= 1_000_000_000_000_000_000m
            || (data.TotalRevenue is { } revenue && decimal.Round(revenue, 6) != revenue))
        {
            Add("INVALID_REVENUE", "/totalRevenue", "Выручка выходит за допустимый диапазон.", "Укажите неотрицательную сумму с точностью до 6 знаков.");
        }

        if (data.EnterpriseInn is { } inn && (inn.Length is not (10 or 12) || inn.Any(digit => !char.IsAsciiDigit(digit))))
        {
            Add("INVALID_INN", "/enterpriseInn", "ИНН должен содержать 10 или 12 цифр.", "Исправьте ИНН или укажите null.");
        }

        if (data.Participants.Count > 100)
        {
            Add("TOO_MANY_PARTICIPANTS", "/participants", "Допускается до 100 участников.", "Сократите список участников.");
        }
        else
        {
            for (var i = 0; i < data.Participants.Count; i++)
            {
                var path = FormattableString.Invariant($"/participants/{i}");
                if (data.Participants[i] is not { } participant)
                {
                    Add("INVALID_PARTICIPANT", path, "Участник должен быть объектом.", "Укажите данные участника.");
                    continue;
                }

                Text(participant.FullName, 300, path + "/fullName");
                Text(participant.Position, 300, path + "/position");
                if (participant.Role is not ("operator" or "protocolPreparer" or "decisionMaker" or "specialist"))
                {
                    Add("INVALID_PARTICIPANT_ROLE", path + "/role", "Неизвестная роль участника.", "Выберите роль из контракта.");
                }
            }
        }

        return issues;

        void Add(string code, string path, string message, string suggestion) =>
            issues.Add(new AnalysisIssue(code, path, message, suggestion));

        void Text(string? value, int maxLength, string path)
        {
            if (value is not null && value.EnumerateRunes().Count() > maxLength)
            {
                Add("FIELD_TOO_LONG", path, "Слишком длинное значение.", FormattableString.Invariant($"Сократите его до {maxLength} символов."));
            }
            else if (value?.Contains('\0', StringComparison.Ordinal) == true)
            {
                Add("INVALID_TEXT", path, "Значение содержит нулевой символ.", "Удалите нулевой символ.");
            }
        }

        void Period(AnalysisPeriod? period, string path)
        {
            if (period is not null && period.Start > period.End)
            {
                Add("INVALID_PERIOD", path, "Начало периода позже его окончания.", "Исправьте даты периода.");
            }
        }

        void Evidence(AnalysisEvidence evidence, string path)
        {
            Text(evidence.Source, 2000, path + "/source");
            Text(evidence.Basis, 2000, path + "/basis");
            Text(evidence.MissingReason, 2000, path + "/missingReason");
            Period(evidence.Period, path + "/period");
        }
    }
}
