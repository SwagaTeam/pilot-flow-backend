# Pilot Flow HTTP API Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Создать запускаемый .NET 10 HTTP-каркас, который регистрирует все 45 операций OpenAPI, честно отвечает `NOT_IMPLEMENTED`, защищает предметные маршруты Bearer-аутентификацией и автоматически доказывает совпадение маршрутов с контрактом.

**Architecture:** ASP.NET Core Minimal API использует статический каталог endpoint descriptors, разделённый по предметным группам. Пока application handlers не реализованы, каждый зарегистрированный endpoint проходит общие HTTP-политики и возвращает единый `501 application/problem+json`; контрактные тесты сравнивают runtime EndpointDataSource с авторитетным `contracts/openapi.json`.

**Tech Stack:** .NET 10 LTS, C# 14, ASP.NET Core Minimal APIs, JWT Bearer, xUnit, Microsoft.AspNetCore.Mvc.Testing, встроенные .NET analyzers.

**Spec:** `docs/superpowers/specs/2026-11-22-backend-foundation-design.md`

## Global Constraints

- Вся работа остаётся в ветке `main` и разделяется каталогами `contracts/`, `src/`, `tests/`, `docs/`.
- `contracts/openapi.yaml` и `contracts/openapi.json` являются авторитетным публичным контрактом.
- Target framework — `net10.0`; nullable reference types, implicit usings, deterministic build и warnings-as-errors обязательны.
- Незавершённая операция возвращает `501 application/problem+json` с `code=NOT_IMPLEMENTED`; успешные фиктивные ответы запрещены.
- Предметные endpoints требуют authenticated identity; тестовая authentication scheme существует только в test host.
- Не добавлять MediatR, FluentAssertions, AutoMapper, generic repository, database packages или PDF packages на этапе каркаса.
- Производственный код появляется только после теста, который упал по ожидаемой причине; после каждого цикла выполняются focused test и полная suite.
- Существующие OpenAPI, schema, fixtures, handoff и Swagger assets не удаляются и не изменяют смысл при переносе.

## Review Focus

- OpenAPI-операция с дублирующимся `method + path` должна провалить contract test, а не скрыть одну регистрацию.
- Предметный endpoint без identity должен вернуть `401`, но неизвестный URL должен остаться обычным `404`.
- Аутентифицированный вызов реально существующего stub endpoint должен вернуть `501`, а не `404`, `200` или пустой ответ.
- Отсутствующий `If-Match` у version mutation должен вернуть `428`; malformed ETag — `400`; корректный ETag проходит к `501`.
- Problem Details должен сохранять `application/problem+json`, стабильный `code`, непустой `traceId` и массив `issues` при любых Accept headers.

---

### Task 1: Repository contract layout

**Files:**
- Create: `contracts/`
- Create: `contracts/reference/`
- Move: `openapi.yaml` -> `contracts/openapi.yaml`
- Move: `openapi.json` -> `contracts/openapi.json`
- Move: `schema.sql` -> `contracts/schema.sql`
- Move: `acceptance-vectors.json` -> `contracts/acceptance-vectors.json`
- Move: `control-example.json` -> `contracts/control-example.json`
- Move: `handoff.html`, `swagger.html`, `swagger-ui.css`, `swagger-ui-bundle.js`, `database-map.svg`, `figma-api-map.csv`, `validation-report.json`, `source-audit.json`, `pilot-flow-contract-v1.zip`, `mock-server.mjs`, `START-HERE.txt`, `LICENSE`, `NOTICE` -> `contracts/reference/`
- Modify: `README.md`
- Modify: `contracts/reference/mock-server.mjs`
- Test: command-line smoke checks in this task

**Interfaces:**
- Consumes: current root handoff package.
- Produces: stable paths `contracts/openapi.json`, `contracts/openapi.yaml`, `contracts/schema.sql`, `contracts/acceptance-vectors.json`, `contracts/control-example.json` used by every later task.

- [ ] **Step 1: Capture the current contract hashes and mock behavior**

Run:

```powershell
Get-FileHash openapi.yaml,openapi.json,schema.sql,acceptance-vectors.json,control-example.json -Algorithm SHA256
node mock-server.mjs
```

In a second terminal run `Invoke-WebRequest http://127.0.0.1:5080/openapi.json` and `Invoke-WebRequest http://127.0.0.1:5080/api/v1/me`; record status `200` for both, then stop the mock.

- [ ] **Step 2: Move files with Git-aware moves**

Use `git mv` for the exact paths in the Files block. Do not unpack or regenerate `pilot-flow-contract-v1.zip`.

- [ ] **Step 3: Update mock path resolution**

In `contracts/reference/mock-server.mjs`, resolve contract and fixture files from `..`, while Swagger assets and reference reports remain in the script directory. Preserve the existing HTTP behavior and port.

- [ ] **Step 4: Update repository entrypoints**

Update `README.md` and `contracts/reference/START-HERE.txt` so every local path and launch command uses the new directories, including:

```powershell
node contracts/reference/mock-server.mjs
psql -v ON_ERROR_STOP=1 -d pilot_db -f contracts/schema.sql
```

- [ ] **Step 5: Verify the move is lossless**

Run the same SHA-256 command against the new five paths. Expected: hashes match Step 1. Start the moved mock and repeat both HTTP checks; expected status remains `200`.

- [ ] **Step 6: Commit**

```powershell
git add contracts README.md
git commit -m "chore: organize backend contract assets"
```

### Task 2: .NET solution and project boundaries

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `PilotFlow.Backend.slnx`
- Create: `src/PilotFlow.Domain/PilotFlow.Domain.csproj`
- Create: `src/PilotFlow.Application/PilotFlow.Application.csproj`
- Create: `src/PilotFlow.Infrastructure/PilotFlow.Infrastructure.csproj`
- Create: `src/PilotFlow.Api/PilotFlow.Api.csproj`
- Create: `tests/PilotFlow.ContractTests/PilotFlow.ContractTests.csproj`
- Create: `tests/PilotFlow.Api.Tests/PilotFlow.Api.Tests.csproj`
- Test: `tests/PilotFlow.ContractTests/Architecture/ProjectReferenceTests.cs`

**Interfaces:**
- Consumes: .NET SDK `10.0.400` installed in the workspace.
- Produces: solution projects and references `Api -> Application`, `Api -> Infrastructure`, `Infrastructure -> Application + Domain`, `Application -> Domain`; `Api.Tests -> Api`; `ContractTests -> Api` for runtime endpoint inspection.

- [ ] **Step 1: Create SDK and shared build configuration**

Set `global.json` to SDK `10.0.400` with `rollForward: latestPatch`. In `Directory.Build.props`, set `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `EnableNETAnalyzers=true`, `AnalysisLevel=latest-recommended`, and deterministic builds.

In `Directory.Packages.props`, enable central package management and pin only `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `coverlet.collector` to mutually compatible .NET 10 versions resolved during implementation.

- [ ] **Step 2: Create solution and empty projects**

Use `dotnet new` templates for class libraries, web, and xUnit projects, add them to `PilotFlow.Backend.slnx`, and delete generated `Class1.cs`/`UnitTest1.cs` placeholders.

- [ ] **Step 3: Write the architecture test**

Create `ProjectReferences_MatchApprovedDependencyGraph` in `ProjectReferenceTests.cs`. Parse each project file with `XDocument` and assert the exact project-reference sets, including that Domain has none and Application references only Domain.

- [ ] **Step 4: Run the test and verify RED**

Run:

```powershell
dotnet test tests/PilotFlow.ContractTests/PilotFlow.ContractTests.csproj --filter ProjectReferences_MatchApprovedDependencyGraph
```

Expected: FAIL before all required references are present or if the graph differs.

- [ ] **Step 5: Add the exact project references**

Add references matching the Interfaces block. Do not add a reverse reference or a reference from Domain to another project.

- [ ] **Step 6: Verify GREEN**

Make only the project-reference changes needed by the test. Re-run the focused test, then:

```powershell
dotnet build PilotFlow.Backend.slnx
dotnet test PilotFlow.Backend.slnx
```

Expected: build and tests pass with zero warnings.

- [ ] **Step 7: Commit**

```powershell
git add global.json Directory.Build.props Directory.Packages.props PilotFlow.Backend.slnx src tests
git commit -m "build: create backend solution boundaries"
```

### Task 3: HTTP host and operational health

**Files:**
- Create: `src/PilotFlow.Api/Program.cs`
- Create: `src/PilotFlow.Api/Hosting/ApiServiceCollectionExtensions.cs`
- Create: `src/PilotFlow.Api/Hosting/EndpointRouteBuilderExtensions.cs`
- Create: `src/PilotFlow.Api/appsettings.json`
- Create: `src/PilotFlow.Api/appsettings.Development.json`
- Create: `tests/PilotFlow.Api.Tests/Infrastructure/PilotFlowWebApplicationFactory.cs`
- Create: `tests/PilotFlow.Api.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Consumes: ASP.NET Core shared framework and solution boundaries from Task 2.
- Produces: `Program` partial type for WebApplicationFactory; extension methods `AddPilotFlowApi(IServiceCollection, IConfiguration, IHostEnvironment)` and `MapPilotFlowApi(IEndpointRouteBuilder)`; `/health/live` and `/health/ready`.

- [ ] **Step 1: Write failing health and unknown-route tests**

Add:

```csharp
public async Task LiveHealth_ReturnsOk()
public async Task ReadyHealth_ReturnsOkBeforeDatabaseStage()
public async Task UnknownRoute_ReturnsNotFound()
```

Assert `200`, `200`, and `404` respectively. The readiness test documents that the scaffold has no database dependency yet.

- [ ] **Step 2: Run tests and verify RED**

Run `dotnet test tests/PilotFlow.Api.Tests/PilotFlow.Api.Tests.csproj --filter HealthEndpointTests`.

Expected: FAIL because the API entry point and health routes do not exist.

- [ ] **Step 3: Implement the minimal host**

Create `Program.cs` with `WebApplication.CreateBuilder`, `AddPilotFlowApi`, `MapPilotFlowApi`, `Run`, and `public partial class Program`. Map only the two health endpoints and leave the fallback behavior to ASP.NET Core.

- [ ] **Step 4: Verify GREEN and full suite**

Run the focused test, `dotnet build PilotFlow.Backend.slnx`, then `dotnet test PilotFlow.Backend.slnx`. Expected: zero warnings and all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/PilotFlow.Api tests/PilotFlow.Api.Tests
git commit -m "feat: add API host health endpoints"
```

### Task 4: Authentication boundary and test identity

**Files:**
- Create: `src/PilotFlow.Api/Authentication/JwtOptions.cs`
- Create: `src/PilotFlow.Api/Authentication/AuthenticationExtensions.cs`
- Create: `src/PilotFlow.Api/Endpoints/ScaffoldProbeEndpoints.cs`
- Create: `tests/PilotFlow.Api.Tests/Infrastructure/TestAuthenticationHandler.cs`
- Modify: `tests/PilotFlow.Api.Tests/Infrastructure/PilotFlowWebApplicationFactory.cs`
- Create: `tests/PilotFlow.Api.Tests/AuthenticationTests.cs`
- Modify: `src/PilotFlow.Api/appsettings.json`

**Interfaces:**
- Consumes: host extensions from Task 3.
- Produces: policy `analysis-user`; production scheme `Bearer`; test-only header `X-Test-Subject` recognized only by the WebApplicationFactory; temporary protected `/api/v1/_scaffold-probe` removed in Task 6.

- [ ] **Step 1: Write authentication tests**

Add:

```csharp
public async Task ProtectedEndpoint_WithoutIdentity_ReturnsUnauthorized()
public async Task ProtectedEndpoint_WithTestIdentity_ReachesEndpoint()
public async Task UnknownEndpoint_WithIdentity_ReturnsNotFound()
```

The protected probe returns `204` only to prove authentication flow. The unknown endpoint remains `404` with or without identity.

- [ ] **Step 2: Run tests and verify RED**

Run `dotnet test tests/PilotFlow.Api.Tests/PilotFlow.Api.Tests.csproj --filter AuthenticationTests`.

Expected: FAIL because no authentication services, policy, test handler, or protected route exist.

- [ ] **Step 3: Implement JWT registration and authorization**

Define `JwtOptions` with `Authority` and `Audience`. `AddPilotFlowAuthentication` registers JWT Bearer and the `analysis-user` policy. Production startup validates non-empty settings; Development may start without them but does not authenticate any request automatically.

- [ ] **Step 4: Implement test-only authentication override**

`TestAuthenticationHandler` creates claims `sub` and `name` from `X-Test-Subject` only inside the test host. The production project contains no development bypass handler.

- [ ] **Step 5: Verify GREEN and full suite**

Run the focused test and `dotnet test PilotFlow.Backend.slnx`. Expected: all pass.

- [ ] **Step 6: Commit**

```powershell
git add src/PilotFlow.Api tests/PilotFlow.Api.Tests
git commit -m "feat: establish API authentication boundary"
```

### Task 5: Stable Problem Details and stub behavior

**Files:**
- Create: `src/PilotFlow.Api/Errors/ApiIssue.cs`
- Create: `src/PilotFlow.Api/Errors/ApiProblem.cs`
- Create: `src/PilotFlow.Api/Errors/ProblemResults.cs`
- Create: `src/PilotFlow.Api/Endpoints/NotImplementedEndpoint.cs`
- Create: `tests/PilotFlow.Api.Tests/NotImplementedEndpointTests.cs`

**Interfaces:**
- Consumes: authenticated test host from Task 4.
- Produces: `ProblemResults.NotImplemented(HttpContext, string operationId) -> IResult`; `NotImplementedEndpoint.Handle(HttpContext, string operationId) -> Task<IResult>`; response shape compatible with OpenAPI Problem schema.

- [ ] **Step 1: Write the failing stub response tests**

Test the temporary protected probe after replacing its successful body with a call to the desired API:

```csharp
public async Task Stub_WithIdentity_ReturnsNotImplementedProblem()
public async Task Stub_WithAnyAcceptHeader_ReturnsProblemJson()
```

Assert status `501`, media type `application/problem+json`, `code == "NOT_IMPLEMENTED"`, non-empty `traceId`, `issues` is an empty array, and `detail` names the operationId without exposing a stack trace.

- [ ] **Step 2: Run tests and verify RED**

Run `dotnet test tests/PilotFlow.Api.Tests/PilotFlow.Api.Tests.csproj --filter NotImplementedEndpointTests`.

Expected: FAIL because the probe still returns `204` and the result types do not exist.

- [ ] **Step 3: Implement the minimum error model and stub**

Use ASP.NET `ProblemDetails` extensions or a small immutable record matching the contract. Do not add exception middleware or a general result framework yet.

- [ ] **Step 4: Verify GREEN and full suite**

Run the focused tests and the full solution suite. Expected: all pass, no warnings.

- [ ] **Step 5: Commit**

```powershell
git add src/PilotFlow.Api tests/PilotFlow.Api.Tests
git commit -m "feat: add explicit not implemented problems"
```

### Task 6: All 45 OpenAPI endpoint registrations

**Files:**
- Create: `src/PilotFlow.Api/Endpoints/EndpointDescriptor.cs`
- Create: `src/PilotFlow.Api/Endpoints/EndpointCatalog.cs`
- Create: `src/PilotFlow.Api/Endpoints/Catalog/IdentityRulesCatalog.cs`
- Create: `src/PilotFlow.Api/Endpoints/Catalog/AnalysisVersionCatalog.cs`
- Create: `src/PilotFlow.Api/Endpoints/Catalog/ProductOperationCatalog.cs`
- Create: `src/PilotFlow.Api/Endpoints/Catalog/FamilyRouteCatalog.cs`
- Create: `src/PilotFlow.Api/Endpoints/Catalog/EvaluationCatalog.cs`
- Create: `src/PilotFlow.Api/Endpoints/Catalog/ProtocolSelectionCatalog.cs`
- Create: `src/PilotFlow.Api/Endpoints/Catalog/DecisionExportCatalog.cs`
- Create: `src/PilotFlow.Api/Endpoints/EndpointCatalogExtensions.cs`
- Delete: `src/PilotFlow.Api/Endpoints/ScaffoldProbeEndpoints.cs`
- Create: `tests/PilotFlow.ContractTests/OpenApi/OpenApiOperationReader.cs`
- Create: `tests/PilotFlow.ContractTests/OpenApi/RegisteredEndpointReader.cs`
- Create: `tests/PilotFlow.ContractTests/OpenApiEndpointParityTests.cs`
- Create: `tests/PilotFlow.Api.Tests/ScaffoldEndpointBehaviorTests.cs`

**Interfaces:**
- Consumes: `contracts/openapi.json`, `NotImplementedEndpoint.Handle`, policy `analysis-user`.
- Produces: `EndpointDescriptor(string Method, string Pattern, string OperationId, bool RequiresIfMatch)`; `EndpointCatalog.All`; `MapScaffoldEndpoints(IEndpointRouteBuilder)`.

- [ ] **Step 1: Write exact OpenAPI parity tests**

`OpenApiOperationReader.Read(path)` returns the multiset of `(method, normalizedPath, operationId)`. `RegisteredEndpointReader.Read(serviceProvider)` returns the same shape from `EndpointDataSource` and `IEndpointNameMetadata`, excluding health/static endpoints.

Add tests:

```csharp
public void RegisteredEndpoints_ExactlyMatchOpenApiOperations()
public void RegisteredEndpoints_HaveUniqueMethodAndPath()
public void RegisteredEndpoints_HaveUniqueOperationIds()
```

Assert exactly 45 entries and exact set equality, not only counts.

- [ ] **Step 2: Run parity tests and verify RED**

Run `dotnet test tests/PilotFlow.ContractTests/PilotFlow.ContractTests.csproj --filter OpenApiEndpointParityTests`.

Expected: FAIL with 45 missing operations and the temporary probe reported as unexpected if it is still present.

- [ ] **Step 3: Implement descriptor catalogs**

Transcribe every method/path/operationId from `contracts/openapi.json` into the seven focused catalog files. Prefix patterns with `/api/v1`. Set `RequiresIfMatch=true` only when OpenAPI declares the header for that operation.

- [ ] **Step 4: Map generic scaffold endpoints**

For each descriptor, call `MapMethods`, `WithName(operationId)`, `RequireAuthorization("analysis-user")`, and route to `NotImplementedEndpoint.Handle`. Remove the temporary probe.

- [ ] **Step 5: Verify parity GREEN**

Run the focused parity tests. Expected: exactly 45 operations, no missing, extra, or duplicate entries.

- [ ] **Step 6: Write and run scaffold behavior tests**

Generate concrete URLs by replacing UUID route parameters with fixed valid UUIDs. For every descriptor assert unauthenticated status `401`. For every descriptor that does not require `If-Match`, assert authenticated status `501`. Include a separate explicit unknown URL test asserting `404`.

Run the new tests and verify they fail before the final mapping/policies are complete, then pass after the minimal fixes.

- [ ] **Step 7: Run full verification**

```powershell
dotnet build PilotFlow.Backend.slnx
dotnet test PilotFlow.Backend.slnx
```

Expected: zero warnings; all contract and API tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src/PilotFlow.Api tests/PilotFlow.ContractTests tests/PilotFlow.Api.Tests
git commit -m "feat: register complete OpenAPI scaffold"
```

### Task 7: If-Match boundary for version mutations

**Files:**
- Create: `src/PilotFlow.Api/Concurrency/AggregateEtag.cs`
- Create: `src/PilotFlow.Api/Concurrency/IfMatchEndpointFilter.cs`
- Modify: `src/PilotFlow.Api/Endpoints/EndpointCatalogExtensions.cs`
- Create: `tests/PilotFlow.Api.Tests/IfMatchBoundaryTests.cs`

**Interfaces:**
- Consumes: `EndpointDescriptor.RequiresIfMatch` from Task 6.
- Produces: `AggregateEtag(Guid VersionId, long Revision)`; `AggregateEtag.TryParse(string, out AggregateEtag)`; filter result available to later handlers through `HttpContext.Items[AggregateEtag.HttpContextKey]`.

- [ ] **Step 1: Write parser and HTTP boundary tests**

Add exact parser cases for `"v:00000000-0000-4000-8000-000000000011:r:1"`, missing quotes, wrong prefix, invalid UUID, zero/negative revision, multiple header values, and weak ETag.

Add HTTP tests:

```csharp
public async Task VersionMutation_WithoutIfMatch_ReturnsPreconditionRequired()
public async Task VersionMutation_WithMalformedIfMatch_ReturnsInvalidRequest()
public async Task VersionMutation_WithValidIfMatch_ReachesStub()
```

Assert statuses `428`, `400`, and `501`, with `ETAG_REQUIRED`/`INVALID_ETAG` codes for errors.

- [ ] **Step 2: Run tests and verify RED**

Run `dotnet test tests/PilotFlow.Api.Tests/PilotFlow.Api.Tests.csproj --filter IfMatchBoundaryTests`.

Expected: FAIL because no parser/filter exists and mutations reach the stub.

- [ ] **Step 3: Implement strict parsing and filter**

Accept only the strong quoted format defined in the design. Do not compare revision to storage yet; parse and attach it for later handlers.

- [ ] **Step 4: Apply the filter from descriptor metadata**

Attach `IfMatchEndpointFilter` only when `RequiresIfMatch=true`. Authentication runs first so unauthenticated requests remain `401` and do not reveal header requirements.

- [ ] **Step 5: Verify GREEN and full suite**

Run focused parser/HTTP tests, then the full solution. Expected: all pass, no warnings.

- [ ] **Step 6: Commit**

```powershell
git add src/PilotFlow.Api tests/PilotFlow.Api.Tests
git commit -m "feat: enforce If-Match request shape"
```

### Task 8: Authoritative Swagger assets and developer handoff

**Files:**
- Modify: `src/PilotFlow.Api/PilotFlow.Api.csproj`
- Modify: `src/PilotFlow.Api/Hosting/EndpointRouteBuilderExtensions.cs`
- Create: `tests/PilotFlow.Api.Tests/SwaggerAssetTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `contracts/openapi.json`, `contracts/openapi.yaml`, and local Swagger UI assets from `contracts/reference/`.
- Produces: `/swagger/swagger.html`, `/swagger/openapi.json`, `/swagger/openapi.yaml`, `/swagger/swagger-ui.css`, `/swagger/swagger-ui-bundle.js` copied from the authoritative files at build/publish time.

- [ ] **Step 1: Write failing static asset tests**

Add:

```csharp
public async Task SwaggerPage_LoadsWithoutAuthentication()
public async Task ServedOpenApiJson_EqualsRepositoryContract()
public async Task SwaggerAssets_AreAvailableOffline()
```

Compare response bytes for served `openapi.json` with `contracts/openapi.json`; assert CSS/JS return `200` with appropriate content types.

- [ ] **Step 2: Run tests and verify RED**

Run `dotnet test tests/PilotFlow.Api.Tests/PilotFlow.Api.Tests.csproj --filter SwaggerAssetTests`.

Expected: FAIL with `404` for the assets.

- [ ] **Step 3: Link contract assets into publish output**

Use MSBuild `Content Include` with `Link` and `CopyToOutputDirectory/CopyToPublishDirectory=PreserveNewest`; do not create byte-divergent duplicate files under `src/`.

- [ ] **Step 4: Serve Swagger assets**

Enable static files for `/swagger`. Keep these assets and health endpoints outside the `analysis-user` policy.

- [ ] **Step 5: Update README**

Document prerequisites, `dotnet restore`, `dotnet run --project src/PilotFlow.Api`, `/health/live`, `/health/ready`, Swagger URL, authentication behavior, test commands, and the fact that all 45 business operations intentionally return `NOT_IMPLEMENTED` at this milestone.

- [ ] **Step 6: Run final scaffold verification**

```powershell
dotnet format PilotFlow.Backend.slnx --verify-no-changes
dotnet build PilotFlow.Backend.slnx
dotnet test PilotFlow.Backend.slnx
git diff --check
git status --short
```

Expected: formatting clean, build succeeds with zero warnings, all tests pass, no whitespace errors, and only intended files remain changed.

- [ ] **Step 7: Commit**

```powershell
git add src/PilotFlow.Api tests/PilotFlow.Api.Tests README.md
git commit -m "docs: publish scaffold runbook and Swagger"
```

### Task 9: Docker runtime and test-only CI

**Files:**
- Create: `Dockerfile`
- Create: `.dockerignore`
- Create: `compose.yaml`
- Create: `.github/workflows/ci.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: completed `PilotFlow.Api`, `PilotFlow.Backend.slnx`, and health endpoints from earlier tasks.
- Produces: reproducible local API image and container launch; CI workflow that runs tests only and performs no deployment or registry publication.

- [ ] **Step 1: Write container and CI acceptance commands before configuration**

Record the required outcomes: `docker build` succeeds, the container answers `/health/live` with `200`, and the CI workflow executes `dotnet test PilotFlow.Backend.slnx --configuration Release` on pushes and pull requests to `main`.

- [ ] **Step 2: Confirm RED**

Run `docker build -t pilot-flow-backend:local .` and verify it fails because `Dockerfile` does not exist. Verify `.github/workflows/ci.yml` is absent.

- [ ] **Step 3: Implement the minimal multi-stage image**

Use official .NET 10 SDK and ASP.NET runtime images, restore through the solution, publish only `PilotFlow.Api`, run as the image's non-root user, expose port 8080, and set `ASPNETCORE_URLS=http://+:8080`. Do not bake secrets or JWT settings into the image.

- [ ] **Step 4: Add local compose launch**

`compose.yaml` builds the local image, maps `8080:8080`, sets `ASPNETCORE_ENVIRONMENT=Development`, and includes an HTTP healthcheck. It does not add PostgreSQL before the database stage.

- [ ] **Step 5: Add test-only GitHub Actions CI**

Create one workflow triggered by pushes and pull requests to `main`. It checks out the repository, installs .NET 10, restores, and runs `dotnet test PilotFlow.Backend.slnx --configuration Release --no-restore`. Do not log in to Docker Hub, build/push images, deploy, or request registry secrets.

- [ ] **Step 6: Verify GREEN**

Run:

```powershell
dotnet test PilotFlow.Backend.slnx --configuration Release
docker build -t pilot-flow-backend:local .
docker compose up --build -d
Invoke-WebRequest http://127.0.0.1:8080/health/live
docker compose down
```

Expected: tests pass, image builds, container becomes healthy, health endpoint returns `200`, and compose resources stop cleanly.

- [ ] **Step 7: Document future image publication boundary**

Update README with Docker commands and state that Docker Hub publication is intentionally not in CI yet. Note the future requirements: registry repository, Docker Hub username, access token secret, and agreed tag policy.

- [ ] **Step 8: Commit**

```powershell
git add Dockerfile .dockerignore compose.yaml .github/workflows/ci.yml README.md
git commit -m "ci: add Docker runtime and test workflow"
```

