-- PostgreSQL. Initial migration; execute once in an empty database.
-- C# Guid -> uuid; decimal -> numeric(24,6); DateOnly -> date;
-- DateTimeOffset UTC -> timestamptz. No float/double for business values.
BEGIN;
CREATE SCHEMA pilot;
SET search_path = pilot, public;

CREATE TABLE rule_set (
 version varchar(100) PRIMARY KEY,
 status varchar(20) NOT NULL CHECK(status IN ('proposed','approved')),
 specification jsonb NOT NULL CHECK(jsonb_typeof(specification)='object')
);
INSERT INTO rule_set VALUES ('fck-2026-tz-v1','proposed',
 '{"source":"МР-11-2026 / ТЗ §§5,8","revenueMinimumPercent":10,"durationMaximumDays":30,"rankMethod":"dense","comparisonPool":"admitted","marginMaximumScore":3,"zeroMarginScoreAfterMaximum":1.5,"growth10Score":1,"load50Score":2,"negativeGrowth":"unresolved","negativeMaximumMargin":"unresolved","incompleteRanking":"provisional","cycleDifference":"(max-min)/min*100; min=0 => unknown"}');

CREATE TABLE analysis (
 id uuid PRIMARY KEY,
 owner_subject varchar(300) NOT NULL CHECK(length(trim(owner_subject))>0),
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_analysis_owner ON analysis(owner_subject,created_at,id);

CREATE TABLE analysis_version (
 id uuid PRIMARY KEY,
 analysis_id uuid NOT NULL REFERENCES analysis(id),
 version_number integer NOT NULL CHECK(version_number>0),
 revision bigint NOT NULL DEFAULT 1 CHECK(revision>0),
 state varchar(10) NOT NULL DEFAULT 'draft' CHECK(state IN ('draft','fixed')),
 rules_version varchar(100) NOT NULL REFERENCES rule_set(version),
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 fork_reason text,
 title varchar(300) NOT NULL CHECK(length(trim(title))>0),
 enterprise_name varchar(300), enterprise_inn varchar(12) CHECK(enterprise_inn ~ '^([0-9]{10}|[0-9]{12})$'),
 region varchar(200), activity varchar(500), family_conclusions text, ranking_conclusions text,
 project_start_date date,
 financial_start date, financial_end date,
 labor_start date, labor_end date,
 activities_start date, activities_end date,
 forecast_start date, forecast_end date,
 total_revenue numeric(24,6) CHECK(total_revenue>=0),
 currency char(3) NOT NULL DEFAULT 'RUB' CHECK(currency='RUB'),
 accounting_basis text,
 revenue_evidence jsonb NOT NULL DEFAULT '{"source":null,"basis":null,"period":null,"assessedOn":null,"missingReason":null}',
 remainder_explanation text,
 nomenclature_complete boolean NOT NULL DEFAULT false,
 completeness_evidence jsonb NOT NULL DEFAULT '{"source":null,"basis":null,"period":null,"assessedOn":null,"missingReason":null}',
 participants jsonb NOT NULL DEFAULT '[]' CHECK(jsonb_typeof(participants)='array'),
 UNIQUE(analysis_id,version_number), UNIQUE(analysis_id,id),
 CHECK ((financial_start IS NULL)=(financial_end IS NULL)), CHECK(financial_start<=financial_end),
 CHECK ((labor_start IS NULL)=(labor_end IS NULL)), CHECK(labor_start<=labor_end),
 CHECK ((activities_start IS NULL)=(activities_end IS NULL)), CHECK(activities_start<=activities_end),
 CHECK ((forecast_start IS NULL)=(forecast_end IS NULL)), CHECK(forecast_start<=forecast_end)
);
CREATE UNIQUE INDEX ux_one_draft ON analysis_version(analysis_id) WHERE state='draft';

CREATE TABLE product (
 id uuid PRIMARY KEY,
 version_id uuid NOT NULL REFERENCES analysis_version(id),
 name varchar(300) NOT NULL CHECK(length(trim(name))>0),
 product_code varchar(100),
 unit_code varchar(30) NOT NULL CHECK(length(trim(unit_code))>0),
 sales_volume numeric(24,6) CHECK(sales_volume>=0),
 revenue numeric(24,6) CHECK(revenue>=0),
 raw_material_cost numeric(24,6) CHECK(raw_material_cost>=0),
 financial_evidence jsonb NOT NULL,
 calendar_duration_days numeric(24,6) CHECK(calendar_duration_days>=0),
 duration_start varchar(300), duration_end varchar(300), includes_waiting boolean,
 duration_evidence jsonb NOT NULL,
 in_production_plan boolean,
 plan_evidence jsonb NOT NULL,
 is_other boolean NOT NULL DEFAULT false,
 UNIQUE(version_id,id)
);
CREATE INDEX ix_product_list ON product(version_id,name,id);

CREATE TABLE operation (
 id uuid PRIMARY KEY,
 version_id uuid NOT NULL REFERENCES analysis_version(id),
 name varchar(300) NOT NULL CHECK(length(trim(name))>0),
 kind varchar(20) NOT NULL CHECK(kind IN ('operation','equipment','workplace','section')),
 workplace_name varchar(300),
 aggregation_coverage_percent numeric(24,6) CHECK(aggregation_coverage_percent BETWEEN 0 AND 100),
 aggregation_evidence jsonb NOT NULL,
 UNIQUE(version_id,id)
);
CREATE INDEX ix_operation_list ON operation(version_id,name,id);

CREATE TABLE product_route_step (
 occurrence_id uuid PRIMARY KEY,
 version_id uuid NOT NULL REFERENCES analysis_version(id),
 product_id uuid NOT NULL,
 operation_id uuid NOT NULL,
 position integer NOT NULL CHECK(position>0),
 duration_hours numeric(24,6) CHECK(duration_hours>=0),
 workplace_name varchar(300),
 evidence jsonb NOT NULL,
 FOREIGN KEY(version_id,product_id) REFERENCES product(version_id,id) ON DELETE CASCADE,
 FOREIGN KEY(version_id,operation_id) REFERENCES operation(version_id,id),
 UNIQUE(product_id,position) DEFERRABLE INITIALLY DEFERRED
);
CREATE INDEX ix_product_route_operation ON product_route_step(version_id,operation_id);

CREATE TABLE family (
 id uuid PRIMARY KEY,
 version_id uuid NOT NULL REFERENCES analysis_version(id),
 name varchar(300) NOT NULL CHECK(length(trim(name))>0),
 start_boundary varchar(500),end_boundary varchar(500),composition_reason text,
 confirmed boolean NOT NULL DEFAULT false,
 confirmation_evidence jsonb NOT NULL,
 UNIQUE(version_id,id)
);
CREATE INDEX ix_family_list ON family(version_id,name,id);
CREATE TABLE family_product (
 version_id uuid NOT NULL REFERENCES analysis_version(id),
 product_id uuid PRIMARY KEY,
 family_id uuid NOT NULL,
 FOREIGN KEY(version_id,product_id) REFERENCES product(version_id,id) ON DELETE CASCADE,
 FOREIGN KEY(version_id,family_id) REFERENCES family(version_id,id)
);
CREATE INDEX ix_family_members ON family_product(version_id,family_id);
CREATE TABLE family_route_step (
 occurrence_id uuid PRIMARY KEY,
 version_id uuid NOT NULL REFERENCES analysis_version(id),
 family_id uuid NOT NULL,
 operation_id uuid NOT NULL,
 position integer NOT NULL CHECK(position>0),
 duration_hours numeric(24,6) CHECK(duration_hours>=0),
 workplace_name varchar(300),
 evidence jsonb NOT NULL,
 FOREIGN KEY(version_id,family_id) REFERENCES family(version_id,id) ON DELETE CASCADE,
 FOREIGN KEY(version_id,operation_id) REFERENCES operation(version_id,id),
 UNIQUE(family_id,position) DEFERRABLE INITIALLY DEFERRED
);
CREATE INDEX ix_family_route_operation ON family_route_step(version_id,operation_id);

CREATE TABLE family_metrics (
 family_id uuid PRIMARY KEY,
 version_id uuid NOT NULL REFERENCES analysis_version(id),
 sales_growth_percent numeric(24,6), sales_growth_evidence jsonb NOT NULL,
 load_percent numeric(24,6) CHECK(load_percent>=0),
 load_actual numeric(24,6) CHECK(load_actual>=0),
 load_capacity numeric(24,6) CHECK(load_capacity>0), load_unit varchar(50),load_evidence jsonb NOT NULL,
 production_type varchar(20) CHECK(production_type IN ('largeSeries','mediumSeries','smallSeries','project')),
 production_type_evidence jsonb NOT NULL,
 labor_mode varchar(20) NOT NULL CHECK(labor_mode IN ('manual','calculated')),
 labor_hours_per_unit numeric(24,6) CHECK(labor_hours_per_unit>=0),
 direct_labor_hours numeric(24,6) CHECK(direct_labor_hours>=0),
 output_quantity numeric(24,6) CHECK(output_quantity>0),
 labor_unit_code varchar(30),labor_base_quantity numeric(24,6) CHECK(labor_base_quantity>0),
 labor_evidence jsonb NOT NULL,comparability_explanation text,
 FOREIGN KEY(version_id,family_id) REFERENCES family(version_id,id) ON DELETE CASCADE
);

CREATE TABLE protocol (
 version_id uuid PRIMARY KEY REFERENCES analysis_version(id),
 protocol_number varchar(100),agreement_number varchar(100),agreement_date date,
 enterprise_party varchar(500),partner_party varchar(500),signing_place varchar(300),protocol_date date,
 prepared_by_name varchar(300),prepared_by_position varchar(300),
 approved_by_name varchar(300),approved_by_position varchar(300),conclusions text
);
CREATE TABLE selection (
 version_id uuid PRIMARY KEY REFERENCES analysis_version(id),
 family_id uuid NOT NULL,
 mode varchar(20) NOT NULL CHECK(mode IN ('ranked','monoproduct','dominantProduct')),
 dominant_product_id uuid,
 justification text,direct_basis_evidence jsonb NOT NULL,manager_decision_reference text,
 FOREIGN KEY(version_id,family_id) REFERENCES family(version_id,id),
 FOREIGN KEY(version_id,dominant_product_id) REFERENCES product(version_id,id),
 CHECK ((mode='dominantProduct')=(dominant_product_id IS NOT NULL))
);

-- Full typed snapshot shape is defined by OpenAPI Snapshot, not an unstructured business-data store.
CREATE TABLE decision (
 id uuid PRIMARY KEY,
 version_id uuid NOT NULL UNIQUE REFERENCES analysis_version(id),
 analysis_id uuid NOT NULL,
 family_id uuid NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),
 created_by_subject varchar(300) NOT NULL,
 created_by_name varchar(300) NOT NULL,
 snapshot_schema_version varchar(10) NOT NULL CHECK(snapshot_schema_version='1.0'),
 rules_version varchar(100) NOT NULL REFERENCES rule_set(version),
 snapshot jsonb NOT NULL CHECK(jsonb_typeof(snapshot)='object'),
 snapshot_sha256 char(64) NOT NULL CHECK(snapshot_sha256 ~ '^[a-f0-9]{64}$'),
 FOREIGN KEY(analysis_id,version_id) REFERENCES analysis_version(analysis_id,id),
 FOREIGN KEY(version_id,family_id) REFERENCES family(version_id,id)
);
CREATE INDEX ix_decision_analysis ON decision(analysis_id,created_at,id);

-- Database defense: fixed input and immutable records cannot be changed accidentally.
CREATE FUNCTION deny_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'immutable_record' USING ERRCODE='23514'; END $$;
CREATE TRIGGER immutable_decision BEFORE UPDATE OR DELETE ON decision FOR EACH ROW EXECUTE FUNCTION deny_change();
CREATE TRIGGER immutable_rule_set BEFORE UPDATE OR DELETE ON rule_set FOR EACH ROW EXECUTE FUNCTION deny_change();

CREATE FUNCTION guard_version() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'version_delete_forbidden' USING ERRCODE='23514'; END IF;
 IF OLD.state='fixed' THEN RAISE EXCEPTION 'version_fixed' USING ERRCODE='23514'; END IF;
 IF NEW.id<>OLD.id OR NEW.analysis_id<>OLD.analysis_id OR NEW.version_number<>OLD.version_number OR NEW.rules_version<>OLD.rules_version THEN
  RAISE EXCEPTION 'version_identity_immutable' USING ERRCODE='23514';
 END IF;
 IF NEW.revision<>OLD.revision+1 THEN RAISE EXCEPTION 'revision_must_increment' USING ERRCODE='23514'; END IF;
 NEW.updated_at:=now();
 RETURN NEW;
END $$;
CREATE TRIGGER version_guard BEFORE UPDATE OR DELETE ON analysis_version FOR EACH ROW EXECUTE FUNCTION guard_version();

CREATE FUNCTION guard_child() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v uuid; s text;
BEGIN
 IF TG_OP='UPDATE' AND NEW.version_id<>OLD.version_id THEN RAISE EXCEPTION 'version_identity_immutable' USING ERRCODE='23514'; END IF;
 IF TG_OP='DELETE' THEN v:=OLD.version_id; ELSE v:=NEW.version_id; END IF;
 SELECT state INTO s FROM analysis_version WHERE id=v FOR UPDATE;
 IF s IS DISTINCT FROM 'draft' THEN RAISE EXCEPTION 'version_not_draft' USING ERRCODE='23514'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END $$;
DO $$ DECLARE t text; BEGIN
 FOREACH t IN ARRAY ARRAY['product','operation','product_route_step','family','family_product','family_route_step','family_metrics','protocol','selection'] LOOP
  EXECUTE format('CREATE TRIGGER child_guard BEFORE INSERT OR UPDATE OR DELETE ON pilot.%I FOR EACH ROW EXECUTE FUNCTION pilot.guard_child()',t);
 END LOOP;
END $$;

-- Deferred invariant permits insert decision then set state=fixed in the same transaction.
CREATE FUNCTION check_fixed_decision() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v uuid; s text; n integer;
BEGIN
 IF TG_TABLE_NAME='decision' THEN v:=NEW.version_id; ELSE v:=NEW.id; END IF;
 SELECT state INTO s FROM analysis_version WHERE id=v;
 SELECT count(*) INTO n FROM decision WHERE version_id=v;
 IF (s='fixed' AND n<>1) OR (s='draft' AND n<>0) THEN RAISE EXCEPTION 'fixed_requires_exactly_one_decision' USING ERRCODE='23514'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER fixed_has_decision AFTER INSERT OR UPDATE ON analysis_version DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_fixed_decision();
CREATE CONSTRAINT TRIGGER decision_has_fixed AFTER INSERT ON decision DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_fixed_decision();

-- Views preserve null: SUM alone would hide missing cost/revenue values.
CREATE VIEW family_financials AS
SELECT f.version_id,f.id AS family_id,count(p.id) AS product_count,
 CASE WHEN count(p.id)>0 AND count(p.revenue)=count(p.id) THEN sum(p.revenue) END AS revenue,
 CASE WHEN count(p.id)>0 AND count(p.raw_material_cost)=count(p.id) THEN sum(p.raw_material_cost) END AS raw_material_cost,
 CASE WHEN count(p.id)>0 AND count(p.revenue)=count(p.id) AND count(p.raw_material_cost)=count(p.id) THEN sum(p.revenue)-sum(p.raw_material_cost) END AS margin_income,
 max(p.calendar_duration_days) AS known_max_calendar_duration_days,
 count(p.calendar_duration_days)=count(p.id) AND count(p.id)>0 AS duration_complete,
 bool_or(p.calendar_duration_days>30) AS has_duration_violation,
 bool_or(p.in_production_plan=false) AS has_plan_violation,
 count(p.in_production_plan)=count(p.id) AND count(p.id)>0 AS plan_complete
FROM family f LEFT JOIN family_product fp ON fp.family_id=f.id
LEFT JOIN product p ON p.id=fp.product_id GROUP BY f.version_id,f.id;
COMMIT;

-- REQUIRED application transaction for every mutation:
-- BEGIN; SET LOCAL search_path=pilot,public;
-- UPDATE analysis_version SET revision=revision+1 [,...] WHERE id=@versionId
-- AND analysis_id=@analysisId AND revision=@expectedRevision AND state='draft'
-- AND EXISTS(SELECT 1 FROM analysis a WHERE a.id=analysis_id AND a.owner_subject=@sub)
-- RETURNING revision; -- no row => distinguish 404 / 409 / 412; rollback.
-- For input/membership/route/metrics mutation: DELETE selection for version first.
-- Reset affected family.confirmed on route/membership changes; execute mutation; COMMIT.
-- All child writes must go through that transaction; database triggers do not perform CAS.
-- App role must not own schema, be superuser, or have TRUNCATE/DDL privileges.
-- Set search_path=pilot,public for the application connection; use EF schema pilot.
