using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Domain.Families;

namespace PilotFlow.Application.Families;

public sealed record FamilyEntry(Guid Id, Guid VersionId, FamilyData Data, IReadOnlyList<Guid> ProductIds);
public sealed record FamilyNotice(string Code, string Severity, string Path, string Message, string Suggestion, Guid? EntityId);
public sealed record FamilySuggestion(
    IReadOnlyList<Guid> ProductIds, IReadOnlyList<Guid> OperationIdsInOrder, decimal? CycleDifferencePercent,
    IReadOnlyList<FamilyNotice> Issues);
public sealed record AssignmentOption(Guid FamilyId, bool CanAssign, bool RouteMatches, IReadOnlyList<FamilyNotice> Issues);
public sealed record ProductAssignmentOptions(Guid ProductId, IReadOnlyList<AssignmentOption> Options, bool CanCreateSeparateFamily);

public interface IFamilyCatalog
{
    Task<Versioned<Page<FamilyEntry>>?> ListFamiliesAsync(
        VersionScope scope, PageRequest page, CancellationToken cancellationToken);
    Task<Versioned<FamilyEntry>?> GetFamilyAsync(
        VersionScope scope, Guid familyId, CancellationToken cancellationToken);
    Task<AnalysisWriteResult<Versioned<FamilyEntry>>> SaveFamilyAsync(
        CatalogChange change, Guid? familyId, ValidatedInput<FamilyData> input, CancellationToken cancellationToken);
    Task<AnalysisWriteResult<Versioned<bool>>> DeleteFamilyAsync(
        CatalogChange change, Guid familyId, CancellationToken cancellationToken);
    Task<Versioned<IReadOnlyList<FamilySuggestion>>?> SuggestFamiliesAsync(
        VersionScope scope, CancellationToken cancellationToken);
    Task<Versioned<ProductAssignmentOptions>?> GetAssignmentOptionsAsync(
        VersionScope scope, Guid productId, CancellationToken cancellationToken);
    Task<AnalysisWriteResult<Versioned<AnalysisVersionDetails>>> AssignProductsAsync(
        CatalogChange change, ValidatedInput<AssignmentsData> input, CancellationToken cancellationToken);
}

public sealed class FamilyCommands(IFamilyCatalog catalog)
{
    public Task<AnalysisWriteResult<Versioned<FamilyEntry>>> SaveFamilyAsync(
        CatalogChange change, Guid? familyId, FamilyData data, CancellationToken cancellationToken) =>
        catalog.SaveFamilyAsync(change, familyId, new(data, FamilyValidation.Validate(data)), cancellationToken);

    public Task<AnalysisWriteResult<Versioned<AnalysisVersionDetails>>> AssignProductsAsync(
        CatalogChange change, AssignmentsData data, CancellationToken cancellationToken) =>
        catalog.AssignProductsAsync(change, new(data, FamilyValidation.Validate(data)), cancellationToken);
}
