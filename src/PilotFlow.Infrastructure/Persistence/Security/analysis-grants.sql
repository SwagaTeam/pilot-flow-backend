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

GRANT SELECT ON pilot.rule_set, pilot.product_completeness TO pilot_app;
GRANT SELECT, INSERT, DELETE ON pilot.product, pilot.operation, pilot.product_route_step TO pilot_app;
GRANT UPDATE (
    name, unit_code, sales_volume, revenue, raw_material_cost, financial_evidence, calendar_duration_days,
    duration_start, duration_end, includes_waiting, duration_evidence, in_production_plan, plan_evidence,
    is_other, product_code
) ON pilot.product TO pilot_app;
GRANT UPDATE (name, kind, workplace_name, aggregation_coverage_percent, aggregation_evidence)
    ON pilot.operation TO pilot_app;
GRANT SELECT (version_id, product_id, family_id) ON pilot.family_product TO pilot_app;
GRANT SELECT (version_id, operation_id, family_id, workplace_name) ON pilot.family_route_step TO pilot_app;
GRANT SELECT (id, version_id), UPDATE (confirmed) ON pilot.family TO pilot_app;
GRANT SELECT (version_id, family_id), UPDATE (
    sales_growth_percent, sales_growth_evidence, production_type, production_type_evidence,
    load_percent, load_actual, load_capacity, load_unit, load_evidence,
    labor_hours_per_unit, direct_labor_hours, output_quantity, labor_unit_code, labor_base_quantity,
    labor_evidence, comparability_explanation
) ON pilot.family_metrics TO pilot_app;

COMMIT;
