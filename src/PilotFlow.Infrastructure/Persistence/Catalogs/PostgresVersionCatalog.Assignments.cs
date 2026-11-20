using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Families;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Families;
using PilotFlow.Infrastructure.Persistence.Families;

namespace PilotFlow.Infrastructure.Persistence.Catalogs;

internal sealed partial class PostgresVersionCatalog
{
    public Task<Versioned<ProductAssignmentOptions>?> GetAssignmentOptionsAsync(
        VersionScope scope, Guid productId, CancellationToken cancellationToken) =>
        transactions.ReadAsync<ProductAssignmentOptions>(scope, async token =>
        {
            var versionId = scope.VersionId;
            var product = await db.Set<ProductRow>().AsNoTracking().SingleOrDefaultAsync(
                row => row.VersionId == versionId && row.Id == productId, token);
            if (product is null)
            {
                return null;
            }

            var families = await Families(versionId).AsNoTracking().OrderBy(row => row.Name).ThenBy(row => row.Id)
                .ToArrayAsync(token);
            var (targets, routes) = await LoadAssignmentRoutesAsync(versionId, families, [productId], token);
            var productRoute = routes[productId].Signature;
            var options = families.Select(row =>
            {
                var target = targets[row.Id];
                var otherMembers = target.Members.Where(id => id != productId).Select(id => routes[id].Signature).ToArray();
                var issue = FamilyAssignmentRules.CanAssign(product.IsOther, productRoute, target.Route,
                    otherMembers, target.Family.Data.CompositionReason, "/familyId");
                IReadOnlyList<FamilyNotice> issues = issue is null ? []
                    : [new(issue.Code, "error", issue.Path, issue.Message, issue.Suggestion, row.Id)];
                return new AssignmentOption(row.Id, issue is null,
                    FamilyAssignmentRules.ExactMatch(productRoute, target.Route, otherMembers), issues);
            }).ToArray();
            return new ProductAssignmentOptions(productId, options, !product.IsOther);
        }, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<AnalysisVersionDetails>>> AssignProductsAsync(
        CatalogChange change, ValidatedInput<AssignmentsData> input, CancellationToken cancellationToken) =>
        transactions.WriteAsync<AnalysisVersionDetails>(change, async token =>
        {
            if (input.Issues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: input.Issues);
            }

            var versionId = change.Scope.VersionId;
            var assignments = input.Data.Assignments;
            var productIds = assignments.Select(row => row.ProductId).ToArray();
            var familyIds = assignments.Where(row => row.FamilyId.HasValue).Select(row => row.FamilyId!.Value)
                .Distinct().ToArray();
            var products = await db.Set<ProductRow>().AsNoTracking()
                .Where(row => row.VersionId == versionId && productIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.IsOther, token);
            var families = await Families(versionId).AsNoTracking().Where(row => familyIds.Contains(row.Id))
                .ToArrayAsync(token);
            var (targets, routes) = await LoadAssignmentRoutesAsync(versionId, families, products.Keys.ToArray(), token);
            var existing = await FamilyMembers(versionId).AsNoTracking().Where(row => productIds.Contains(row.ProductId))
                .ToDictionaryAsync(row => row.ProductId, row => row.FamilyId, token);

            var issues = CheckAssignmentReferences(assignments, products, targets);
            if (issues.Count != 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: issues);
            }

            var affected = new HashSet<Guid>();
            var moving = assignments.Select((assignment, index) => (assignment, index))
                .Where(item => (existing.TryGetValue(item.assignment.ProductId, out var familyId) ? familyId : (Guid?)null)
                    != item.assignment.FamilyId).ToArray();
            foreach (var (assignment, _) in moving)
            {
                if (existing.TryGetValue(assignment.ProductId, out var source))
                {
                    affected.Add(source);
                    if (targets.TryGetValue(source, out var oldTarget))
                    {
                        oldTarget.Members.Remove(assignment.ProductId);
                    }
                }
            }

            foreach (var (assignment, index) in moving)
            {
                if (assignment.FamilyId is not { } familyId)
                {
                    continue;
                }

                var target = targets[familyId];
                var memberRoutes = target.Members.Select(id => routes[id].Signature).ToArray();
                var issue = FamilyAssignmentRules.CanAssign(products[assignment.ProductId], routes[assignment.ProductId].Signature,
                    target.Route, memberRoutes, target.Family.Data.CompositionReason,
                    FormattableString.Invariant($"/assignments/{index}/familyId"));
                if (issue is not null)
                {
                    issues.Add(issue);
                    continue;
                }

                affected.Add(familyId);
                target.Members.Add(assignment.ProductId);
            }

            if (issues.Count != 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: issues);
            }

            if (moving.Length != 0)
            {
                var movedProducts = moving.Select(item => item.assignment.ProductId).ToArray();
                await FamilyMembers(versionId).Where(row => movedProducts.Contains(row.ProductId)).ExecuteDeleteAsync(token);
                db.Set<FamilyProductRow>().AddRange(moving.Select(item => item.assignment)
                    .Where(item => item.FamilyId.HasValue).Select(item => new FamilyProductRow
                    {
                        ProductId = item.ProductId,
                        VersionId = versionId,
                        FamilyId = item.FamilyId!.Value,
                    }));
                await db.SaveChangesAsync(token);
                await InvalidateFamiliesAsync(versionId, affected.ToArray(), compositionChanged: true, token);
            }

            var stored = await db.Versions.AsNoTracking().SingleAsync(row => row.Id == versionId, token);
            return new(AnalysisWriteStatus.Success, stored.ToDetails());
        }, cancellationToken);

    private async Task<(Dictionary<Guid, AssignmentTarget> Targets, Dictionary<Guid, ProductFamilyRoute> ProductRoutes)>
        LoadAssignmentRoutesAsync(Guid versionId, FamilyRow[] families, Guid[] productIds, CancellationToken cancellationToken)
    {
        var entries = await FamilyConversion.ReadFamilyEntriesAsync(db, versionId, families, cancellationToken);
        var operations = await ReadRouteOperationsAsync(versionId, cancellationToken);
        var targets = entries.ToDictionary(family => family.Id,
            family => new AssignmentTarget(family, new RouteSignature(family.Data.Route.Steps, operations), family.ProductIds.ToHashSet()));
        var allProducts = productIds.Concat(entries.SelectMany(family => family.ProductIds)).Distinct().ToArray();
        var routes = await ReadProductRoutesAsync(versionId, allProducts, operations, cancellationToken);
        return (targets, routes);
    }

    private static List<AnalysisIssue> CheckAssignmentReferences(
        IReadOnlyList<Assignment> assignments, Dictionary<Guid, bool> products, Dictionary<Guid, AssignmentTarget> targets)
    {
        var issues = new List<AnalysisIssue>();
        for (var i = 0; i < assignments.Count; i++)
        {
            var path = FormattableString.Invariant($"/assignments/{i}");
            var assignment = assignments[i];
            if (!products.ContainsKey(assignment.ProductId))
            {
                issues.Add(new("UNKNOWN_PRODUCT", path + "/productId", "Продукт не найден в этой версии.",
                    "Выберите продукт текущей версии."));
            }

            if (assignment.FamilyId is { } familyId && !targets.ContainsKey(familyId))
            {
                issues.Add(new("UNKNOWN_FAMILY", path + "/familyId", "Семейство не найдено в этой версии.",
                    "Выберите семейство текущей версии."));
            }
        }

        return issues;
    }

    private sealed record AssignmentTarget(FamilyEntry Family, RouteSignature Route, HashSet<Guid> Members);
}
