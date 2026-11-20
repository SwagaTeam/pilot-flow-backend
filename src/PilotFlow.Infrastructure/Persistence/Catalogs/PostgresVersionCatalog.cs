using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Families;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Catalogs;
using PilotFlow.Infrastructure.Persistence.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Catalogs;

internal sealed partial class PostgresVersionCatalog(AnalysisDbContext db, VersionCatalogTransactions transactions) : IVersionCatalog, IFamilyCatalog
{
    public Task<Versioned<Page<ProductEntry>>?> ListProductsAsync(
        VersionScope scope, PageRequest page, string completeness, CancellationToken cancellationToken) =>
        transactions.ReadAsync<Page<ProductEntry>>(scope, async token =>
        {
            var all = Products(scope.VersionId);
            var unfilteredTotal = await all.CountAsync(token);
            var filtered = all;
            if (!string.IsNullOrEmpty(page.Query))
            {
                var pattern = VersionPersistence.ContainsPattern(page.Query);
                filtered = filtered.Where(item => EF.Functions.ILike(item.Row.Name, pattern, "\\")
                    || (item.Row.ProductCode != null && EF.Functions.ILike(item.Row.ProductCode, pattern, "\\")));
            }

            filtered = completeness switch
            {
                "complete" => filtered.Where(item => item.Missing.Length == 0),
                "incomplete" => filtered.Where(item => item.Missing.Length > 0),
                _ => filtered,
            };
            var total = await filtered.CountAsync(token);
            var items = await filtered.OrderBy(item => item.Row.Name).ThenBy(item => item.Row.Id)
                .Skip(page.Offset).Take(page.Limit).ToArrayAsync(token);
            return new Page<ProductEntry>(items.Select(static item => item.ToEntry()).ToArray(),
                page.Offset, page.Limit, total, unfilteredTotal);
        }, cancellationToken);

    public Task<Versioned<ProductEntry>?> GetProductAsync(
        VersionScope scope, Guid productId, CancellationToken cancellationToken) =>
        transactions.ReadAsync<ProductEntry>(scope, async token =>
        {
            var item = await Products(scope.VersionId).SingleOrDefaultAsync(row => row.Row.Id == productId, token);
            return item?.ToEntry();
        }, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<ProductEntry>>> SaveProductAsync(
        CatalogChange change, Guid? productId, ValidatedInput<ProductData> input, CancellationToken cancellationToken) =>
        transactions.WriteAsync<ProductEntry>(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            var existing = productId.HasValue
                ? await db.Set<ProductRow>().SingleOrDefaultAsync(
                    row => row.VersionId == versionId && row.Id == productId.Value, token)
                : null;
            if (productId.HasValue && existing is null)
            {
                return new(AnalysisWriteStatus.NotFound);
            }

            if (input.Issues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: input.Issues);
            }

            if (input.Data.IsOther && existing is { IsOther: false }
                && await ProductFamilyAsync(versionId, existing.Id, token) is not null)
            {
                return new(AnalysisWriteStatus.StateConflict);
            }

            var row = CatalogConversion.NewProduct(productId ?? Guid.NewGuid(), versionId, input.Data);
            if (existing is null)
            {
                db.Set<ProductRow>().Add(row);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(row);
            }

            await db.SaveChangesAsync(token);
            var stored = await Products(versionId).SingleAsync(item => item.Row.Id == row.Id, token);
            return new(AnalysisWriteStatus.Success, stored.ToEntry());
        }, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<bool>>> DeleteProductAsync(
        CatalogChange change, Guid productId, CancellationToken cancellationToken) =>
        transactions.WriteAsync<bool>(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            var family = await ProductFamilyAsync(versionId, productId, token);
            var deleted = await db.Set<ProductRow>().Where(row => row.VersionId == versionId && row.Id == productId)
                .ExecuteDeleteAsync(token);
            if (deleted == 0)
            {
                return new(AnalysisWriteStatus.NotFound);
            }

            if (family.HasValue)
            {
                await InvalidateFamiliesAsync(versionId, [family.Value], compositionChanged: true, token);
            }

            return new(AnalysisWriteStatus.Success, true);
        }, cancellationToken);

    public Task<Versioned<RouteData>?> GetRouteAsync(
        VersionScope scope, Guid productId, CancellationToken cancellationToken) =>
        transactions.ReadAsync<RouteData>(scope, async token =>
        {
            if (!await HasProductAsync(scope.VersionId, productId, token))
            {
                return null;
            }

            var rows = await ProductRoute(scope.VersionId, productId).AsNoTracking().OrderBy(row => row.Position)
                .ToArrayAsync(token);
            return new RouteData { Steps = rows.Select(CatalogConversion.ToStep).ToArray() };
        }, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<RouteData>>> ReplaceRouteAsync(
        CatalogChange change, Guid productId, ValidatedInput<RouteData> input, CancellationToken cancellationToken) =>
        transactions.WriteAsync<RouteData>(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            if (!await HasProductAsync(versionId, productId, token))
            {
                return new(AnalysisWriteStatus.NotFound);
            }

            if (input.Issues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: input.Issues);
            }

            var operationIds = input.Data.Steps.Select(step => step.OperationId).Distinct().ToArray();
            var available = await db.Set<OperationRow>().AsNoTracking()
                .Where(row => row.VersionId == versionId && operationIds.Contains(row.Id))
                .Select(row => row.Id).ToListAsync(token);
            var found = available.ToHashSet();
            var invalid = input.Data.Steps.Select((step, index) => (Step: step, Index: index))
                .Where(item => !found.Contains(item.Step.OperationId))
                .Select(item => new AnalysisIssue("UNKNOWN_ROUTE_OPERATION",
                    FormattableString.Invariant($"/steps/{item.Index}/operationId"),
                    "Операция не найдена в этой версии.", "Выберите операцию из справочника текущей версии."))
                .ToArray();
            if (invalid.Length > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: invalid);
            }

            var rows = input.Data.Steps.OrderBy(step => step.Position)
                .Select(step => CatalogConversion.NewStep(versionId, productId, step)).ToArray();
            await ProductRoute(versionId, productId).ExecuteDeleteAsync(token);
            db.Set<RouteStepRow>().AddRange(rows);
            await db.SaveChangesAsync(token);

            var family = await ProductFamilyAsync(versionId, productId, token);
            if (family.HasValue)
            {
                await InvalidateFamiliesAsync(versionId, [family.Value], compositionChanged: false, token);
            }

            return new(AnalysisWriteStatus.Success,
                new RouteData { Steps = rows.Select(CatalogConversion.ToStep).ToArray() });
        }, cancellationToken);

    private IQueryable<ProductProjection> Products(Guid versionId) =>
        from row in db.Set<ProductRow>().AsNoTracking()
        join quality in db.Set<ProductCompletenessRow>() on row.Id equals quality.ProductId
        where row.VersionId == versionId && quality.VersionId == versionId
        select new ProductProjection { Row = row, Missing = quality.Missing };

    private Task<bool> HasProductAsync(Guid versionId, Guid productId, CancellationToken cancellationToken) =>
        db.Set<ProductRow>().AsNoTracking().AnyAsync(
            row => row.VersionId == versionId && row.Id == productId, cancellationToken);

    private IQueryable<RouteStepRow> ProductRoute(Guid versionId, Guid productId) =>
        db.Set<RouteStepRow>().Where(row => row.VersionId == versionId && row.ProductId == productId);

    private Task<Guid?> ProductFamilyAsync(Guid versionId, Guid productId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<Guid?>($"""
            SELECT family_id AS "Value" FROM pilot.family_product
            WHERE version_id = {versionId} AND product_id = {productId}
            """).SingleOrDefaultAsync(cancellationToken);

    private sealed class ProductProjection
    {
        public required ProductRow Row { get; init; }
        public required string[] Missing { get; init; }
        public ProductEntry ToEntry() => CatalogConversion.ToProduct(Row, Missing);
    }
}
