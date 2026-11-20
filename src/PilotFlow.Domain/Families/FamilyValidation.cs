using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;

namespace PilotFlow.Domain.Families;

public static class FamilyValidation
{
    public static IReadOnlyList<AnalysisIssue> Validate(FamilyData data)
    {
        var issues = new List<AnalysisIssue>();
        CatalogValidation.Text(issues, data.Name, "/name", 300, required: true);
        CatalogValidation.Text(issues, data.StartBoundary, "/startBoundary", 500);
        CatalogValidation.Text(issues, data.EndBoundary, "/endBoundary", 500);
        CatalogValidation.Text(issues, data.CompositionReason, "/compositionReason", 2000);
        CatalogValidation.Evidence(issues, data.ConfirmationEvidence, "/confirmationEvidence");
        return issues.Concat(CatalogValidation.Validate(data.Route).Select(static issue =>
            issue with { Path = "/route" + issue.Path })).Take(1000).ToArray();
    }

    public static IReadOnlyList<AnalysisIssue> Validate(AssignmentsData data)
    {
        var issues = new List<AnalysisIssue>();
        CatalogValidation.Text(issues, data.Reason, "/reason", 2000);
        if (data.Assignments.Count > 500)
        {
            issues.Add(new("TOO_MANY_ASSIGNMENTS", "/assignments", "Превышено число назначений.",
                "Укажите не более 500 назначений за один запрос."));
            return issues;
        }

        var products = new HashSet<Guid>();
        for (var i = 0; i < data.Assignments.Count; i++)
        {
            var path = FormattableString.Invariant($"/assignments/{i}");
            if (data.Assignments[i] is not { } assignment)
            {
                issues.Add(new("INVALID_ASSIGNMENT", path, "Назначение должно быть объектом.", "Укажите данные назначения."));
            }
            else if (!products.Add(assignment.ProductId))
            {
                issues.Add(new("DUPLICATE_PRODUCT_ASSIGNMENT", path + "/productId",
                    "Продукт указан больше одного раза.", "Передайте только одно назначение на продукт."));
            }
        }

        return issues;
    }

    public static IReadOnlyList<AnalysisIssue> Confirm(
        FamilyData data, RouteSignature canonicalRoute, IReadOnlyList<ProductFamilyRoute> members)
    {
        var issues = new List<AnalysisIssue>();
        if (members.Count == 0)
        {
            issues.Add(new("FAMILY_EMPTY", "/confirmed", "В семействе нет продукции.",
                "Назначьте продукцию семейству перед подтверждением."));
        }

        CatalogValidation.Text(issues, data.StartBoundary, "/startBoundary", 500, required: true);
        CatalogValidation.Text(issues, data.EndBoundary, "/endBoundary", 500, required: true);
        if (!HasEvidence(data.ConfirmationEvidence))
        {
            issues.Add(new("CONFIRMATION_EVIDENCE_REQUIRED", "/confirmationEvidence",
                "Для подтверждения нужны источник и период либо дата оценки.", "Добавьте основание подтверждения."));
        }

        if (canonicalRoute.Count == 0)
        {
            issues.Add(new("FAMILY_ROUTE_REQUIRED", "/route/steps", "Маршрут семейства пуст.", "Укажите операции маршрута."));
        }
        else if (members.Any(member => !canonicalRoute.IsCompatibleWith(member.Signature, data.CompositionReason)))
        {
            issues.Add(new("ROUTE_INCOMPATIBLE", "/route/steps", "Маршрут семейства не соответствует маршрутам его продукции.",
                "Согласуйте маршруты или обоснуйте укрупнение подтверждённого участка."));
        }

        if (canonicalRoute.HasUnconfirmedSection || members.Any(member => member.Signature.HasUnconfirmedSection))
        {
            issues.Add(new("SECTION_COVERAGE_UNCONFIRMED", "/route/steps",
                "Для укрупнённого участка не подтверждено покрытие не менее 80%.",
                "Укажите покрытие, источник, дату или период и основание в справочнике операций."));
        }

        if (FamilyCycle.Difference(members) > FamilyCycle.RecommendationPercent && string.IsNullOrWhiteSpace(data.CompositionReason))
        {
            issues.Add(new("CYCLE_EXPLANATION_REQUIRED", "/compositionReason", "Различие циклов превышает 20%.",
                "Укажите основание объединения продукции в одно семейство."));
        }

        return issues;
    }

    internal static bool HasEvidence(AnalysisEvidence evidence) =>
        !string.IsNullOrWhiteSpace(evidence.Source) && (evidence.Period is not null || evidence.AssessedOn is not null);
}
