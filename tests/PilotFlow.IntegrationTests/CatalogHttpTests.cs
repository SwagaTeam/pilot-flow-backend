using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.IntegrationTests;

public sealed partial class CatalogHttpTests(AnalysisApiDatabase database) : IClassFixture<AnalysisApiDatabase>
{
    private const string Evidence = """{"source":"Test","basis":null,"period":null,"assessedOn":"2026-11-01","missingReason":null}""";
    private const string Unknown = """{"source":null,"basis":null,"period":null,"assessedOn":null,"missingReason":null}""";
    private const string Draft = """
        {"data":{"title":"Catalog test","enterpriseName":null,"region":null,"activity":null,
        "projectStartDate":null,"financialPeriod":null,"activitiesPeriod":null,"forecastPeriod":null,
        "totalRevenue":null,"currency":"RUB","accountingBasis":null,"revenueEvidence":null,
        "remainderExplanation":null,"nomenclatureComplete":false,"completenessEvidence":null,
        "participants":[],"enterpriseInn":null,"familyConclusions":null,"rankingConclusions":null,"laborPeriod":null}}
        """;

    [Fact]
    public async Task FamilyMetrics_ComputeLoadAndRejectInconsistentInputWithoutAdvancingRevision()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var version = await CreateVersionAsync(client);
        var familyId = Guid.NewGuid();
        await using (var seed = database.DataSource.CreateCommand("""
            INSERT INTO pilot.family (id, version_id, name, confirmed, confirmation_evidence)
            VALUES ($1, $2, 'Тест', false, '{}')
            """))
        {
            seed.Parameters.Add(new Npgsql.NpgsqlParameter<Guid> { TypedValue = familyId });
            seed.Parameters.Add(new Npgsql.NpgsqlParameter<Guid> { TypedValue = version.Id });
            await seed.ExecuteNonQueryAsync();
        }

        var path = version.Path + $"/families/{familyId:D}/metrics";
        var empty = await GetAsync(client, path);
        Assert.Equal(JsonValueKind.Null, empty.Body.GetProperty("loadPercent").ValueKind);
        var metrics = JsonNode.Parse("""
            {"salesGrowthPercent":"-1.5","salesGrowthEvidence":{},"loadPercent":null,
            "loadActual":"1","loadCapacity":"3","loadUnit":"шт.","loadEvidence":{},
            "productionType":"project","productionTypeEvidence":{},"laborMode":"manual",
            "laborHoursPerUnit":"5","directLaborHours":null,"outputQuantity":null,
            "laborUnitCode":"piece","laborBaseQuantity":"1","laborEvidence":{},"comparabilityExplanation":null}
            """)!;
        foreach (var property in new[] { "salesGrowthEvidence", "loadEvidence", "productionTypeEvidence", "laborEvidence" })
        {
            metrics[property] = JsonNode.Parse(Evidence);
        }

        var saved = await SendAsync(client, HttpMethod.Put, path, metrics, empty.Etag, HttpStatusCode.OK);
        Assert.Equal("33.333333", saved.Body.GetProperty("loadPercent").GetString());
        Assert.Equal(-1.5m, decimal.Parse(saved.Body.GetProperty("salesGrowthPercent").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture));
        metrics["loadPercent"] = "33.333335";
        await ProblemAsync(client, HttpMethod.Put, path, HttpStatusCode.UnprocessableEntity, saved.Etag, metrics);
        metrics["loadPercent"] = null;
        metrics["loadActual"] = "10000.000001";
        metrics["loadCapacity"] = "10000";
        await ProblemAsync(client, HttpMethod.Put, path, HttpStatusCode.UnprocessableEntity, saved.Etag, metrics);
        Assert.Equal(saved.Etag, (await GetAsync(client, path)).Etag);
    }

    [Fact]
    public async Task IdentityAndRules_ReadAuthenticatedSubjectAndPersistedRuleSet()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var me = await GetAsync(client, "/api/v1/me");
        Assert.Equal(client.DefaultRequestHeaders.GetValues(TestAuthenticationHandler.SubjectHeader).Single(),
            me.Body.GetProperty("subject").GetString());
        var rules = await GetAsync(client, "/api/v1/rules/fck-2026-tz-v1");
        Assert.Equal("fck-2026-tz-v1", rules.Body.GetProperty("version").GetString());
        Assert.Equal("proposed", rules.Body.GetProperty("status").GetString());
        Assert.Equal("МР-11-2026 / ТЗ §§5,8", rules.Body.GetProperty("source").GetString());
        Assert.Contains(rules.Body.GetProperty("policies").EnumerateArray(), policy =>
            policy.GetProperty("code").GetString() == "NEGATIVE_GROWTH"
            && policy.GetProperty("status").GetString() == "unresolved");
        await ProblemAsync(client, HttpMethod.Get, "/api/v1/rules/nonexistent", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Products_CrudAndCompletenessFilterUseTheSameSnapshot()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var version = await CreateVersionAsync(client);
        var products = version.Path + "/products";
        var complete = await SendAsync(client, HttpMethod.Post, products, Product("Деталь", "X%1"), version.Etag, HttpStatusCode.Created);
        Assert.Equal("complete", complete.Body.GetProperty("completeness").GetString());
        var productPath = products + "/" + complete.Body.GetProperty("id").GetGuid();
        var incomplete = await SendAsync(client, HttpMethod.Post, products, Product("Брак", "XYZ", known: false),
            complete.Etag, HttpStatusCode.Created);
        Assert.Equal("incomplete", incomplete.Body.GetProperty("completeness").GetString());
        Assert.NotEmpty(incomplete.Body.GetProperty("issues").EnumerateArray());

        var page = await GetAsync(client, products + "?q=x%25&completeness=complete&limit=1");
        Assert.Equal(incomplete.Etag, page.Etag);
        Assert.Equal(1, page.Body.GetProperty("total").GetInt32());
        Assert.Equal(2, page.Body.GetProperty("unfilteredTotal").GetInt32());
        Assert.Equal("X%1", page.Body.GetProperty("items")[0].GetProperty("data").GetProperty("productCode").GetString());
        var otherPage = await GetAsync(client, products + "?completeness=incomplete");
        Assert.Equal(1, otherPage.Body.GetProperty("total").GetInt32());

        var replaced = await SendAsync(client, HttpMethod.Put, productPath, Product("Остаток", null, other: true),
            incomplete.Etag, HttpStatusCode.OK);
        Assert.Equal("complete", replaced.Body.GetProperty("completeness").GetString());
        var fetched = await GetAsync(client, productPath);
        Assert.Equal("Остаток", fetched.Body.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, fetched.Body.GetProperty("data").GetProperty("calendarDurationDays").ValueKind);
        var removed = await SendAsync(client, HttpMethod.Delete, productPath, null, fetched.Etag, HttpStatusCode.NoContent);
        Assert.NotEqual(fetched.Etag, removed.Etag);
        await ProblemAsync(client, HttpMethod.Get, productPath, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OperationsAndRoutes_PreserveRepeatedOccurrencesAndRefuseInUseDeletion()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var version = await CreateVersionAsync(client);
        var operations = version.Path + "/operations";
        var op = await SendAsync(client, HttpMethod.Post, operations, Operation("Резка"), version.Etag, HttpStatusCode.Created);
        var id = op.Body.GetProperty("id").GetGuid();
        var operationPath = operations + "/" + id;
        var revised = await SendAsync(client, HttpMethod.Put, operationPath, Operation("Резка 2"), op.Etag, HttpStatusCode.OK);
        var list = await GetAsync(client, operations + "?q=%D1%80%D0%B5%D0%B7%D0%BA%D0%B0%202");
        Assert.Equal(1, list.Body.GetProperty("total").GetInt32());
        Assert.Equal(revised.Etag, (await GetAsync(client, operationPath)).Etag);

        var product = await SendAsync(client, HttpMethod.Post, version.Path + "/products", Product("Деталь", null),
            revised.Etag, HttpStatusCode.Created);
        var productPath = version.Path + "/products/" + product.Body.GetProperty("id").GetGuid();
        var routePath = productPath + "/route";
        Assert.Empty((await GetAsync(client, routePath)).Body.GetProperty("steps").EnumerateArray());
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var route = Route(Step(second, id, 2), Step(first, id, 1));
        var saved = await SendAsync(client, HttpMethod.Put, routePath, route, product.Etag, HttpStatusCode.OK);
        var steps = saved.Body.GetProperty("steps");
        Assert.Equal(first, steps[0].GetProperty("occurrenceId").GetGuid());
        Assert.Equal(second, steps[1].GetProperty("occurrenceId").GetGuid());
        Assert.Equal(id, steps[1].GetProperty("operationId").GetGuid());
        await ProblemAsync(client, HttpMethod.Delete, operationPath, HttpStatusCode.Conflict, saved.Etag);
        await ProblemAsync(client, HttpMethod.Put, routePath, HttpStatusCode.PreconditionFailed, product.Etag, route);
        await ProblemAsync(client, HttpMethod.Put, routePath, HttpStatusCode.UnprocessableEntity, saved.Etag,
            Route(Step(first, id, 2)));
        Assert.Equal(saved.Etag, (await GetAsync(client, routePath)).Etag);
        var deleted = await SendAsync(client, HttpMethod.Delete, productPath, null, saved.Etag, HttpStatusCode.NoContent);
        await SendAsync(client, HttpMethod.Delete, operationPath, null, deleted.Etag, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RouteReplacement_InvalidatesFamilyAndSelectionAtomically()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var version = await CreateVersionAsync(client);
        var op = await SendAsync(client, HttpMethod.Post, version.Path + "/operations", Operation("Drill"),
            version.Etag, HttpStatusCode.Created);
        var product = await SendAsync(client, HttpMethod.Post, version.Path + "/products", Product("Part", null),
            op.Etag, HttpStatusCode.Created);
        var productId = product.Body.GetProperty("id").GetGuid();
        var familyId = Guid.NewGuid();
        await using var seed = database.DataSource.CreateCommand("""
            INSERT INTO pilot.family (id,version_id,name,confirmed,confirmation_evidence)
            VALUES (@family,@version,'Test family',true,'{}');
            INSERT INTO pilot.family_product (version_id,product_id,family_id) VALUES (@version,@product,@family);
            INSERT INTO pilot.family_metrics (
              family_id, version_id, sales_growth_percent, sales_growth_evidence, load_percent, load_evidence,
              production_type_evidence, labor_mode, labor_hours_per_unit, labor_evidence)
            VALUES (@family,@version,11,'{}',70,'{}','{}','manual',2,'{}');
            INSERT INTO pilot.selection (version_id,family_id,mode,direct_basis_evidence) VALUES (@version,@family,'ranked','{}');
            """);
        seed.Parameters.AddWithValue("family", familyId);
        seed.Parameters.AddWithValue("version", version.Id);
        seed.Parameters.AddWithValue("product", productId);
        await seed.ExecuteNonQueryAsync();

        var routePath = version.Path + "/products/" + productId + "/route";
        await ProblemAsync(client, HttpMethod.Put, routePath, HttpStatusCode.UnprocessableEntity, product.Etag,
            Route(Step(Guid.NewGuid(), Guid.NewGuid(), 1)));
        await using var verify = database.DataSource.CreateCommand("""
            SELECT confirmed, load_percent, labor_hours_per_unit, sales_growth_percent,
              EXISTS(SELECT FROM pilot.selection WHERE version_id = f.version_id)
            FROM pilot.family AS f JOIN pilot.family_metrics AS m ON m.family_id=f.id WHERE f.id=$1
            """);
        verify.Parameters.Add(new Npgsql.NpgsqlParameter<Guid> { TypedValue = familyId });
        await using (var unchanged = await verify.ExecuteReaderAsync())
        {
            Assert.True(await unchanged.ReadAsync());
            Assert.True(unchanged.GetBoolean(0));
            Assert.True(unchanged.GetBoolean(4));
        }

        await SendAsync(client, HttpMethod.Put, routePath,
            Route(Step(Guid.NewGuid(), op.Body.GetProperty("id").GetGuid(), 1)), product.Etag, HttpStatusCode.OK);
        await using var updated = await verify.ExecuteReaderAsync();
        Assert.True(await updated.ReadAsync());
        Assert.False(updated.GetBoolean(0));
        Assert.True(updated.IsDBNull(1));
        Assert.True(updated.IsDBNull(2));
        Assert.Equal(11m, updated.GetDecimal(3));
        Assert.False(updated.GetBoolean(4));
    }

    [Fact]
    public async Task Catalog_RejectsForeignOwnerAndCrossVersionLinks()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        using var outsider = Client(factory);
        var first = await CreateVersionAsync(client);
        var second = await CreateVersionAsync(client);
        var op = await SendAsync(client, HttpMethod.Post, second.Path + "/operations", Operation("Other"),
            second.Etag, HttpStatusCode.Created);
        var item = await SendAsync(client, HttpMethod.Post, first.Path + "/products", Product("Item", null),
            first.Etag, HttpStatusCode.Created);
        var path = first.Path + "/products/" + item.Body.GetProperty("id").GetGuid();
        await ProblemAsync(outsider, HttpMethod.Get, first.Path + "/products", HttpStatusCode.NotFound);
        await ProblemAsync(outsider, HttpMethod.Delete, path, HttpStatusCode.NotFound, item.Etag);
        await ProblemAsync(client, HttpMethod.Put, path + "/route", HttpStatusCode.UnprocessableEntity, item.Etag,
            Route(Step(Guid.NewGuid(), op.Body.GetProperty("id").GetGuid(), 1)));
        Assert.Equal(item.Etag, (await GetAsync(client, path)).Etag);
    }

    [Fact]
    public async Task ProductAndOperationWrites_WithOneRevision_OnlyOneCommits()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var version = await CreateVersionAsync(client);
        using var product = new HttpRequestMessage(HttpMethod.Post, version.Path + "/products")
        {
            Content = JsonContent.Create(Product("Part", null)),
        };
        using var operation = new HttpRequestMessage(HttpMethod.Post, version.Path + "/operations")
        {
            Content = JsonContent.Create(Operation("Operation")),
        };
        product.Headers.IfMatch.ParseAdd(version.Etag);
        operation.Headers.IfMatch.ParseAdd(version.Etag);

        var replies = await Task.WhenAll(client.SendAsync(product, CancellationToken.None),
            client.SendAsync(operation, CancellationToken.None));
        using var first = replies[0];
        using var second = replies[1];
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.PreconditionFailed },
            replies.Select(response => response.StatusCode).Order().ToArray());
        var committed = Assert.Single(replies, response => response.IsSuccessStatusCode);
        var updated = await GetAsync(client, version.Path);
        Assert.Equal(2L, updated.Body.GetProperty("revision").GetInt64());
        Assert.Equal(committed.Headers.ETag?.Tag, updated.Etag);
    }

    [Fact]
    public async Task ReplaceRoute_WhenOccurrenceBelongsToAnotherProduct_KeepsBothRoutes()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var version = await CreateVersionAsync(client);
        var operation = await SendAsync(client, HttpMethod.Post, version.Path + "/operations", Operation("Operation"),
            version.Etag, HttpStatusCode.Created);
        var operationId = operation.Body.GetProperty("id").GetGuid();
        var products = version.Path + "/products";
        var first = await SendAsync(client, HttpMethod.Post, products, Product("Part 1", null), operation.Etag, HttpStatusCode.Created);
        var second = await SendAsync(client, HttpMethod.Post, products, Product("Part 2", null), first.Etag, HttpStatusCode.Created);
        var firstPath = products + "/" + first.Body.GetProperty("id").GetGuid() + "/route";
        var secondPath = products + "/" + second.Body.GetProperty("id").GetGuid() + "/route";
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstRoute = await SendAsync(client, HttpMethod.Put, firstPath, Route(Step(firstId, operationId, 1)),
            second.Etag, HttpStatusCode.OK);
        var secondRoute = await SendAsync(client, HttpMethod.Put, secondPath, Route(Step(secondId, operationId, 1)),
            firstRoute.Etag, HttpStatusCode.OK);

        var reused = await SendAsync(client, HttpMethod.Put, secondPath, Route(Step(secondId, operationId, 1)),
            secondRoute.Etag, HttpStatusCode.OK);
        await ProblemAsync(client, HttpMethod.Put, secondPath, HttpStatusCode.Conflict, reused.Etag,
            Route(Step(firstId, operationId, 1)));

        Assert.Equal(firstId, (await GetAsync(client, firstPath)).Body.GetProperty("steps")[0].GetProperty("occurrenceId").GetGuid());
        var unchanged = await GetAsync(client, secondPath);
        Assert.Equal(secondId, unchanged.Body.GetProperty("steps")[0].GetProperty("occurrenceId").GetGuid());
        Assert.Equal(reused.Etag, unchanged.Etag);
    }

    [Theory]
    [InlineData("Override", "New bench", null, true)]
    [InlineData(null, "New bench", null, false)]
    [InlineData(null, "Bench", "90", true)]
    public async Task OperationChange_InvalidatesOnlyEffectiveRouteChanges(
        string? overrideName, string editedWorkplace, string? editedCoverage, bool keepsConfirmation)
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var version = await CreateVersionAsync(client);
        var operations = version.Path + "/operations";
        var operation = await SendAsync(client, HttpMethod.Post, operations, Operation("Drill"), version.Etag, HttpStatusCode.Created);
        var operationId = operation.Body.GetProperty("id").GetGuid();
        var products = version.Path + "/products";
        var product = await SendAsync(client, HttpMethod.Post, products, Product("Part", null), operation.Etag, HttpStatusCode.Created);
        var productId = product.Body.GetProperty("id").GetGuid();
        var route = await SendAsync(client, HttpMethod.Put, products + "/" + productId + "/route",
            Route(Step(Guid.NewGuid(), operationId, 1, overrideName)), product.Etag, HttpStatusCode.OK);

        var familyId = Guid.NewGuid();
        await using var seed = database.DataSource.CreateCommand("""
            INSERT INTO pilot.family (id,version_id,name,confirmed,confirmation_evidence)
            VALUES (@family,@version,'Family',true,'{}');
            INSERT INTO pilot.family_product (version_id,product_id,family_id) VALUES (@version,@product,@family);
            INSERT INTO pilot.family_metrics (
              family_id,version_id,sales_growth_evidence,load_percent,load_evidence,
              production_type_evidence,labor_mode,labor_evidence)
            VALUES (@family,@version,'{}',70,'{}','{}','manual','{}');
            """);
        seed.Parameters.AddWithValue("family", familyId);
        seed.Parameters.AddWithValue("version", version.Id);
        seed.Parameters.AddWithValue("product", productId);
        await seed.ExecuteNonQueryAsync();

        await SendAsync(client, HttpMethod.Put, operations + "/" + operationId, Operation("Drill", editedWorkplace, editedCoverage),
            route.Etag, HttpStatusCode.OK);

        await using var probe = database.DataSource.CreateCommand("""
            SELECT confirmed, load_percent FROM pilot.family AS f JOIN pilot.family_metrics AS m ON m.family_id=f.id WHERE f.id=$1
            """);
        probe.Parameters.Add(new Npgsql.NpgsqlParameter<Guid> { TypedValue = familyId });
        await using var result = await probe.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal(keepsConfirmation, result.GetBoolean(0));
        Assert.Equal(!keepsConfirmation, result.IsDBNull(1));
    }

    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("ConnectionStrings:PilotFlow", database.ConnectionString);
        builder.ConfigureTestServices(services => services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, static _ => { }));
    });

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, Guid.NewGuid().ToString("N"));
        return client;
    }

    private static async Task<(Guid Id, string Path, string Etag)> CreateVersionAsync(HttpClient client)
    {
        var draft = JsonNode.Parse(Draft)!;
        draft["data"]!["revenueEvidence"] = JsonNode.Parse(Unknown);
        draft["data"]!["completenessEvidence"] = JsonNode.Parse(Unknown);
        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/analyses", draft, null, HttpStatusCode.Created);
        var analysisId = created.Body.GetProperty("id").GetGuid();
        var versionId = created.Body.GetProperty("version").GetProperty("id").GetGuid();
        return (versionId, $"/api/v1/analyses/{analysisId:D}/versions/{versionId:D}", created.Etag!);
    }

    private static object Product(string name, string? code, bool known = true, bool other = false) => new
    {
        name,
        productCode = code,
        unitCode = "piece",
        salesVolume = known ? "0" : null,
        revenue = "1000.000001",
        rawMaterialCost = "0",
        financialEvidence = JsonNode.Parse(Evidence),
        calendarDurationDays = known && !other ? "0" : null,
        durationStart = (string?)null,
        durationEnd = (string?)null,
        includesWaiting = (bool?)null,
        durationEvidence = JsonNode.Parse(Evidence),
        inProductionPlan = other ? (bool?)null : false,
        planEvidence = JsonNode.Parse(Evidence),
        isOther = other,
    };

    private static object Operation(string name, string workplace = "Bench", string? coverage = null) => new
    {
        name,
        kind = "operation",
        workplaceName = workplace,
        aggregationCoveragePercent = coverage,
        aggregationEvidence = JsonNode.Parse(Unknown),
    };

    private static object Step(Guid occurrence, Guid operation, int position, string? workplace = null) => new
    {
        occurrenceId = occurrence,
        operationId = operation,
        position,
        durationHours = "0.000001",
        workplaceName = workplace,
        evidence = JsonNode.Parse(Evidence),
    };

    private static object Route(params object[] steps) => new { steps };

    private static Task<(JsonElement Body, string? Etag)> GetAsync(HttpClient client, string path) =>
        SendAsync(client, HttpMethod.Get, path, null, null, HttpStatusCode.OK);

    private static async Task ProblemAsync(HttpClient client, HttpMethod method, string path,
        HttpStatusCode status, string? etag = null, object? body = null)
    {
        var result = await SendAsync(client, method, path, body, etag, status);
        Assert.Equal((int)status, result.Body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(result.Body.GetProperty("code").GetString()));
    }

    private static async Task<(JsonElement Body, string? Etag)> SendAsync(
        HttpClient client, HttpMethod method, string path, object? body, string? etag, HttpStatusCode status)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        using var response = await client.SendAsync(request, CancellationToken.None);
        var payload = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        Assert.True(response.StatusCode == status,
            $"{method} {path}: expected {(int)status}, received {(int)response.StatusCode}: {System.Text.Encoding.UTF8.GetString(payload)}");
        if ((int)status >= 400)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        using var json = payload.Length > 0 ? JsonDocument.Parse(payload) : null;
        return (json?.RootElement.Clone() ?? default, response.Headers.ETag?.Tag);
    }
}
