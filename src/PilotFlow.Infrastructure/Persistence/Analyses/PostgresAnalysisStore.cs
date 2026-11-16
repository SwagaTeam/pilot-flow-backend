using System.Data;
using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;
using PilotFlow.Domain.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Analyses;

internal sealed class PostgresAnalysisStore(AnalysisDbContext db) : IAnalysisStore
{
    public async Task<AnalysisDetails> CreateAsync(string owner, AnalysisData data, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var analysis = new AnalysisRow { Id = Guid.NewGuid(), OwnerSubject = owner };
        var version = AnalysisVersionRow.Create(analysis.Id, data);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Analyses.Add(analysis);
        db.Versions.Add(version);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return analysis.ToDetails(version);
    }

    public async Task<Page<AnalysisDetails>> ListAsync(string owner, PageRequest page, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var owned = LatestVersions(owner);
        var unfilteredTotal = await owned.CountAsync(cancellationToken);
        var filtered = page.Query is { Length: > 0 } query
            ? owned.Where(item => EF.Functions.ILike(item.Version.Title, ContainsPattern(query), "\\"))
            : owned;
        var total = page.Query is { Length: > 0 } ? await filtered.CountAsync(cancellationToken) : unfilteredTotal;
        var items = await filtered.OrderBy(item => item.Analysis.CreatedAt).ThenBy(item => item.Analysis.Id)
            .Skip(page.Offset).Take(page.Limit).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(items.Select(item => item.Analysis.ToDetails(item.Version)).ToArray(),
            page.Offset, page.Limit, total, unfilteredTotal);
    }

    public async Task<AnalysisDetails?> GetAsync(string owner, Guid analysisId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var match = await LatestVersions(owner)
            .SingleOrDefaultAsync(item => item.Analysis.Id == analysisId, cancellationToken);
        return match?.Analysis.ToDetails(match.Version);
    }

    public async Task<Page<AnalysisVersionDetails>?> ListVersionsAsync(
        string owner, Guid analysisId, PageRequest page, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (!await db.Analyses.AsNoTracking().AnyAsync(
            item => item.Id == analysisId && item.OwnerSubject == owner, cancellationToken))
        {
            return null;
        }

        var owned = OwnedVersions(owner, analysisId);
        var unfilteredTotal = await owned.CountAsync(cancellationToken);
        var filtered = page.Query is { Length: > 0 } query
            ? owned.Where(version => EF.Functions.ILike(version.Title, ContainsPattern(query), "\\"))
            : owned;
        var total = page.Query is { Length: > 0 } ? await filtered.CountAsync(cancellationToken) : unfilteredTotal;
        var items = await filtered.OrderBy(version => version.VersionNumber)
            .Skip(page.Offset).Take(page.Limit).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(items.Select(static version => version.ToDetails()).ToArray(),
            page.Offset, page.Limit, total, unfilteredTotal);
    }

    public async Task<AnalysisVersionDetails?> GetVersionAsync(
        string owner, Guid analysisId, Guid versionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var version = await OwnedVersions(owner, analysisId).SingleOrDefaultAsync(
            item => item.Id == versionId, cancellationToken);
        return version?.ToDetails();
    }

    public async Task<AnalysisWriteResult<AnalysisVersionDetails>> ReplaceVersionAsync(
        string owner, VersionUpdate target, AnalysisData data, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var revenueEvidence = AnalysisVersionRow.WriteJson(data.RevenueEvidence);
        var completenessEvidence = AnalysisVersionRow.WriteJson(data.CompletenessEvidence);
        var participants = AnalysisVersionRow.WriteJson(data.Participants);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pilot.analysis_version AS version
            SET
                title = {data.Title},
                enterprise_name = {data.EnterpriseName},
                region = {data.Region},
                activity = {data.Activity},
                project_start_date = {data.ProjectStartDate},
                financial_start = {data.FinancialPeriod?.Start},
                financial_end = {data.FinancialPeriod?.End},
                labor_start = {data.LaborPeriod?.Start},
                labor_end = {data.LaborPeriod?.End},
                activities_start = {data.ActivitiesPeriod?.Start},
                activities_end = {data.ActivitiesPeriod?.End},
                forecast_start = {data.ForecastPeriod?.Start},
                forecast_end = {data.ForecastPeriod?.End},
                total_revenue = {data.TotalRevenue},
                currency = {data.Currency},
                accounting_basis = {data.AccountingBasis},
                revenue_evidence = {revenueEvidence}::jsonb,
                remainder_explanation = {data.RemainderExplanation},
                nomenclature_complete = {data.NomenclatureComplete},
                completeness_evidence = {completenessEvidence}::jsonb,
                participants = {participants}::jsonb,
                enterprise_inn = {data.EnterpriseInn},
                family_conclusions = {data.FamilyConclusions},
                ranking_conclusions = {data.RankingConclusions},
                revision = revision + 1
            FROM pilot.analysis AS analysis
            WHERE version.id = {target.VersionId}
              AND version.id = {target.ExpectedVersionId}
              AND version.analysis_id = {target.AnalysisId}
              AND analysis.id = version.analysis_id
              AND analysis.owner_subject = {owner}
              AND version.state = 'draft'
              AND version.revision = {target.ExpectedRevision}
            """, cancellationToken);

        if (changed == 0)
        {
            var state = await OwnedVersions(owner, target.AnalysisId).Where(version => version.Id == target.VersionId)
                .Select(version => version.State).SingleOrDefaultAsync(cancellationToken);
            return new(state switch
            {
                null => AnalysisWriteStatus.NotFound,
                "fixed" => AnalysisWriteStatus.Fixed,
                _ => AnalysisWriteStatus.RevisionConflict,
            });
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM pilot.selection WHERE version_id = {target.VersionId}", cancellationToken);
        var stored = await OwnedVersions(owner, target.AnalysisId)
            .SingleAsync(version => version.Id == target.VersionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(AnalysisWriteStatus.Success, stored.ToDetails());
    }

    private IQueryable<AnalysisVersionRow> OwnedVersions(string owner, Guid analysisId) =>
        from version in db.Versions.AsNoTracking()
        join analysis in db.Analyses.AsNoTracking() on version.AnalysisId equals analysis.Id
        where analysis.Id == analysisId && analysis.OwnerSubject == owner
        select version;

    private IQueryable<LatestVersion> LatestVersions(string owner) =>
        from analysis in db.Analyses.AsNoTracking()
        join version in db.Versions.AsNoTracking() on analysis.Id equals version.AnalysisId
        where analysis.OwnerSubject == owner
            && !db.Versions.Any(other => other.AnalysisId == analysis.Id && other.VersionNumber > version.VersionNumber)
        select new LatestVersion { Analysis = analysis, Version = version };

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(db.Database.GetConnectionString()))
        {
            throw new AnalysisStoreUnavailableException();
        }
    }

    private static string ContainsPattern(string query) => "%"
        + query.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    private sealed class LatestVersion
    {
        public required AnalysisRow Analysis { get; init; }
        public required AnalysisVersionRow Version { get; init; }
    }
}
