using System.Globalization;
using PilotFlow.Domain.Decisions;
using static PilotFlow.Application.Evaluation.EvaluationCalculator;

namespace PilotFlow.Application.Evaluation;

internal static class SelectionPolicy
{
    internal static DirectSelectionOptionsResult DirectOptions(
        EvaluationInputs input, IReadOnlyList<FamilyEvaluationResult> evaluations,
        decimal? revenue, decimal? remainder, ILookup<Guid?, EvaluationProduct> members)
    {
        var evaluationById = evaluations.ToDictionary(item => item.FamilyId);
        var products = input.Products.Where(product => !product.IsOther).ToArray();
        var realFamilies = products.Where(product => product.FamilyId.HasValue)
            .Select(product => product.FamilyId).Distinct().Count();
        var data = input.Version.Data;
        var complete = data.NomenclatureComplete && HasSource(data.CompletenessEvidence)
            && data.TotalRevenue is > 0 && revenue.HasValue && remainder == 0m;
        var allSorted = input.Products.Where(product => product.Revenue.HasValue)
            .OrderByDescending(product => product.Revenue).ToArray();
        var options = new List<DirectSelectionOption>(input.Families.Count * 2);
        foreach (var family in input.Families)
        {
            var familyMembers = members[family.Id].Where(product => !product.IsOther).ToArray();
            var candidate = familyMembers.Length == 1 ? familyMembers[0] : null;
            var admission = evaluationById[family.Id].Admission;
            options.Add(Option("monoproduct", null, KnownMonoFailure,
                familyMembers.Length > 0 && familyMembers.Length == products.Length));
            options.Add(Option("dominantProduct", candidate?.Id, KnownDominanceFailure, candidate is not null));

            DirectSelectionOption Option(string mode, Guid? productId, Func<EvaluationIssue?> knownFailure, bool hasTarget)
            {
                var failure = admission == "excluded"
                    ? Issue("DIRECT_FAMILY_EXCLUDED", "/selection/familyId", "Семейство не прошло допуск.",
                        "Выберите допущенное семейство.", family.Id) : knownFailure();
                if (failure is not null)
                {
                    return new(family.Id, mode, productId, "ineligible", [failure]);
                }

                if (!complete || !family.Confirmed || admission != "admitted" || !hasTarget)
                {
                    return new(family.Id, mode, productId, "unknown",
                        [Issue("DIRECT_BASIS_UNPROVEN", "/data/nomenclatureComplete", "Прямое основание пока не подтверждено.",
                            "Подтвердите полный перечень и отсутствие остатка, состав и допуск семейства.", family.Id)]);
                }

                return new(family.Id, mode, productId, "eligible", []);
            }

            EvaluationIssue? KnownMonoFailure() => realFamilies > 1
                ? Issue("NOT_MONOPRODUCT", "/selection/mode", "Данные показывают несколько реальных семейств.",
                    "Выберите рейтинг или проверьте основание прямого выбора.", family.Id)
                : null;

            EvaluationIssue? KnownDominanceFailure()
            {
                if (familyMembers.Length > 1)
                {
                    return Issue("DOMINANT_FAMILY_HAS_MULTIPLE_PRODUCTS", "/selection/familyId",
                        "Для доминирования семейство должно состоять из одного продукта.",
                        "Укажите семейство только доминирующего продукта.", family.Id);
                }

                if (candidate?.Revenue is not { } amount)
                {
                    return null;
                }

                // Finding any higher competitor disproves dominance. Only the largest other revenue needs comparing.
                var competitor = allSorted.FirstOrDefault(product => product.Id != candidate.Id);
                if (data.TotalRevenue is > 0 && amount * 100m <= data.TotalRevenue.Value * 20m
                    || competitor?.Revenue is { } competing && amount < 2m * competing)
                {
                    return Issue("PRODUCT_NOT_DOMINANT", "/selection/dominantProductId",
                        "Доля продукта не выше 20% или выручка менее чем вдвое больше выручки другой позиции.",
                        "Проверьте финансовые данные или выберите другой режим.", candidate.Id);
                }

                return null;
            }
        }

        return new(input.Version.Revision, options);
    }

    internal static IReadOnlyList<EvaluationIssue> Issues(EvaluationResult evaluation, DirectSelectionOptionsResult direct,
        SelectionData selection, bool forSave)
    {
        var family = evaluation.Families.SingleOrDefault(item => item.FamilyId == selection.FamilyId);
        if (family is null || family.Admission == "excluded")
        {
            return [Issue("FAMILY_NOT_ADMITTED", "/selection/familyId",
                "Семейство не найдено или не прошло допуск.", "Выберите допущенное семейство.", selection.FamilyId)];
        }

        if (selection.Mode == "ranked")
        {
            if (family.Rank > 2 || family.Rank == 2 && decimal.Parse(family.KnownScoreSum, CultureInfo.InvariantCulture) < 7m)
            {
                return [Issue("RANKED_SELECTION_FORBIDDEN", "/selection/familyId",
                    "Рейтинг допускает первое либо второе место с результатом от 7 баллов.",
                    "Выберите допустимое место.", selection.FamilyId)];
            }

            if (forSave)
            {
                return [];
            }

            if (!evaluation.CanFinalizeRanked || !family.CanSelectRanked)
            {
                return [Issue("RANKED_SELECTION_INCOMPLETE", "/selection/familyId", "Рейтинг или допуск ещё не готовы для выбора.",
                    "Завершите оценку и подтвердите выбранное семейство.", selection.FamilyId)];
            }

            var needsReason = family.Rank == 2 || family.Rank == 1 && evaluation.LeaderFamilyIds.Count > 1;
            return needsReason && !HasText(selection.Justification)
                ? [Issue("SELECTION_JUSTIFICATION_REQUIRED", "/selection/justification",
                    "Равные лидеры или второе место требуют обоснования выбора.",
                    "Укажите обстоятельство выбора вне шести критериев.", selection.FamilyId)]
                : [];
        }

        var option = direct.Options.SingleOrDefault(item => item.FamilyId == selection.FamilyId && item.Mode == selection.Mode);
        if (option is null || selection.Mode == "dominantProduct" && option.DominantProductId != selection.DominantProductId)
        {
            return [Issue("INVALID_DIRECT_TARGET", "/selection/dominantProductId",
                "Указанный продукт не является единственной позицией выбранного семейства.",
                "Укажите идентификатор единственного продукта.", selection.DominantProductId)];
        }

        if (option.Status == "ineligible" || !forSave && option.Status != "eligible")
        {
            return option.Issues;
        }

        return !forSave && !HasSource(selection.DirectBasisEvidence)
            ? [Issue("DIRECT_EVIDENCE_REQUIRED", "/selection/directBasisEvidence",
                "Не указан источник и период или дата оценки прямого основания.", "Подтвердите основание выбора.")]
            : [];
    }
}
