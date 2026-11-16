# Analysis Persistence Implementation Plan

> **For agentic workers:** Verify observable behavior before and after implementation with superpowers:test-driven-development.

**Goal:** Store and retrieve analyses and their versions for the authenticated owner. Replace draft data using an atomic If-Match check.

**Architecture:** Controllers delegate to the Application use case. Infrastructure maps the existing PostgreSQL schema with EF Core and uses explicit SQL for compare-and-swap. Keep the remaining contract operations on their existing scaffold.

**Tech Stack:** ASP.NET Core 10, EF Core, Npgsql, PostgreSQL 18, xUnit, and Testcontainers.

**Spec:** `contracts/openapi.json`, the PostgreSQL schema, and stages 2–3 of the approved foundation design.

## Global Constraints

Preserve operation IDs and types. The JWT subject is the only source of ownership. Keep incomplete drafts, nulls, and decimal strings. Keep database credentials outside Git and run tests only in CI.

## Review Focus

- Foreign and mismatched analysis/version IDs produce the same 404 as missing data.
- Two writes with one ETag yield one success and one 412. Successful writes clear any saved selection.
- A fixed version cannot be changed. If-Match is checked before binding the body.
- Empty database configuration is allowed for the development Swagger shell and required in Production.

### Task 1: HTTP and storage

- [x] Add PostgreSQL-backed HTTP tests for create/read/search, ownership, pagination, invalid data, revision conflict and persisted update.
- [x] Observe scaffold failures.
- [x] Add domain data and validation, Application port, EF mappings, transaction and CAS, controller routes, and contract error handling.
- [x] Run the HTTP, authorization and parity tests.

### Task 2: Configure and verify

- [x] Add PostgreSQL readiness and documented Docker configuration with a restricted runtime role.
- [x] Verify a fresh PostgreSQL container, Docker API and the Release suite.
