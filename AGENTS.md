# Pilot Flow C# engineering profile

These instructions apply to the entire repository.

## Working agreement

- Be honest about incomplete behavior. An unimplemented contract operation returns the documented `NOT_IMPLEMENTED` problem; it never returns invented success data.
- Challenge requirements that conflict with the OpenAPI contract, database invariants, or calculation rules. Record the resolution before coding.
- Keep all work in `main` and separate concerns by `contracts/`, `src/`, `tests/`, and `docs/`.
- Prefer the smallest complete change. Do not add extension points, dependencies, or abstractions for hypothetical future needs.

## C# and .NET

- Target .NET 10 with nullable reference types enabled, implicit usings enabled, deterministic builds, and warnings treated as errors.
- Use current C# language features only when they make the code clearer. Prefer explicit domain names over clever syntax.
- Use `decimal` for money, percentages, durations, scores, and quantities; never use `float` or `double` for business values.
- Use `DateOnly` for business dates, `DateTimeOffset` in UTC for instants, `Guid` for identifiers, and `CancellationToken` as the final parameter of async methods.
- Keep async end to end. Do not use `.Result`, `.Wait()`, `Task.Run` around I/O, `async void`, or fire-and-forget work.
- Make domain nullability explicit. `null` means unknown or absent and must not be converted to zero, false, or an empty collection without a domain rule.
- Expected validation and state failures use typed results; exceptions are for unexpected failures and infrastructure boundaries.

## Architecture

- Keep Minimal API endpoints thin: bind and validate transport input, call one application handler, map the result.
- Endpoint code must not access EF Core, Npgsql, files, clocks, hashes, or PDF libraries directly.
- `Domain` has no dependency on ASP.NET Core, EF Core, Npgsql, configuration, logging, or system time.
- `Application` defines use cases and ports. `Infrastructure` implements persistence and external concerns. `Api` references `Infrastructure` only from the composition root.
- Do not create a generic repository. Use focused query/command ports whose names describe the use case.
- Do not add MediatR or another dispatcher unless repeated concrete duplication proves it is needed.

## ASP.NET Core contract

- Treat `contracts/openapi.yaml` and `contracts/openapi.json` as the authoritative public contract.
- Preserve route, method, operationId, status code, content type, enum spelling, nullable semantics, and decimal-string representation.
- Return errors as `application/problem+json` with stable `code`, `traceId`, and `issues`.
- Require authenticated ownership for analysis resources. Mask missing and foreign resources with the same `404` behavior.
- Parse ETags strictly. A missing required `If-Match` is `428`; a stale revision is `412`; never retry a stale mutation automatically.

## PostgreSQL and EF Core

- Use PostgreSQL schema `pilot`, snake_case database names, explicit constraints, and parameterized SQL.
- Use EF Core for mapping and ordinary queries. Use explicit SQL for CAS updates, `FOR UPDATE`, finalize, and fork when statement ordering is an invariant.
- Disable lazy loading. Use `AsNoTracking` for read-only queries and avoid N+1 query patterns.
- Every mutation runs in one explicit transaction and advances aggregate revision exactly once.
- Migration history includes the migration checksum. Never edit an applied migration; add a new one.

## Tests and verification

- Follow red-green-refactor for every behavior change: write one failing test, verify the expected failure, implement the minimum, then run the focused test and the full suite.
- Prefer tests of observable behavior over mocks. Mock only true external boundaries.
- Name tests `Method_Scenario_ExpectedResult` or an equally explicit behavior sentence.
- Contract tests compare registered endpoints with OpenAPI. Domain tests cover boundary values with exact decimals. Integration tests use a real PostgreSQL container.
- A completed change has no build warnings and passes formatting, build, focused tests, and the full test suite.

## Security and operations

- Do not commit secrets, development passwords, tokens, connection strings, or personal data.
- Validate configuration at startup and fail closed when JWT settings or production dependencies are missing.
- Log identifiers and stable error codes, not financial payloads, bearer tokens, snapshots, or PDF contents.
- Health checks are operational endpoints and must not expose secrets or database details.
