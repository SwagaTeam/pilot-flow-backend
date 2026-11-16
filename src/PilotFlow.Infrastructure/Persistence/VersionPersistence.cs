using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;
using PilotFlow.Infrastructure.Persistence.Analyses;

namespace PilotFlow.Infrastructure.Persistence;

internal static class VersionPersistence
{
    internal static IQueryable<AnalysisVersionRow> OwnedVersions(
        this AnalysisDbContext db, string owner, Guid analysisId) =>
        from version in db.Versions.AsNoTracking()
        join analysis in db.Analyses.AsNoTracking() on version.AnalysisId equals analysis.Id
        where analysis.Id == analysisId && analysis.OwnerSubject == owner
        select version;

    internal static void EnsureConfigured(this AnalysisDbContext db)
    {
        if (string.IsNullOrWhiteSpace(db.Database.GetConnectionString()))
        {
            throw new AnalysisStoreUnavailableException();
        }
    }

    internal static string ContainsPattern(string query) => "%"
        + query.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
