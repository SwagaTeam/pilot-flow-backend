using PilotFlow.Domain.Analyses;
using static PilotFlow.Application.Evaluation.EvaluationCalculator;

namespace PilotFlow.Application.Evaluation;

internal static class VersionReadiness
{
    internal static VersionValidationResult Validate(EvaluationInputs input, EvaluationResult evaluation,
        FamilyMatrixResult matrix, DirectSelectionOptionsResult options)
    {
        var data = input.Version.Data;
        var issues = new List<EvaluationIssue>(evaluation.Issues);
        var familiesById = input.Families.ToDictionary(family => family.Id);
        var members = input.Products.Where(product => !product.IsOther).ToLookup(product => product.FamilyId);

        Required(!HasText(data.EnterpriseName), "/data/enterpriseName", "наименование предприятия");
        Required(data.ProjectStartDate is null, "/data/projectStartDate", "дату начала проекта");
        Required(data.FinancialPeriod is null, "/data/financialPeriod", "финансовый период");
        Required(data.LaborPeriod is null, "/data/laborPeriod", "период данных о труде");
        Required(data.ActivitiesPeriod is null, "/data/activitiesPeriod", "период производственного плана");
        Required(data.ForecastPeriod is null, "/data/forecastPeriod", "прогнозный период");
        Required(!HasText(data.AccountingBasis), "/data/accountingBasis", "основание учёта");
        Period(data.FinancialPeriod, data.ProjectStartDate, 12, "/data/financialPeriod");
        Period(data.LaborPeriod, data.ProjectStartDate, 6, "/data/laborPeriod");
        Evidence(data.RevenueEvidence, data.FinancialPeriod, "/data/revenueEvidence");

        if (input.Families.Count == 0)
        {
            Add("FAMILIES_REQUIRED", "/families", "Не сформированы продуктовые семейства.", "Сформируйте семейства и их маршруты.");
        }

        foreach (var product in input.Products)
        {
            if (product.Revenue.HasValue || product.RawMaterialCost.HasValue)
            {
                Evidence(product.FinancialEvidence, data.FinancialPeriod, "/products/financialEvidence", product.Id);
            }

            if (product.IsOther)
            {
                continue;
            }

            if (product.FamilyId is not { } id || !familiesById.ContainsKey(id))
            {
                Add("PRODUCT_NOT_ASSIGNED", "/products", "Для продукта не определено семейство.",
                    "Назначьте продукт в подтверждённое семейство.", product.Id);
            }

            Required(product.RouteDurations.Count == 0, "/products/route", "маршрут продукта", product.Id);
            if (product.CalendarDurationDays.HasValue)
            {
                Evidence(product.DurationEvidence, null, "/products/durationEvidence", product.Id);
            }

            if (product.InProductionPlan.HasValue)
            {
                Evidence(product.PlanEvidence, data.ActivitiesPeriod, "/products/planEvidence", product.Id);
            }
        }

        foreach (var cycle in matrix.Cycles)
        {
            var family = familiesById[cycle.FamilyId];
            if (!family.Confirmed || !family.HasRoute || !members[family.Id].Any()
                || !HasText(family.StartBoundary) || !HasText(family.EndBoundary))
            {
                Add("FAMILY_NOT_CONFIRMED", "/families", "Семейство, его состав, границы и маршрут должны быть подтверждены.",
                    "Заполните границы и подтвердите непустое семейство с маршрутом.", family.Id);
            }
            else
            {
                Evidence(family.ConfirmationEvidence, null, "/families/confirmationEvidence", family.Id);
            }

            Required(cycle.Status == "exceedsRecommendation" && !HasText(family.CompositionReason),
                "/families/compositionReason", "обоснование состава при разнице времени циклов выше 20%", family.Id);
        }

        var protocol = input.Protocol;
        Required(!HasText(protocol.ProtocolNumber), "/protocol/protocolNumber", "номер протокола");
        Required(protocol.ProtocolDate is null, "/protocol/protocolDate", "дату протокола");
        Required(!HasText(protocol.AgreementNumber), "/protocol/agreementNumber", "номер соглашения");
        Required(protocol.AgreementDate is null, "/protocol/agreementDate", "дату соглашения");
        Required(!HasText(protocol.EnterpriseParty), "/protocol/enterpriseParty", "наименование предприятия в соглашении");
        Required(!HasText(protocol.PartnerParty), "/protocol/partnerParty", "наименование партнёра");
        Required(!HasText(protocol.SigningPlace), "/protocol/signingPlace", "место подписания");
        Required(!HasText(protocol.PreparedBy.FullName), "/protocol/preparedBy/fullName", "ФИО составителя");
        Required(!HasText(protocol.PreparedBy.Position), "/protocol/preparedBy/position", "должность составителя");
        Required(!HasText(protocol.ApprovedBy.FullName), "/protocol/approvedBy/fullName", "ФИО утверждающего");
        Required(!HasText(protocol.ApprovedBy.Position), "/protocol/approvedBy/position", "должность утверждающего");
        Required(!HasText(protocol.Conclusions), "/protocol/conclusions", "выводы протокола");

        var selection = input.Selection;
        if (selection is null)
        {
            Add("SELECTION_REQUIRED", "/selection", "Пилотный поток не выбран.", "Сохраните проект выбора.");
        }
        else
        {
            issues.AddRange(SelectionPolicy.Issues(evaluation, options, selection, forSave: false));
            if (selection.Mode == "ranked")
            {
                issues.AddRange(evaluation.Families.Where(family => family.Admission == "unknown")
                    .SelectMany(family => family.Issues).Where(IsError));
                issues.AddRange(evaluation.Families.Where(family => family.Admission == "admitted")
                    .SelectMany(family => family.Scores).SelectMany(score => score.Issues).Where(IsError));
            }

            var affected = selection.Mode == "ranked"
                ? evaluation.Families.Where(family => family.Admission == "admitted").ToArray()
                : evaluation.Families.Where(family => family.FamilyId == selection.FamilyId).ToArray();
            foreach (var computed in affected)
            {
                var metrics = familiesById[computed.FamilyId].Metrics;
                Required(computed.MarginIncome is null, "/families/marginIncome", "маржинальный доход", computed.FamilyId);
                Required(metrics.SalesGrowthPercent is null, "/metrics/salesGrowthPercent", "прогноз роста продаж", computed.FamilyId);
                if (metrics.SalesGrowthPercent.HasValue)
                {
                    Evidence(metrics.SalesGrowthEvidence, data.ForecastPeriod, "/metrics/salesGrowthEvidence", computed.FamilyId);
                }

                if (selection.Mode == "ranked")
                {
                    Evidence(metrics.LoadEvidence, null, "/metrics/loadEvidence", computed.FamilyId);
                    Evidence(metrics.ProductionTypeEvidence, null, "/metrics/productionTypeEvidence", computed.FamilyId);
                    Evidence(metrics.LaborEvidence, data.LaborPeriod, "/metrics/laborEvidence", computed.FamilyId);
                }
            }
        }

        var canExport = !issues.Any(IsError);
        Required(selection is not null && !HasText(selection.ManagerDecisionReference),
            "/selection/managerDecisionReference", "ссылку на решение руководителя");
        if (input.Version.State != "draft")
        {
            issues.Add(Issue("VERSION_FIXED", "/state", "Версия уже зафиксирована.", "Откройте сохранённое решение.", severity: "info"));
        }

        var canFinalize = input.Version.State == "draft" && !issues.Any(IsError);
        issues.AddRange(matrix.Issues);
        issues.AddRange(evaluation.Families.SelectMany(family => family.Scores)
            .SelectMany(score => score.Issues).Where(issue => !IsError(issue)));
        return new(input.Version.Revision, canFinalize, canExport, issues.Take(1000).ToArray());

        void Add(string code, string path, string message, string suggestion, Guid? id = null) =>
            issues.Add(Issue(code, path, message, suggestion, id));

        void Required(bool missing, string path, string field, Guid? id = null)
        {
            if (missing)
            {
                Add("FIELD_REQUIRED", path, "Обязательное поле не заполнено.", $"Укажите {field}.", id);
            }
        }

        void Evidence(AnalysisEvidence evidence, AnalysisPeriod? period, string path, Guid? id = null)
        {
            if (!HasSource(evidence))
            {
                Add("EVIDENCE_REQUIRED", path, "Источник и период или дата оценки не указаны.",
                    "Укажите основание показателя и период или дату оценки.", id);
            }
            else if (period is not null && evidence.Period != period)
            {
                Add("EVIDENCE_PERIOD_MISMATCH", path + "/period", "Период источника отличается от периода анализа.",
                    "Используйте данные за соответствующий период анализа.", id);
            }
        }

        void Period(AnalysisPeriod? period, DateOnly? projectStart, int months, string path)
        {
            if (period is not { } actual || projectStart is not { } start)
            {
                return;
            }

            var firstOfMonth = new DateOnly(start.Year, start.Month, 1);
            if (start.Year == 1 && start.Month <= months
                || actual.Start != firstOfMonth.AddMonths(-months) || actual.End != firstOfMonth.AddDays(-1))
            {
                Add("PERIOD_DOES_NOT_MATCH_PROJECT", path, "Период не соответствует полным месяцам перед началом проекта.",
                    FormattableString.Invariant($"Укажите последние {months} полных календарных месяцев."));
            }
        }
    }
}
