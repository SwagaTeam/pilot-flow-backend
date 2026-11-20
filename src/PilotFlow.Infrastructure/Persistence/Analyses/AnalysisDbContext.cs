using Microsoft.EntityFrameworkCore;
using PilotFlow.Infrastructure.Persistence.Catalogs;
using PilotFlow.Infrastructure.Persistence.Families;

namespace PilotFlow.Infrastructure.Persistence.Analyses;

internal sealed class AnalysisDbContext(DbContextOptions<AnalysisDbContext> options) : DbContext(options)
{
    public DbSet<AnalysisRow> Analyses => Set<AnalysisRow>();
    public DbSet<AnalysisVersionRow> Versions => Set<AnalysisVersionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var analysis = modelBuilder.Entity<AnalysisRow>();
        analysis.ToTable("analysis", "pilot");
        analysis.HasKey(row => row.Id);
        analysis.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        analysis.Property(row => row.OwnerSubject).HasColumnName("owner_subject");
        analysis.Property(row => row.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        var version = modelBuilder.Entity<AnalysisVersionRow>();
        version.ToTable("analysis_version", "pilot");
        version.HasKey(row => row.Id);
        version.HasOne<AnalysisRow>().WithMany().HasForeignKey(row => row.AnalysisId);
        version.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        version.Property(row => row.AnalysisId).HasColumnName("analysis_id");
        version.Property(row => row.VersionNumber).HasColumnName("version_number");
        version.Property(row => row.Revision).HasColumnName("revision");
        version.Property(row => row.State).HasColumnName("state");
        version.Property(row => row.RulesVersion).HasColumnName("rules_version");
        version.Property(row => row.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        version.Property(row => row.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        version.Property(row => row.Title).HasColumnName("title");
        version.Property(row => row.EnterpriseName).HasColumnName("enterprise_name");
        version.Property(row => row.Region).HasColumnName("region");
        version.Property(row => row.Activity).HasColumnName("activity");
        version.Property(row => row.ProjectStartDate).HasColumnName("project_start_date");
        version.Property(row => row.FinancialStart).HasColumnName("financial_start");
        version.Property(row => row.FinancialEnd).HasColumnName("financial_end");
        version.Property(row => row.LaborStart).HasColumnName("labor_start");
        version.Property(row => row.LaborEnd).HasColumnName("labor_end");
        version.Property(row => row.ActivitiesStart).HasColumnName("activities_start");
        version.Property(row => row.ActivitiesEnd).HasColumnName("activities_end");
        version.Property(row => row.ForecastStart).HasColumnName("forecast_start");
        version.Property(row => row.ForecastEnd).HasColumnName("forecast_end");
        version.Property(row => row.TotalRevenue).HasColumnName("total_revenue").HasPrecision(24, 6);
        version.Property(row => row.Currency).HasColumnName("currency");
        version.Property(row => row.AccountingBasis).HasColumnName("accounting_basis");
        version.Property(row => row.RevenueEvidence).HasColumnName("revenue_evidence").HasColumnType("jsonb");
        version.Property(row => row.RemainderExplanation).HasColumnName("remainder_explanation");
        version.Property(row => row.NomenclatureComplete).HasColumnName("nomenclature_complete");
        version.Property(row => row.CompletenessEvidence).HasColumnName("completeness_evidence").HasColumnType("jsonb");
        version.Property(row => row.Participants).HasColumnName("participants").HasColumnType("jsonb");
        version.Property(row => row.EnterpriseInn).HasColumnName("enterprise_inn");
        version.Property(row => row.FamilyConclusions).HasColumnName("family_conclusions");
        version.Property(row => row.RankingConclusions).HasColumnName("ranking_conclusions");
        CatalogMapping.Configure(modelBuilder);
        FamilyMapping.Configure(modelBuilder);
    }
}
