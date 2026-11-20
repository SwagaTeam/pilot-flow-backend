using PilotFlow.Domain.Catalogs;

namespace PilotFlow.Application.Catalogs;

public sealed record ProductDocument(
    Guid Id, Guid VersionId, ProductData Data, string Completeness, IReadOnlyList<ProductDocument.Issue> Issues)
{
    public static ProductDocument From(ProductEntry product) => new(
        product.Id, product.VersionId, product.Data, product.Missing.Count == 0 ? "complete" : "incomplete",
        product.Missing.Select(field => new Issue("PRODUCT_FIELD_MISSING", "warning", "/data/" + field,
            "Не хватает данных для проверки продукции.", "Заполните значение или укажите источник с датой или периодом.", product.Id))
            .ToArray());

    public sealed record Issue(string Code, string Severity, string Path, string Message, string Suggestion, Guid EntityId);
}
