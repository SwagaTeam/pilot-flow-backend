using Microsoft.EntityFrameworkCore;
using Npgsql;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Evaluation;
using PilotFlow.Application.Families;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;
using PilotFlow.Domain.Families;
using PilotFlow.Infrastructure.Persistence.Families;

namespace PilotFlow.Infrastructure.Persistence.Catalogs;

internal sealed partial class PostgresVersionCatalog
{
    public Task<Versioned<Page<FamilyEntry>>?> ListFamiliesAsync(
        VersionScope scope, PageRequest page, CancellationToken cancellationToken) =>
        transactions.ReadAsync<Page<FamilyEntry>>(scope, async token =>
        {
            var all = Families(scope.VersionId).AsNoTracking();
            var unfilteredTotal = await all.CountAsync(token);
            var filtered = string.IsNullOrEmpty(page.Query)
                ? all
                : all.Where(row => EF.Functions.ILike(row.Name, VersionPersistence.ContainsPattern(page.Query), "\\"));
            var total = string.IsNullOrEmpty(page.Query) ? unfilteredTotal : await filtered.CountAsync(token);
            var rows = await filtered.OrderBy(row => row.Name).ThenBy(row => row.Id)
                .Skip(page.Offset).Take(page.Limit).ToArrayAsync(token);
            var items = await FamilyConversion.ReadFamilyEntriesAsync(db, scope.VersionId, rows, token);
            return new Page<FamilyEntry>(items, page.Offset, page.Limit, total, unfilteredTotal);
        }, cancellationToken);

    public Task<Versioned<FamilyEntry>?> GetFamilyAsync(
        VersionScope scope, Guid familyId, CancellationToken cancellationToken) =>
        transactions.ReadAsync<FamilyEntry>(scope, async token =>
        {
            var row = await Families(scope.VersionId).AsNoTracking().SingleOrDefaultAsync(item => item.Id == familyId, token);
            return row is null ? null : (await FamilyConversion.ReadFamilyEntriesAsync(db, scope.VersionId, [row], token))[0];
        }, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<FamilyEntry>>> SaveFamilyAsync(
        CatalogChange change, Guid? familyId, ValidatedInput<FamilyData> input, CancellationToken cancellationToken) =>
        transactions.WriteAsync<FamilyEntry>(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            var existing = familyId.HasValue
                ? await Families(versionId).SingleOrDefaultAsync(row => row.Id == familyId.Value, token)
                : null;
            if (familyId.HasValue && existing is null)
            {
                return new(AnalysisWriteStatus.NotFound);
            }

            if (input.Issues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: input.Issues);
            }

            var id = familyId ?? Guid.NewGuid();
            var current = existing is null ? null : (await FamilyConversion.ReadFamilyEntriesAsync(db, versionId, [existing], token))[0];
            var members = current?.ProductIds ?? [];
            var route = input.Data.Route.Steps.OrderBy(static step => step.Position).ToArray();
            var operations = await ReadRouteOperationsAsync(versionId, token);
            var unknownOperations = input.Data.Route.Steps.Select((step, index) => (step, index))
                .Where(item => !operations.ContainsKey(item.step.OperationId))
                .Select(item => new AnalysisIssue("UNKNOWN_ROUTE_OPERATION",
                    FormattableString.Invariant($"/route/steps/{item.index}/operationId"),
                    "Операция не найдена в этой версии.", "Выберите операцию текущей версии."))
                .ToArray();
            if (unknownOperations.Length > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: unknownOperations);
            }

            var routeChanged = current is not null && !current.Data.Route.Steps.SequenceEqual(route);
            var data = input.Data with
            {
                Confirmed = input.Data.Confirmed && !(routeChanged && current!.Data.Confirmed),
                Route = new RouteData { Steps = route },
            };
            if (data.Confirmed)
            {
                var productRoutes = await ReadProductRoutesAsync(versionId, members.ToArray(), operations, token);
                var issues = FamilyValidation.Confirm(data, new RouteSignature(route, operations), productRoutes.Values.ToArray());
                if (issues.Count > 0)
                {
                    return new(AnalysisWriteStatus.ValidationFailed, Issues: issues);
                }
            }

            if (routeChanged)
            {
                await InvalidateFamiliesAsync(versionId, [id], compositionChanged: false, token);
                await FamilyRouteSteps(versionId).Where(row => row.FamilyId == id).ExecuteDeleteAsync(token);
            }

            var family = new FamilyRow
            {
                Id = id,
                VersionId = versionId,
                Name = data.Name,
                StartBoundary = data.StartBoundary,
                EndBoundary = data.EndBoundary,
                CompositionReason = data.CompositionReason,
                Confirmed = data.Confirmed,
                ConfirmationEvidence = StoredJson.Write(data.ConfirmationEvidence),
            };
            if (existing is null)
            {
                db.Set<FamilyRow>().Add(family);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(family);
            }

            if (existing is null || routeChanged)
            {
                db.Set<FamilyRouteStepRow>().AddRange(route.Select(step => new FamilyRouteStepRow
                {
                    OccurrenceId = step.OccurrenceId,
                    VersionId = versionId,
                    FamilyId = id,
                    OperationId = step.OperationId,
                    Position = step.Position,
                    DurationHours = step.DurationHours,
                    WorkplaceName = step.WorkplaceName,
                    Evidence = StoredJson.Write(step.Evidence),
                }));
            }

            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "family_route_step_pkey" })
            {
                return new(AnalysisWriteStatus.StateConflict);
            }

            return new(AnalysisWriteStatus.Success, new FamilyEntry(id, versionId, data, members));
        }, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<bool>>> DeleteFamilyAsync(
        CatalogChange change, Guid familyId, CancellationToken cancellationToken) =>
        transactions.WriteAsync<bool>(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            if (await FamilyMembers(versionId).AnyAsync(row => row.FamilyId == familyId, token))
            {
                return new(AnalysisWriteStatus.StateConflict);
            }

            var deleted = await Families(versionId).Where(row => row.Id == familyId).ExecuteDeleteAsync(token);
            return deleted == 0 ? new(AnalysisWriteStatus.NotFound) : new(AnalysisWriteStatus.Success, true);
        }, cancellationToken);

    public Task<Versioned<IReadOnlyList<FamilySuggestion>>?> SuggestFamiliesAsync(
        VersionScope scope, CancellationToken cancellationToken) =>
        transactions.ReadAsync<IReadOnlyList<FamilySuggestion>>(scope, async token =>
        {
            var versionId = scope.VersionId;
            var products = await db.Set<ProductRow>().AsNoTracking()
                .Where(row => row.VersionId == versionId && !row.IsOther)
                .OrderBy(row => row.Name).ThenBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
            var operations = await ReadRouteOperationsAsync(versionId, token);
            var routes = await ReadProductRoutesAsync(versionId, products, operations, token);
            return products.Select(id => routes[id]).Where(row => row.Signature.Count > 0)
                .GroupBy(row => row.Signature)
                .Select(group =>
                {
                    var members = group.ToArray();
                    var cycle = FamilyCycle.Difference(members);
                    decimal? wireCycle = null;
                    if (cycle is { } known)
                    {
                        if (!known.TryRoundForWire(out var rounded))
                        {
                            throw new EvaluationNumericRangeException("/cycleDifferencePercent", members[0].ProductId);
                        }

                        wireCycle = rounded;
                    }

                    IReadOnlyList<FamilyNotice> issues = cycle switch
                    {
                        null => [new("CYCLE_DIFFERENCE_UNKNOWN", "warning", "/route/steps",
                            "Различие циклов неизвестно: время должно быть указано для каждого вхождения, а минимальный цикл больше нуля.",
                            "Уточните продолжительность вхождений.", null)],
                        { } difference when FamilyCycle.ExceedsRecommendation(difference) => [new("CYCLE_DIFFERENCE_ABOVE_RECOMMENDATION", "warning", "/compositionReason",
                            "Различие циклов превышает 20%.", "Обоснуйте объединение продукции в одно семейство.", null)],
                        _ => [],
                    };
                    return new FamilySuggestion(members.Select(row => row.ProductId).ToArray(),
                        group.Key.OperationIds, wireCycle, issues);
                }).ToArray();
        }, cancellationToken);

    private IQueryable<FamilyRow> Families(Guid versionId) =>
        db.Set<FamilyRow>().Where(row => row.VersionId == versionId);

    private IQueryable<FamilyProductRow> FamilyMembers(Guid versionId) =>
        db.Set<FamilyProductRow>().Where(row => row.VersionId == versionId);

    private IQueryable<FamilyRouteStepRow> FamilyRouteSteps(Guid versionId) =>
        db.Set<FamilyRouteStepRow>().Where(row => row.VersionId == versionId);

    private async Task<Dictionary<Guid, OperationData>> ReadRouteOperationsAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var operations = await Operations(versionId).AsNoTracking().ToArrayAsync(cancellationToken);
        return operations.ToDictionary(row => row.Id, row => CatalogConversion.ToOperation(row).Data);
    }

    private async Task<Dictionary<Guid, ProductFamilyRoute>> ReadProductRoutesAsync(
        Guid versionId, Guid[] productIds, IReadOnlyDictionary<Guid, OperationData> operations, CancellationToken cancellationToken)
    {
        var rows = await db.Set<RouteStepRow>().AsNoTracking()
            .Where(row => row.VersionId == versionId && productIds.Contains(row.ProductId))
            .ToArrayAsync(cancellationToken);
        var steps = rows.ToLookup(row => row.ProductId, CatalogConversion.ToStep);
        return productIds.ToDictionary(id => id,
            id => new ProductFamilyRoute(id, new RouteSignature(steps[id], operations), FamilyCycle.RouteDuration(steps[id])));
    }
}
