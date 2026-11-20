using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Decisions;
using PilotFlow.Domain.Decisions;
using PilotFlow.Infrastructure.Persistence.Analyses;
using PilotFlow.Infrastructure.Persistence.Catalogs;

namespace PilotFlow.Infrastructure.Persistence.Decisions;

internal sealed class PostgresFamilyMetricsStore(AnalysisDbContext db, VersionCatalogTransactions transactions) : IFamilyMetricsStore
{
    public Task<Versioned<MetricsData>?> GetAsync(VersionScope scope, Guid familyId, CancellationToken cancellationToken) =>
        transactions.ReadAsync(scope, token => GetDataAsync(scope.VersionId, familyId, token), cancellationToken);

    public Task<AnalysisWriteResult<Versioned<MetricsData>>> ReplaceAsync(
        CatalogChange change, Guid familyId, MetricsData data, CancellationToken cancellationToken) =>
        transactions.WriteAsync(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            if (!await ExistsAsync(versionId, familyId, token))
            {
                return new AnalysisWriteResult<MetricsData>(AnalysisWriteStatus.NotFound);
            }

            var (prepared, issues) = MetricsValidation.Prepare(data);
            if (issues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: issues);
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pilot.family_metrics (
                    version_id, family_id, sales_growth_percent, sales_growth_evidence, load_percent, load_actual,
                    load_capacity, load_unit, load_evidence, production_type, production_type_evidence, labor_mode,
                    labor_hours_per_unit, direct_labor_hours, output_quantity, labor_unit_code, labor_base_quantity,
                    labor_evidence, comparability_explanation
                ) VALUES (
                    {versionId}, {familyId}, {prepared.SalesGrowthPercent}, {StoredJson.Write(prepared.SalesGrowthEvidence)}::jsonb,
                    {prepared.LoadPercent}, {prepared.LoadActual}, {prepared.LoadCapacity}, {prepared.LoadUnit},
                    {StoredJson.Write(prepared.LoadEvidence)}::jsonb, {prepared.ProductionType},
                    {StoredJson.Write(prepared.ProductionTypeEvidence)}::jsonb, {prepared.LaborMode},
                    {prepared.LaborHoursPerUnit}, {prepared.DirectLaborHours}, {prepared.OutputQuantity},
                    {prepared.LaborUnitCode}, {prepared.LaborBaseQuantity}, {StoredJson.Write(prepared.LaborEvidence)}::jsonb,
                    {prepared.ComparabilityExplanation}
                )
                ON CONFLICT (family_id) DO UPDATE SET
                    sales_growth_percent = excluded.sales_growth_percent, sales_growth_evidence = excluded.sales_growth_evidence,
                    load_percent = excluded.load_percent, load_actual = excluded.load_actual, load_capacity = excluded.load_capacity,
                    load_unit = excluded.load_unit, load_evidence = excluded.load_evidence, production_type = excluded.production_type,
                    production_type_evidence = excluded.production_type_evidence, labor_mode = excluded.labor_mode,
                    labor_hours_per_unit = excluded.labor_hours_per_unit, direct_labor_hours = excluded.direct_labor_hours,
                    output_quantity = excluded.output_quantity, labor_unit_code = excluded.labor_unit_code,
                    labor_base_quantity = excluded.labor_base_quantity, labor_evidence = excluded.labor_evidence,
                    comparability_explanation = excluded.comparability_explanation
                """, token);
            return new(AnalysisWriteStatus.Success, prepared);
        }, cancellationToken);

    private Task<bool> ExistsAsync(Guid versionId, Guid familyId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM pilot.family WHERE version_id = {versionId} AND id = {familyId}) AS "Value"
            """).SingleAsync(cancellationToken);

    private async Task<MetricsData?> GetDataAsync(Guid versionId, Guid familyId, CancellationToken cancellationToken)
    {
        var row = await Query(db, versionId).SingleOrDefaultAsync(entry => entry.FamilyId == familyId, cancellationToken);
        return row is null ? null : ToMetrics(row).Data;
    }

    internal static async Task<FamilyMetricsEntry[]> ReadAllAsync(
        AnalysisDbContext db, Guid versionId, CancellationToken cancellationToken)
    {
        var rows = await Query(db, versionId).OrderBy(entry => entry.FamilyId).ToArrayAsync(cancellationToken);
        return rows.Select(ToMetrics).ToArray();
    }

    private static FamilyMetricsEntry ToMetrics(MetricsJson row) =>
        new(row.FamilyId, row.Json is null ? MetricsData.Empty : StoredJson.Read<MetricsData>(row.Json));

    private static IQueryable<MetricsJson> Query(AnalysisDbContext db, Guid versionId) =>
        db.Database.SqlQuery<MetricsJson>($"""
            SELECT f.id AS "FamilyId", CASE WHEN m.family_id IS NOT NULL THEN jsonb_build_object(
                'salesGrowthPercent', m.sales_growth_percent, 'salesGrowthEvidence', m.sales_growth_evidence,
                'loadPercent', m.load_percent, 'loadActual', m.load_actual, 'loadCapacity', m.load_capacity,
                'loadUnit', m.load_unit, 'loadEvidence', m.load_evidence, 'productionType', m.production_type,
                'productionTypeEvidence', m.production_type_evidence, 'laborMode', m.labor_mode,
                'laborHoursPerUnit', m.labor_hours_per_unit, 'directLaborHours', m.direct_labor_hours,
                'outputQuantity', m.output_quantity, 'laborUnitCode', m.labor_unit_code,
                'laborBaseQuantity', m.labor_base_quantity, 'laborEvidence', m.labor_evidence,
                'comparabilityExplanation', m.comparability_explanation
            )::text END AS "Json"
            FROM pilot.family f LEFT JOIN pilot.family_metrics m ON m.family_id = f.id AND m.version_id = f.version_id
            WHERE f.version_id = {versionId}
            """);

    private sealed class MetricsJson
    {
        public Guid FamilyId { get; init; }
        public string? Json { get; init; }
    }
}
