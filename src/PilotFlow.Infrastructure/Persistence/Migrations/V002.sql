CREATE VIEW pilot.product_completeness WITH (security_invoker = true) AS
SELECT p.id AS product_id, p.version_id,
    ARRAY(
        SELECT field.name
        FROM (VALUES
            (1, 'salesVolume', false, p.sales_volume IS NULL, NULL::jsonb),
            (2, 'revenue', false, p.revenue IS NULL, NULL),
            (3, 'rawMaterialCost', false, p.raw_material_cost IS NULL, NULL),
            (4, 'financialEvidence', false, false, p.financial_evidence),
            (5, 'calendarDurationDays', true, p.calendar_duration_days IS NULL, NULL),
            (6, 'durationEvidence', true, false, p.duration_evidence),
            (7, 'inProductionPlan', true, p.in_production_plan IS NULL, NULL),
            (8, 'planEvidence', true, false, p.plan_evidence)
        ) AS field(ordinal, name, production, missing_value, evidence)
        WHERE (NOT field.production OR NOT p.is_other)
          AND (field.missing_value OR (
            field.evidence IS NOT NULL AND (
                jsonb_typeof(field.evidence->'source') = 'string'
                AND field.evidence->>'source' ~ '[^[:space:]]'
                AND (
                    field.evidence->>'assessedOn' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'
                    OR (
                        field.evidence->'period'->>'start' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'
                        AND field.evidence->'period'->>'end' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'
                    )
                )
            ) IS NOT TRUE
          ))
        ORDER BY field.ordinal
    ) AS missing
FROM pilot.product p;
