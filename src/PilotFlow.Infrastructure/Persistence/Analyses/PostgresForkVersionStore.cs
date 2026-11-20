using Microsoft.EntityFrameworkCore;
using Npgsql;
using PilotFlow.Application.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Analyses;

internal sealed class PostgresForkVersionStore(AnalysisDbContext db) : IForkVersionStore
{
    public async Task<AnalysisWriteResult<AnalysisVersionDetails>> ForkAsync(
        string owner, VersionUpdate source, string reason, CancellationToken cancellationToken)
    {
        db.EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var versionId = source.VersionId;
        var analysisId = source.AnalysisId;
        var version = await db.Database.SqlQuery<ForkSource>($"""
            SELECT version.version_number AS "VersionNumber", version.state AS "State", version.revision AS "Revision"
            FROM pilot.analysis_version AS version
            JOIN pilot.analysis AS analysis ON analysis.id = version.analysis_id
            WHERE version.id = {versionId} AND analysis.id = {analysisId} AND analysis.owner_subject = {owner}
            FOR UPDATE OF version
            """).SingleOrDefaultAsync(cancellationToken);
        if (version is null)
        {
            return new(AnalysisWriteStatus.NotFound);
        }

        if (source.ExpectedVersionId != versionId || source.ExpectedRevision != version.Revision)
        {
            return new(AnalysisWriteStatus.RevisionConflict);
        }

        if (version.State != "fixed" || await db.Versions.AnyAsync(candidate => candidate.AnalysisId == analysisId
            && (candidate.VersionNumber > version.VersionNumber || candidate.State == "draft"), cancellationToken))
        {
            return new(AnalysisWriteStatus.StateConflict);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Contains('\0'))
        {
            return new(AnalysisWriteStatus.ValidationFailed,
                Issues: [new("INVALID_FORK_REASON", "/reason", "Укажите причину создания новой версии.",
                    "Введите непустой текст без нулевых символов.")]);
        }

        var targetId = Guid.NewGuid();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pilot.analysis_version (
                    id, analysis_id, version_number, rules_version, fork_reason,
                    title, enterprise_name, enterprise_inn, region, activity, family_conclusions, ranking_conclusions,
                    project_start_date, financial_start, financial_end, labor_start, labor_end,
                    activities_start, activities_end, forecast_start, forecast_end, total_revenue, currency,
                    accounting_basis, revenue_evidence, remainder_explanation, nomenclature_complete,
                    completeness_evidence, participants)
                SELECT {targetId}, analysis_id, version_number + 1, rules_version, {reason},
                    title, enterprise_name, enterprise_inn, region, activity, family_conclusions, ranking_conclusions,
                    project_start_date, financial_start, financial_end, labor_start, labor_end,
                    activities_start, activities_end, forecast_start, forecast_end, total_revenue, currency,
                    accounting_basis, revenue_evidence, remainder_explanation, nomenclature_complete,
                    completeness_evidence, participants
                FROM pilot.analysis_version WHERE id = {versionId}
                """, cancellationToken);

            var ids = await NewIdentifiersAsync(versionId, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(CopyWorkingData, [
                new NpgsqlParameter<Guid>("source", versionId),
                new NpgsqlParameter<Guid>("target", targetId),
                new NpgsqlParameter<string>("ids", ids),
            ], cancellationToken);
            var created = await db.OwnedVersions(owner, analysisId)
                .SingleAsync(candidate => candidate.Id == targetId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(AnalysisWriteStatus.Success, created.ToDetails());
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation
            && error.ConstraintName is "ux_one_draft" or "analysis_version_analysis_id_version_number_key")
        {
            return new(AnalysisWriteStatus.StateConflict);
        }
    }

    private Task<string> NewIdentifiersAsync(Guid versionId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<string>($$"""
            SELECT jsonb_build_object(
                'products', (SELECT jsonb_object_agg(id, gen_random_uuid()) FROM pilot.product WHERE version_id = {{versionId}}),
                'operations', (SELECT jsonb_object_agg(id, gen_random_uuid()) FROM pilot.operation WHERE version_id = {{versionId}}),
                'families', (SELECT jsonb_object_agg(id, gen_random_uuid()) FROM pilot.family WHERE version_id = {{versionId}})
            )::text AS "Value"
            """).SingleAsync(cancellationToken);

    private sealed class ForkSource
    {
        public int VersionNumber { get; init; }
        public required string State { get; init; }
        public long Revision { get; init; }
    }

    private const string CopyWorkingData = """
        INSERT INTO pilot.product (
            id, version_id, name, product_code, unit_code, sales_volume, revenue, raw_material_cost,
            financial_evidence, calendar_duration_days, duration_start, duration_end, includes_waiting,
            duration_evidence, in_production_plan, plan_evidence, is_other)
        SELECT (@ids::jsonb #>> ARRAY['products', id::text])::uuid, @target, name, product_code,
            unit_code, sales_volume, revenue, raw_material_cost, financial_evidence, calendar_duration_days,
            duration_start, duration_end, includes_waiting, duration_evidence, in_production_plan, plan_evidence, is_other
        FROM pilot.product WHERE version_id = @source;

        INSERT INTO pilot.operation (
            id, version_id, name, kind, workplace_name, aggregation_coverage_percent, aggregation_evidence)
        SELECT (@ids::jsonb #>> ARRAY['operations', id::text])::uuid, @target, name, kind,
            workplace_name, aggregation_coverage_percent, aggregation_evidence
        FROM pilot.operation WHERE version_id = @source;

        INSERT INTO pilot.family (
            id, version_id, name, start_boundary, end_boundary, composition_reason, confirmed, confirmation_evidence)
        SELECT (@ids::jsonb #>> ARRAY['families', id::text])::uuid, @target, name,
            start_boundary, end_boundary, composition_reason, false, confirmation_evidence
        FROM pilot.family WHERE version_id = @source;

        INSERT INTO pilot.family_product (version_id, product_id, family_id)
        SELECT @target, (@ids::jsonb #>> ARRAY['products', product_id::text])::uuid,
            (@ids::jsonb #>> ARRAY['families', family_id::text])::uuid
        FROM pilot.family_product WHERE version_id = @source;

        INSERT INTO pilot.product_route_step (
            occurrence_id, version_id, product_id, operation_id, position, duration_hours, workplace_name, evidence)
        SELECT gen_random_uuid(), @target, (@ids::jsonb #>> ARRAY['products', product_id::text])::uuid,
            (@ids::jsonb #>> ARRAY['operations', operation_id::text])::uuid, position, duration_hours, workplace_name, evidence
        FROM pilot.product_route_step WHERE version_id = @source;

        INSERT INTO pilot.family_route_step (
            occurrence_id, version_id, family_id, operation_id, position, duration_hours, workplace_name, evidence)
        SELECT gen_random_uuid(), @target, (@ids::jsonb #>> ARRAY['families', family_id::text])::uuid,
            (@ids::jsonb #>> ARRAY['operations', operation_id::text])::uuid, position, duration_hours, workplace_name, evidence
        FROM pilot.family_route_step WHERE version_id = @source;

        INSERT INTO pilot.family_metrics (
            family_id, version_id, sales_growth_percent, sales_growth_evidence,
            load_percent, load_actual, load_capacity, load_unit, load_evidence, production_type, production_type_evidence,
            labor_mode, labor_hours_per_unit, direct_labor_hours, output_quantity, labor_unit_code, labor_base_quantity,
            labor_evidence, comparability_explanation)
        SELECT (@ids::jsonb #>> ARRAY['families', family_id::text])::uuid, @target,
            sales_growth_percent, sales_growth_evidence,
            load_percent, load_actual, load_capacity, load_unit, load_evidence, production_type, production_type_evidence,
            labor_mode, labor_hours_per_unit, direct_labor_hours, output_quantity, labor_unit_code, labor_base_quantity,
            labor_evidence, comparability_explanation
        FROM pilot.family_metrics WHERE version_id = @source;

        INSERT INTO pilot.protocol (
            version_id, protocol_number, agreement_number, agreement_date, enterprise_party, partner_party, signing_place,
            prepared_by_name, prepared_by_position, approved_by_name, approved_by_position, conclusions)
        SELECT @target, protocol_number, agreement_number, agreement_date, enterprise_party, partner_party, signing_place,
            prepared_by_name, prepared_by_position, approved_by_name, approved_by_position, conclusions
        FROM pilot.protocol WHERE version_id = @source;
        """;
}
