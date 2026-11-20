using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Evaluation;

namespace PilotFlow.Infrastructure.Persistence.Decisions;

internal static class SnapshotOrdering
{
    internal static ProductDocument Product(ProductDocument product) => product with
    {
        Issues = product.Issues.OrderBy(issue => issue.Severity, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.EntityId).ToArray(),
    };

    internal static EvaluationResult Evaluation(EvaluationResult evaluation) => evaluation with
    {
        Issues = SortIssues(evaluation.Issues),
        Families = evaluation.Families.OrderBy(family => family.FamilyId)
            .Select(family => family with
            {
                Issues = SortIssues(family.Issues),
                Scores = family.Scores.Select(score => score with { Issues = SortIssues(score.Issues) }).ToArray(),
            }).ToArray(),
        LeaderFamilyIds = evaluation.LeaderFamilyIds.Order().ToArray(),
    };

    internal static FamilyMatrixResult Matrix(FamilyMatrixResult matrix) => matrix with
    {
        Issues = SortIssues(matrix.Issues),
        Products = matrix.Products.OrderBy(product => product.ProductId).ToArray(),
        Cycles = matrix.Cycles.OrderBy(cycle => cycle.FamilyId)
            .Select(cycle => cycle with { Issues = SortIssues(cycle.Issues) }).ToArray(),
    };

    private static EvaluationIssue[] SortIssues(IEnumerable<EvaluationIssue> issues) =>
        issues.OrderBy(issue => issue.Severity, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.EntityId).ToArray();
}
