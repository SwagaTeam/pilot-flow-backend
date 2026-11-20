using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.IntegrationTests;

public sealed partial class AnalysisHttpTests(AnalysisApiDatabase database) : IClassFixture<AnalysisApiDatabase>
{
    private const string Analyses = "/api/v1/analyses";
    private const string Draft = """
        {
          "title":"Цех Север %", "enterpriseName":null, "region":null, "activity":null,
          "projectStartDate":"2026-11-01", "financialPeriod":null, "activitiesPeriod":null, "forecastPeriod":null,
          "totalRevenue":"123456789012345678.123456", "currency":"RUB", "accountingBasis":null,
          "revenueEvidence":{"source":"Input","basis":null,"period":null,"assessedOn":null,"missingReason":null},
          "remainderExplanation":null, "nomenclatureComplete":false,
          "completenessEvidence":{"source":null,"basis":null,"period":null,"assessedOn":null,"missingReason":null},
          "participants":[{"fullName":"Оператор","position":null,"role":"operator"}],
          "enterpriseInn":null, "familyConclusions":null, "rankingConclusions":null,
          "laborPeriod":{"start":"2026-05-01","end":"2026-10-31"}
        }
        """;

    [Fact]
    public async Task CreateAndRead_PreserveIdsOwnerAndDraftFields()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var health = await client.GetAsync("/health/ready", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var created = await CreateAsync(client);
        using var response = await client.GetAsync(created.VersionPath, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Etag, Assert.Single(response.Headers.GetValues("ETag")));
        var version = await ReadAsync(response);
        Assert.Equal(1, version.GetProperty("versionNumber").GetInt32());
        Assert.Equal("draft", version.GetProperty("state").GetString());
        var data = version.GetProperty("data");
        Assert.Equal("123456789012345678.123456", data.GetProperty("totalRevenue").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("enterpriseName").ValueKind);
        Assert.Equal("Оператор", data.GetProperty("participants")[0].GetProperty("fullName").GetString());
        Assert.Equal("2026-05-01", data.GetProperty("laborPeriod").GetProperty("start").GetString());

        using var analysis = await client.GetAsync(created.AnalysisPath, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, analysis.StatusCode);
        Assert.Equal(created.Etag, Assert.Single(analysis.Headers.GetValues("ETag")));
        var document = await ReadAsync(analysis);
        Assert.Equal(client.DefaultRequestHeaders.GetValues(TestAuthenticationHandler.SubjectHeader).Single(),
            document.GetProperty("ownerSubject").GetString());
    }

    [Fact]
    public async Task JwtBearer_UsesRawSubjectAndRejectsTokenWithoutIt()
    {
        const string issuer = "https://integration.pilot-flow.invalid";
        const string audience = "pilot-flow-api";
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        using var factory = CreateFactory(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = issuer;
                options.TokenValidationParameters.ValidAudience = audience;
                options.TokenValidationParameters.IssuerSigningKey = key;
            }));
        using var client = factory.CreateClient();
        var claims = new Dictionary<string, object>
        {
            [ClaimTypes.NameIdentifier] = "not-the-owner",
            [JwtRegisteredClaimNames.Sub] = "verified-subject",
        };
        Authorize();

        var created = await CreateAsync(client);
        using var analysis = await client.GetAsync(created.AnalysisPath, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, analysis.StatusCode);
        Assert.Equal("verified-subject", (await ReadAsync(analysis)).GetProperty("ownerSubject").GetString());

        claims.Remove(JwtRegisteredClaimNames.Sub);
        Authorize();
        using var rejected = await client.GetAsync(created.AnalysisPath, CancellationToken.None);
        await AssertProblemAsync(rejected, HttpStatusCode.Unauthorized, "UNAUTHORIZED");

        void Authorize()
        {
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Audience = audience,
                Issuer = issuer,
                Claims = claims,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    [Fact]
    public async Task Lists_FilterLiteralTitleAndKeepOwnerTotals()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var other = CreateClient(factory);
        var first = await CreateAsync(client);
        await CreateAsync(client, "Цех Юг");
        await CreateAsync(other, "Цех Запад %");

        using var response = await client.GetAsync(Analyses + "?q=" + Uri.EscapeDataString("север %") + "&limit=1",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadAsync(response);
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("unfilteredTotal").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());

        using var wildcard = await client.GetAsync(Analyses + "?q=%25", CancellationToken.None);
        Assert.Equal(1, (await ReadAsync(wildcard)).GetProperty("total").GetInt32());

        using var versions = await client.GetAsync(first.AnalysisPath + "/versions?offset=1", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
        var versionPage = await ReadAsync(versions);
        Assert.Equal(1, versionPage.GetProperty("total").GetInt32());
        Assert.Empty(versionPage.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task AnalysisAndVersion_WhenForeignOrMismatched_ReturnNotFound()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var outsider = CreateClient(factory);
        var first = await CreateAsync(client);
        var second = await CreateAsync(client, "Second");

        foreach (var path in new[] { first.AnalysisPath, first.AnalysisPath + "/versions", first.VersionPath })
        {
            using var response = await outsider.GetAsync(path, CancellationToken.None);
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
        }

        using var wrongParent = await client.GetAsync(second.AnalysisPath + "/versions/" + first.VersionId, CancellationToken.None);
        await AssertProblemAsync(wrongParent, HttpStatusCode.NotFound, "NOT_FOUND");
        using var forbiddenWrite = await ReplaceAsync(outsider, first, Draft);
        await AssertProblemAsync(forbiddenWrite, HttpStatusCode.NotFound, "NOT_FOUND");
        using var invalidForeignWrite = await ReplaceAsync(outsider, first, Draft.Replace("Цех Север %", " ", StringComparison.Ordinal));
        await AssertProblemAsync(invalidForeignWrite, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task ConcurrentReplace_OnlyOneWriteSucceedsAndSelectionIsCleared()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var created = await CreateAsync(client);
        await using var seed = database.DataSource.CreateCommand("""
            INSERT INTO pilot.family (id, version_id, name, confirmation_evidence) VALUES (@family_id, @version_id, 'Family', '{}');
            INSERT INTO pilot.selection (version_id, family_id, mode, direct_basis_evidence) VALUES (@version_id, @family_id, 'ranked', '{}');
            """);
        seed.Parameters.AddWithValue("family_id", Guid.NewGuid());
        seed.Parameters.AddWithValue("version_id", created.VersionId);
        await seed.ExecuteNonQueryAsync(CancellationToken.None);

        var replies = await Task.WhenAll(
            ReplaceAsync(client, created, Draft.Replace("Цех Север %", "First", StringComparison.Ordinal)),
            ReplaceAsync(client, created, Draft.Replace("Цех Север %", "Second", StringComparison.Ordinal)));
        using var success = Assert.Single(replies, response => response.IsSuccessStatusCode);
        using var stale = Assert.Single(replies, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        await AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, "REVISION_CONFLICT");
        Assert.Equal($"\"v:{created.VersionId:D}:r:2\"", Assert.Single(success.Headers.GetValues("ETag")));
        var changed = await ReadAsync(success);
        Assert.Equal(2, changed.GetProperty("revision").GetInt64());
        using var stored = await client.GetAsync(created.VersionPath, CancellationToken.None);
        Assert.Equal(changed.GetProperty("data").GetProperty("title").GetString(),
            (await ReadAsync(stored)).GetProperty("data").GetProperty("title").GetString());
        await using var selection = database.DataSource.CreateCommand(
            "SELECT count(*) FROM pilot.selection WHERE version_id = $1");
        selection.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = created.VersionId });
        Assert.Equal(0L, await selection.ExecuteScalarAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Replace_WithAnotherVersionsEtag_ReturnsRevisionConflict()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var first = await CreateAsync(client);
        var second = await CreateAsync(client);

        using var response = await ReplaceAsync(client, first with { Etag = second.Etag }, Draft);

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed, "REVISION_CONFLICT");
    }

    [Fact]
    public async Task Replace_WhenVersionIsFixed_ReturnsStateConflict()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var created = await CreateAsync(client);
        await using var fix = database.DataSource.CreateCommand("""
            INSERT INTO pilot.family (id, version_id, name, confirmation_evidence)
            VALUES (@family, @version, 'Family', '{}');
            INSERT INTO pilot.decision (
                id, version_id, analysis_id, family_id, created_by_subject, created_by_name,
                snapshot_schema_version, rules_version, snapshot, snapshot_sha256)
            VALUES (gen_random_uuid(), @version, @analysis, @family, 'operator', 'Operator',
                '1.0', 'fck-2026-tz-v1', '{}', repeat('a', 64));
            UPDATE pilot.analysis_version SET state = 'fixed', revision = 2 WHERE id = @version;
            """);
        fix.Parameters.AddWithValue("family", Guid.NewGuid());
        fix.Parameters.AddWithValue("version", created.VersionId);
        fix.Parameters.AddWithValue("analysis", created.Id);
        await fix.ExecuteNonQueryAsync(CancellationToken.None);

        using var response = await ReplaceAsync(client, created with { Etag = $"\"v:{created.VersionId:D}:r:2\"" }, Draft);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "STATE_CONFLICT");
    }

    [Fact]
    public async Task Replace_WhenDatesAreReversed_ReturnsValidationIssueWithoutWriting()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var created = await CreateAsync(client);

        using var response = await ReplaceAsync(client, created, Draft.Replace("2026-10-31", "2026-01-01", StringComparison.Ordinal));

        var problem = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");
        Assert.Equal("/laborPeriod", problem.GetProperty("issues")[0].GetProperty("path").GetString());
        using var stored = await client.GetAsync(created.VersionPath, CancellationToken.None);
        Assert.Equal(created.Etag, Assert.Single(stored.Headers.GetValues("ETag")));
    }

    [Fact]
    public async Task RuntimeRole_CannotAlterSchemaOrTruncateVersions()
    {
        await using var runtime = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = runtime.CreateCommand("""
            SELECT has_schema_privilege(current_user, 'pilot', 'CREATE')
                OR has_table_privilege(current_user, 'pilot.analysis_version', 'TRUNCATE')
                OR has_column_privilege(current_user, 'pilot.analysis_version', 'rules_version', 'UPDATE')
                OR has_table_privilege(current_user, 'pilot.decision', 'UPDATE, DELETE')
            """);
        Assert.Equal(false, await command.ExecuteScalarAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RuntimeRole_CannotFixWithoutDecision()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var draft = await CreateAsync(client);
        await using var runtime = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = runtime.CreateCommand("""
            UPDATE pilot.analysis_version SET state = 'fixed', revision = revision + 1 WHERE id = $1
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = draft.VersionId });
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("fixed_requires_exactly_one_decision", error.MessageText);
        using var unchanged = await client.GetAsync(draft.VersionPath, CancellationToken.None);
        Assert.Equal(draft.Etag, Assert.Single(unchanged.Headers.GetValues("ETag")));
    }

    [Fact]
    public async Task Create_RequiresNullablePropertiesInRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var content = Json("{\"data\":" + Draft.Replace("\"enterpriseName\":null,", "", StringComparison.Ordinal) + "}");

        using var response = await client.PostAsync(Analyses, content, CancellationToken.None);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "INVALID_REQUEST");
    }

    [Fact]
    public async Task Get_LegacyEvidenceWithMissingFields_PreservesSourceAndNulls()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var created = await CreateAsync(client);
        await using var update = database.DataSource.CreateCommand("""
            UPDATE pilot.analysis_version SET revenue_evidence = '{"source":"Legacy import"}', revision = 2 WHERE id = $1
            """);
        update.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = created.VersionId });
        await update.ExecuteNonQueryAsync(CancellationToken.None);

        using var response = await client.GetAsync(created.VersionPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await ReadAsync(response)).GetProperty("data").GetProperty("revenueEvidence");
        Assert.Equal("Legacy import", data.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("basis").ValueKind);
    }

    [Theory]
    [InlineData("123.45", HttpStatusCode.BadRequest, "INVALID_REQUEST")]
    [InlineData("\"123.1234567\"", HttpStatusCode.BadRequest, "INVALID_REQUEST")]
    [InlineData("\"1e2\"", HttpStatusCode.BadRequest, "INVALID_REQUEST")]
    [InlineData("\"-0\"", HttpStatusCode.BadRequest, "INVALID_REQUEST")]
    [InlineData("\"-1\"", HttpStatusCode.BadRequest, "INVALID_REQUEST")]
    public async Task Create_WithInvalidDecimalFormat_ReturnsProblem(string value, HttpStatusCode status, string code)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var content = Json("{\"data\":" + Draft.Replace("\"123456789012345678.123456\"", value, StringComparison.Ordinal) + "}");

        using var response = await client.PostAsync(Analyses, content, CancellationToken.None);

        await AssertProblemAsync(response, status, code);
    }

    [Fact]
    public async Task Create_WithIncorrectJsonPropertyCasing_ReturnsInvalidRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var content = Json("{\"Data\":" + Draft + "}");

        using var response = await client.PostAsync(Analyses, content, CancellationToken.None);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "INVALID_REQUEST");
    }

    [Fact]
    public async Task Create_WithNonJsonBody_ReturnsInvalidRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var content = new StringContent("payload", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(Analyses, content, CancellationToken.None);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "INVALID_REQUEST");
    }

    [Fact]
    public async Task List_WithNullCharacterInQuery_ReturnsInvalidRequest()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(Analyses + "?q=%00", CancellationToken.None);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "INVALID_REQUEST");

        var created = await CreateAsync(client);
        using var versions = await client.GetAsync(created.AnalysisPath + "/versions?q=%00", CancellationToken.None);
        await AssertProblemAsync(versions, HttpStatusCode.BadRequest, "INVALID_REQUEST");
    }

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureAuthentication = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("ConnectionStrings:PilotFlow", database.ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            if (configureAuthentication is null)
            {
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName, static _ => { });
            }
            else
            {
                configureAuthentication(services);
            }
        });
    });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, Guid.NewGuid().ToString("N"));
        return client;
    }

    private static async Task<CreatedAnalysis> CreateAsync(HttpClient client, string title = "Цех Север %")
    {
        using var content = Json("{\"data\":" + Draft.Replace("Цех Север %", title, StringComparison.Ordinal) + "}");
        using var response = await client.PostAsync(Analyses, content, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await ReadAsync(response);
        var id = payload.GetProperty("id").GetGuid();
        Assert.Equal($"{Analyses}/{id:D}", response.Headers.Location?.OriginalString);
        var versionId = payload.GetProperty("version").GetProperty("id").GetGuid();
        var etag = Assert.Single(response.Headers.GetValues("ETag"));
        Assert.Equal($"\"v:{versionId:D}:r:1\"", etag);
        return new CreatedAnalysis(id, versionId, etag);
    }

    private static async Task<HttpResponseMessage> ReplaceAsync(HttpClient client, CreatedAnalysis target, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, target.VersionPath) { Content = Json(json) };
        request.Headers.TryAddWithoutValidation("If-Match", target.Etag);
        return await client.SendAsync(request, CancellationToken.None);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private sealed record CreatedAnalysis(Guid Id, Guid VersionId, string Etag)
    {
        public string AnalysisPath => $"{Analyses}/{Id:D}";
        public string VersionPath => $"{AnalysisPath}/versions/{VersionId:D}";
    }
}
