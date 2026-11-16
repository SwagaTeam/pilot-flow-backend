BEGIN;

GRANT USAGE ON SCHEMA pilot TO pilot_app;
GRANT SELECT, INSERT ON pilot.analysis, pilot.analysis_version TO pilot_app;
GRANT UPDATE (
    title, enterprise_name, region, activity, project_start_date,
    financial_start, financial_end, labor_start, labor_end,
    activities_start, activities_end, forecast_start, forecast_end,
    total_revenue, currency, accounting_basis, revenue_evidence, remainder_explanation,
    nomenclature_complete, completeness_evidence, participants, enterprise_inn,
    family_conclusions, ranking_conclusions, revision
) ON pilot.analysis_version TO pilot_app;
GRANT SELECT (version_id), DELETE ON pilot.selection TO pilot_app;
GRANT SELECT (version_id) ON pilot.decision TO pilot_app;

COMMIT;
