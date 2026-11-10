# PostgreSQL Migrations Implementation Plan

> **For agentic workers:** Use superpowers:test-driven-development to implement and verify each task.

**Goal:** Start a fresh PostgreSQL database, apply the existing schema with versioned history, and verify the result against a real PostgreSQL server.

**Architecture:** A separate console project calls the Infrastructure migrator. The migrator applies packaged SQL under a PostgreSQL advisory lock, with schema changes and SHA-256 history in the same transaction.

**Tech Stack:** .NET 10, Npgsql 10, PostgreSQL 18.7, Docker Compose, xUnit and Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-22-backend-foundation-design.md`, stage 2, database and migrations.

## Global Constraints

Use schema `pilot`, preserve the contract DDL, and keep PostgreSQL credentials outside Git. Build on `main`. CI runs tests.

## Review Focus

- An existing database with different migration content must stop rather than update its history.
- Concurrent runners against an empty database must record V001 once.
- A DDL failure must not create a successful migration record.
- Later callers must be able to use fully qualified table names without breaking trigger lookup.

### Task 1: Versioned migrations

- [x] Add `PilotFlow.IntegrationTests` and test that two concurrent calls to `PostgresMigrator.ApplyAsync(NpgsqlDataSource, CancellationToken)` install the 13 domain tables and store one SHA-256 in `pilot_migrations.history`. Test a checksum mismatch and a failed installation transaction.
- [x] Run the integration tests and verify that they fail without the migrator.
- [x] Embed `src/PilotFlow.Infrastructure/Persistence/Migrations/V001.sql`. Preserve the contract schema and transfer transaction ownership to the migrator. Fix the two trigger functions' search path so they work with qualified queries.
- [x] Implement the migrator with an advisory transaction lock, ordered versions, prefix and checksum validation, and transactional application.
- [x] Run the 19 migration and schema scenarios listed in `contracts/reference/validation-report.json` on PostgreSQL.
- [x] Run the PostgreSQL integration tests and the Release suite.

### Task 2: Run from Docker

- [x] Add `src/PilotFlow.DbMigrator`, a Docker target, and opt-in Compose services for PostgreSQL and an explicit migration run. Keep direct CLI execution available through `PILOTFLOW_MIGRATION_CONNECTION_STRING`.
- [x] Verify a fresh Compose database, a second migration run, and the API health and Swagger endpoints.
- [x] Document the commands and record the team's HTTP controller choice. Run formatting, build and tests before committing.
