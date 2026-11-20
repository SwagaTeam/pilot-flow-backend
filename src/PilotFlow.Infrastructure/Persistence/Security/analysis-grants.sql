BEGIN;

GRANT USAGE ON SCHEMA pilot TO pilot_app;
GRANT SELECT, INSERT ON pilot.analysis, pilot.analysis_version TO pilot_app;
GRANT UPDATE (
    title, enterprise_name, region, activity, project_start_date,
    financial_start, financial_end, labor_start, labor_end,
    activities_start, activities_end, forecast_start, forecast_end,
    total_revenue, currency, accounting_basis, revenue_evidence, remainder_explanation,
    nomenclature_complete, completeness_evidence, participants, enterprise_inn,
    family_conclusions, ranking_conclusions, revision, state
) ON pilot.analysis_version TO pilot_app;
GRANT SELECT, INSERT, DELETE ON pilot.selection TO pilot_app;
GRANT UPDATE (family_id, mode, dominant_product_id, justification, direct_basis_evidence, manager_decision_reference)
    ON pilot.selection TO pilot_app;
GRANT SELECT, INSERT ON pilot.decision, pilot.protocol TO pilot_app;
GRANT UPDATE (
    protocol_number, agreement_number, agreement_date, enterprise_party, partner_party, signing_place,
    protocol_date, prepared_by_name, prepared_by_position, approved_by_name, approved_by_position, conclusions
) ON pilot.protocol TO pilot_app;

GRANT SELECT ON pilot.rule_set, pilot.product_completeness TO pilot_app;
GRANT SELECT, INSERT, DELETE ON pilot.product, pilot.operation, pilot.product_route_step TO pilot_app;
GRANT UPDATE (
    name, unit_code, sales_volume, revenue, raw_material_cost, financial_evidence, calendar_duration_days,
    duration_start, duration_end, includes_waiting, duration_evidence, in_production_plan, plan_evidence,
    is_other, product_code
) ON pilot.product TO pilot_app;
GRANT UPDATE (name, kind, workplace_name, aggregation_coverage_percent, aggregation_evidence)
    ON pilot.operation TO pilot_app;
GRANT SELECT, INSERT, DELETE ON pilot.family, pilot.family_product, pilot.family_route_step TO pilot_app;
GRANT UPDATE (name, start_boundary, end_boundary, composition_reason, confirmed, confirmation_evidence)
    ON pilot.family TO pilot_app;
GRANT SELECT, INSERT, UPDATE (
    sales_growth_percent, sales_growth_evidence, production_type, production_type_evidence,
    load_percent, load_actual, load_capacity, load_unit, load_evidence,
    labor_hours_per_unit, direct_labor_hours, output_quantity, labor_unit_code, labor_base_quantity,
    labor_evidence, comparability_explanation, labor_mode
) ON pilot.family_metrics TO pilot_app;

COMMIT;
