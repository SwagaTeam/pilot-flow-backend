# Version Catalogs Implementation Plan

> **For agentic workers:** Verify HTTP and PostgreSQL behavior with superpowers:test-driven-development.

**Goal:** Implement identity and rules, the product and operation catalogs, and ordered product routes. A draft can be created and filled through the published API.

**Architecture:** Use controllers by resource, Application ports and EF Core mappings. Read payload and ETag in one PostgreSQL snapshot. Use the version row as the write lock and advance its revision once in successful mutations.

**Spec:** `contracts/openapi.json`, `contracts/schema.sql`, and the rules in `contracts/reference/handoff.html`.

## Review focus

- Lists calculate counts before paging, search by literal text, and derive product completeness from the same fields used to filter.
- Each read and write masks a missing or foreign aggregate with the same 404. Foreign operation references are invalid inputs, like missing operation references.
- Valid drafts can still contain unknown evidence or incomplete production data. Section coverage below 80 percent can be recorded as draft data. It does not qualify a section for later route confirmation.
- Failed validation, a stale ETag, and an in-use operation do not advance the version or clear the selection.
- Route replacement preserves occurrence IDs and checks all inputs before committing. Changing a product route resets confirmation and workload metrics of its assigned family.

### Tasks

- [x] Write HTTP integration tests for catalogs and identity, observe the existing 501s, and verify the new successful paths.
- [x] Add typed catalog models and input validation, EF stores and HTTP controllers. Add a versioned view for product completeness and grant only the runtime permissions the operations need.
- [x] Verify route rollback, family invalidation, restricted database permissions, contract parity and the full Release suite.
- [x] Apply the migration and updated grants to the development Docker stack, rebuild the API and verify readiness.
