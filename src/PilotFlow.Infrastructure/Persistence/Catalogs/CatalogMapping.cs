using Microsoft.EntityFrameworkCore;
using PilotFlow.Infrastructure.Persistence.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Catalogs;

internal static class CatalogMapping
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<ProductRow>();
        product.ToTable("product", "pilot");
        product.HasKey(row => row.Id);
        product.HasOne<AnalysisVersionRow>().WithMany().HasForeignKey(row => row.VersionId)
            .OnDelete(DeleteBehavior.NoAction);
        product.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        product.Property(row => row.VersionId).HasColumnName("version_id");
        product.Property(row => row.Name).HasColumnName("name").HasMaxLength(300);
        product.Property(row => row.ProductCode).HasColumnName("product_code").HasMaxLength(100);
        product.Property(row => row.UnitCode).HasColumnName("unit_code").HasMaxLength(30);
        product.Property(row => row.SalesVolume).HasColumnName("sales_volume").HasPrecision(24, 6);
        product.Property(row => row.Revenue).HasColumnName("revenue").HasPrecision(24, 6);
        product.Property(row => row.RawMaterialCost).HasColumnName("raw_material_cost").HasPrecision(24, 6);
        product.Property(row => row.FinancialEvidence).HasColumnName("financial_evidence").HasColumnType("jsonb");
        product.Property(row => row.CalendarDurationDays).HasColumnName("calendar_duration_days").HasPrecision(24, 6);
        product.Property(row => row.DurationStart).HasColumnName("duration_start").HasMaxLength(300);
        product.Property(row => row.DurationEnd).HasColumnName("duration_end").HasMaxLength(300);
        product.Property(row => row.IncludesWaiting).HasColumnName("includes_waiting");
        product.Property(row => row.DurationEvidence).HasColumnName("duration_evidence").HasColumnType("jsonb");
        product.Property(row => row.InProductionPlan).HasColumnName("in_production_plan");
        product.Property(row => row.PlanEvidence).HasColumnName("plan_evidence").HasColumnType("jsonb");
        product.Property(row => row.IsOther).HasColumnName("is_other");

        var operation = modelBuilder.Entity<OperationRow>();
        operation.ToTable("operation", "pilot");
        operation.HasKey(row => row.Id);
        operation.HasOne<AnalysisVersionRow>().WithMany().HasForeignKey(row => row.VersionId)
            .OnDelete(DeleteBehavior.NoAction);
        operation.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        operation.Property(row => row.VersionId).HasColumnName("version_id");
        operation.Property(row => row.Name).HasColumnName("name").HasMaxLength(300);
        operation.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(20);
        operation.Property(row => row.WorkplaceName).HasColumnName("workplace_name").HasMaxLength(300);
        operation.Property(row => row.AggregationCoveragePercent).HasColumnName("aggregation_coverage_percent").HasPrecision(24, 6);
        operation.Property(row => row.AggregationEvidence).HasColumnName("aggregation_evidence").HasColumnType("jsonb");

        var step = modelBuilder.Entity<RouteStepRow>();
        step.ToTable("product_route_step", "pilot");
        step.HasKey(row => row.OccurrenceId);
        step.HasOne<AnalysisVersionRow>().WithMany().HasForeignKey(row => row.VersionId)
            .OnDelete(DeleteBehavior.NoAction);
        step.HasOne<ProductRow>().WithMany()
            .HasForeignKey(row => new { row.VersionId, row.ProductId })
            .HasPrincipalKey(row => new { row.VersionId, row.Id })
            .OnDelete(DeleteBehavior.Cascade);
        step.HasOne<OperationRow>().WithMany()
            .HasForeignKey(row => new { row.VersionId, row.OperationId })
            .HasPrincipalKey(row => new { row.VersionId, row.Id })
            .OnDelete(DeleteBehavior.NoAction);
        step.Property(row => row.OccurrenceId).HasColumnName("occurrence_id").ValueGeneratedNever();
        step.Property(row => row.VersionId).HasColumnName("version_id");
        step.Property(row => row.ProductId).HasColumnName("product_id");
        step.Property(row => row.OperationId).HasColumnName("operation_id");
        step.Property(row => row.Position).HasColumnName("position");
        step.Property(row => row.DurationHours).HasColumnName("duration_hours").HasPrecision(24, 6);
        step.Property(row => row.WorkplaceName).HasColumnName("workplace_name").HasMaxLength(300);
        step.Property(row => row.Evidence).HasColumnName("evidence").HasColumnType("jsonb");

        var completeness = modelBuilder.Entity<ProductCompletenessRow>();
        completeness.ToView("product_completeness", "pilot");
        completeness.HasNoKey();
        completeness.Property(row => row.ProductId).HasColumnName("product_id");
        completeness.Property(row => row.VersionId).HasColumnName("version_id");
        completeness.Property(row => row.Missing).HasColumnName("missing").HasColumnType("text[]");
    }
}
