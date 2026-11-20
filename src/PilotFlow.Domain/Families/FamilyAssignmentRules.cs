using PilotFlow.Domain.Analyses;

namespace PilotFlow.Domain.Families;

public static class FamilyAssignmentRules
{
    public static bool ExactMatch(
        RouteSignature product, RouteSignature canonical, IReadOnlyList<RouteSignature> incumbentRoutes) =>
        canonical.Count > 0 ? canonical.Matches(product)
            : incumbentRoutes.Count > 0 && incumbentRoutes.All(product.Matches);

    public static AnalysisIssue? CanAssign(
        bool isOther, RouteSignature product, RouteSignature canonical, IReadOnlyList<RouteSignature> incumbentRoutes,
        string? compositionReason, string path)
    {
        if (isOther)
        {
            return new("OTHER_PRODUCT_NOT_ASSIGNABLE", path, "Строка «Прочее» не входит в семейство.",
                "Укажите отдельную позицию номенклатуры.");
        }

        if (incumbentRoutes.Count >= 500)
        {
            return new("FAMILY_CAPACITY_EXCEEDED", path, "В семействе уже 500 продуктов.",
                "Выберите другое семейство или создайте новое.");
        }

        var compatible = canonical.Count == 0
            ? incumbentRoutes.All(product.Matches)
            : canonical.IsCompatibleWith(product, compositionReason);
        return compatible ? null : new("ROUTE_INCOMPATIBLE", path,
            "Маршрут продукта не совпадает с маршрутом семейства.",
            "Согласуйте операции и рабочие места, обоснуйте укрупнение или выберите другое семейство.");
    }
}
