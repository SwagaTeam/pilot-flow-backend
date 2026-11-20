using Microsoft.EntityFrameworkCore;
using PilotFlow.Infrastructure.Persistence.Analyses;
using PilotFlow.Infrastructure.Persistence.Catalogs;

namespace PilotFlow.Infrastructure.Persistence.Families;

internal static class FamilyMapping
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        var family = modelBuilder.Entity<FamilyRow>();
        family.ToTable("family", "pilot");
        family.HasKey(row => row.Id);
        family.HasOne<AnalysisVersionRow>().WithMany().HasForeignKey(row => row.VersionId)
            .OnDelete(DeleteBehavior.NoAction);
        family.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        family.Property(row => row.VersionId).HasColumnName("version_id");
        family.Property(row => row.Name).HasColumnName("name").HasMaxLength(300);
        family.Property(row => row.StartBoundary).HasColumnName("start_boundary").HasMaxLength(500);
        family.Property(row => row.EndBoundary).HasColumnName("end_boundary").HasMaxLength(500);
        family.Property(row => row.CompositionReason).HasColumnName("composition_reason");
        family.Property(row => row.Confirmed).HasColumnName("confirmed");
        family.Property(row => row.ConfirmationEvidence).HasColumnName("confirmation_evidence").HasColumnType("jsonb");

        var member = modelBuilder.Entity<FamilyProductRow>();
        member.ToTable("family_product", "pilot");
        member.HasKey(row => row.ProductId);
        member.HasOne<ProductRow>().WithMany()
            .HasForeignKey(row => new { row.VersionId, row.ProductId })
            .HasPrincipalKey(row => new { row.VersionId, row.Id })
            .OnDelete(DeleteBehavior.Cascade);
        member.HasOne<FamilyRow>().WithMany()
            .HasForeignKey(row => new { row.VersionId, row.FamilyId })
            .HasPrincipalKey(row => new { row.VersionId, row.Id })
            .OnDelete(DeleteBehavior.NoAction);
        member.Property(row => row.ProductId).HasColumnName("product_id").ValueGeneratedNever();
        member.Property(row => row.VersionId).HasColumnName("version_id");
        member.Property(row => row.FamilyId).HasColumnName("family_id");

        var step = modelBuilder.Entity<FamilyRouteStepRow>();
        step.ToTable("family_route_step", "pilot");
        step.HasKey(row => row.OccurrenceId);
        step.HasOne<FamilyRow>().WithMany()
            .HasForeignKey(row => new { row.VersionId, row.FamilyId })
            .HasPrincipalKey(row => new { row.VersionId, row.Id })
            .OnDelete(DeleteBehavior.Cascade);
        step.HasOne<OperationRow>().WithMany()
            .HasForeignKey(row => new { row.VersionId, row.OperationId })
            .HasPrincipalKey(row => new { row.VersionId, row.Id })
            .OnDelete(DeleteBehavior.NoAction);
        step.Property(row => row.OccurrenceId).HasColumnName("occurrence_id").ValueGeneratedNever();
        step.Property(row => row.VersionId).HasColumnName("version_id");
        step.Property(row => row.FamilyId).HasColumnName("family_id");
        step.Property(row => row.OperationId).HasColumnName("operation_id");
        step.Property(row => row.Position).HasColumnName("position");
        step.Property(row => row.DurationHours).HasColumnName("duration_hours").HasPrecision(24, 6);
        step.Property(row => row.WorkplaceName).HasColumnName("workplace_name").HasMaxLength(300);
        step.Property(row => row.Evidence).HasColumnName("evidence").HasColumnType("jsonb");
    }
}
