using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PilotFlow.Api.Tests.Infrastructure;

namespace PilotFlow.IntegrationTests;

public sealed class EvaluationHttpTests(AnalysisApiDatabase database) : IClassFixture<AnalysisApiDatabase>
{
    [Fact]
    public async Task Evaluation_RecalculatesAllFamiliesAndKeepsExcludedFamilyOutOfComparison()
    {
        var seeded = await SeedAsync();
        using var factory = CreateFactory();
        using var client = Client(factory, seeded.Owner);
        using var response = await client.GetAsync(seeded.Path + "/evaluation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"\"v:{seeded.Version:D}:r:1\"", response.Headers.ETag?.Tag);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var evaluation = body.RootElement;
        var families = evaluation.GetProperty("families").EnumerateArray()
            .ToDictionary(family => family.GetProperty("familyId").GetGuid());
        Assert.Equal("400", evaluation.GetProperty("reconciledRevenue").GetString());
        Assert.Equal("13", families[seeded.Families[0]].GetProperty("totalScore").GetString());
        Assert.Equal("12", families[seeded.Families[1]].GetProperty("totalScore").GetString());
        Assert.Equal("7.5", families[seeded.Families[2]].GetProperty("totalScore").GetString());
        Assert.Equal("excluded", families[seeded.Families[3]].GetProperty("admission").GetString());
        var exclusionCodes = families[seeded.Families[3]].GetProperty("issues").EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString()).ToArray();
        Assert.Contains("DURATION_EXCEEDED", exclusionCodes);
        Assert.Contains("PRODUCTION_PLAN_UNKNOWN", exclusionCodes);
        Assert.Equal(JsonValueKind.Null, families[seeded.Families[3]].GetProperty("totalScore").ValueKind);
        Assert.All(families[seeded.Families[3]].GetProperty("scores").EnumerateArray(),
            score => Assert.Equal("notApplicable", score.GetProperty("status").GetString()));

        using var other = Client(factory, "other-owner");
        using var unauthorized = await other.GetAsync(seeded.Path + "/evaluation");
        Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [Fact]
    public async Task UnknownAdmissionMakesRankingProvisionalAndRetainsKnownScores()
    {
        var seeded = await SeedAsync();
        await using var update = database.DataSource.CreateCommand("""
            UPDATE pilot.product SET in_production_plan = NULL, calendar_duration_days = NULL
            WHERE version_id = $1 AND name = 'Product 4'
            """);
        update.Parameters.Add(new Npgsql.NpgsqlParameter<Guid> { TypedValue = seeded.Version });
        await update.ExecuteNonQueryAsync();
        using var factory = CreateFactory();
        using var client = Client(factory, seeded.Owner);
        using var response = await client.GetAsync(seeded.Path + "/evaluation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var evaluation = body.RootElement;
        Assert.False(evaluation.GetProperty("canRecommend").GetBoolean());
        var first = evaluation.GetProperty("families").EnumerateArray().Single(family =>
            family.GetProperty("familyId").GetGuid() == seeded.Families[0]);
        Assert.True(first.GetProperty("isProvisional").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("rank").ValueKind);
        Assert.Equal("13", first.GetProperty("knownScoreSum").GetString());
        Assert.Contains(first.GetProperty("scores").EnumerateArray(),
            score => score.GetProperty("status").GetString() == "provisional");
    }

    [Fact]
    public async Task FamilyMatrix_SumsOccurrencesAndCalculatesCycleDifference()
    {
        var seeded = await SeedAsync();
        await using var update = database.DataSource.CreateCommand("""
            UPDATE pilot.family_product
            SET family_id = @family
            WHERE version_id = @version AND product_id IN (
              SELECT id FROM pilot.product WHERE version_id = @version AND name = 'Product 2'
            );
            INSERT INTO pilot.operation (id, version_id, name, kind, aggregation_evidence)
              VALUES (@operation, @version, 'Test operation', 'operation', '{}');
            INSERT INTO pilot.product_route_step (
              occurrence_id, version_id, product_id, operation_id, position, duration_hours, evidence)
              SELECT gen_random_uuid(), @version, id, @operation, position,
                CASE WHEN name = 'Product 1' THEN 2.5 ELSE 3.25 END, '{}'
              FROM pilot.product CROSS JOIN generate_series(1, 2) AS position
              WHERE version_id = @version AND name IN ('Product 1', 'Product 2');
            """);
        update.Parameters.AddWithValue("version", seeded.Version);
        update.Parameters.AddWithValue("family", seeded.Families[0]);
        update.Parameters.AddWithValue("operation", Guid.NewGuid());
        await update.ExecuteNonQueryAsync();
        using var factory = CreateFactory();
        using var client = Client(factory, seeded.Owner);
        using var response = await client.GetAsync(seeded.Path + "/family-matrix");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var products = body.RootElement.GetProperty("products").EnumerateArray().Where(product =>
            product.GetProperty("familyId").GetGuid() == seeded.Families[0]).ToArray();
        Assert.Collection(products, first =>
        {
            Assert.Equal("5", first.GetProperty("routeDurationHours").GetString());
            Assert.Equal(2, first.GetProperty("operationOccurrenceCount").GetInt32());
            Assert.Equal("49.6", first.GetProperty("marginIncome").GetString());
            Assert.Equal(JsonValueKind.Null, first.GetProperty("marginIncomePerUnit").ValueKind);
        }, second => Assert.Equal("6.5", second.GetProperty("routeDurationHours").GetString()));
        var cycle = body.RootElement.GetProperty("cycles").EnumerateArray().Single(item =>
            item.GetProperty("familyId").GetGuid() == seeded.Families[0]);
        Assert.Equal("30", cycle.GetProperty("cycleDifferencePercent").GetString());
        Assert.Equal("exceedsRecommendation", cycle.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DirectSelection_DominanceAcceptsDoubleButRejectsExactlyTwentyPercent()
    {
        var seeded = await SeedAsync();
        await using var update = database.DataSource.CreateCommand("""
            UPDATE pilot.analysis_version SET nomenclature_complete = true, revision = revision + 1,
              completeness_evidence = '{"source":"Registry","assessedOn":"2026-11-01"}' WHERE id = @version;
            UPDATE pilot.product SET revenue = CASE name WHEN 'Product 1' THEN 200
              WHEN 'Product 2' THEN 100 WHEN 'Product 3' THEN 80 ELSE 20 END WHERE version_id = @version;
            """);
        update.Parameters.AddWithValue("version", seeded.Version);
        await update.ExecuteNonQueryAsync();
        using var factory = CreateFactory();
        using var client = Client(factory, seeded.Owner);
        using var response = await client.GetAsync(seeded.Path + "/direct-selection-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var options = body.RootElement.GetProperty("options").EnumerateArray()
            .Where(option => option.GetProperty("mode").GetString() == "dominantProduct")
            .ToDictionary(option => option.GetProperty("familyId").GetGuid());
        Assert.Equal("eligible", options[seeded.Families[0]].GetProperty("status").GetString());
        Assert.Equal("ineligible", options[seeded.Families[2]].GetProperty("status").GetString());
    }

    [Fact]
    public async Task DirectSelection_MonoproductRequiresKnownReconciliationOfOtherRevenue()
    {
        var seeded = await SeedAsync();
        await using var update = database.DataSource.CreateCommand("""
            UPDATE pilot.analysis_version SET nomenclature_complete = true, revision = revision + 1,
              completeness_evidence = '{"source":"Registry","assessedOn":"2026-11-01"}' WHERE id = @version;
            UPDATE pilot.product SET is_other = true WHERE version_id = @version AND name = 'Product 4';
            DELETE FROM pilot.family_product WHERE version_id = @version AND product_id IN (
              SELECT id FROM pilot.product WHERE version_id = @version AND is_other);
            UPDATE pilot.family_product SET family_id = @family WHERE version_id = @version;
            DELETE FROM pilot.family WHERE version_id = @version AND id <> @family;
            """);
        update.Parameters.AddWithValue("version", seeded.Version);
        update.Parameters.AddWithValue("family", seeded.Families[0]);
        await update.ExecuteNonQueryAsync();
        using var factory = CreateFactory();
        using var client = Client(factory, seeded.Owner);
        var path = seeded.Path + "/direct-selection-options";
        Assert.Equal("eligible", await StatusAsync());

        await using var clear = database.DataSource.CreateCommand(
            "UPDATE pilot.product SET revenue = NULL WHERE version_id = $1 AND is_other");
        clear.Parameters.Add(new Npgsql.NpgsqlParameter<Guid> { TypedValue = seeded.Version });
        await clear.ExecuteNonQueryAsync();
        Assert.Equal("unknown", await StatusAsync());

        async Task<string?> StatusAsync()
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            return body.RootElement.GetProperty("options").EnumerateArray()
                .Single(option => option.GetProperty("mode").GetString() == "monoproduct")
                .GetProperty("status").GetString();
        }
    }

    [Fact]
    public async Task Validation_ReturnsMissingProtocolAndSelectionWithCurrentVersionEtag()
    {
        var seeded = await SeedAsync();
        using var factory = CreateFactory();
        using var client = Client(factory, seeded.Owner);
        using var response = await client.GetAsync(seeded.Path + "/validation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"\"v:{seeded.Version:D}:r:1\"", response.Headers.ETag?.Tag);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.False(body.RootElement.GetProperty("canFinalize").GetBoolean());
        Assert.False(body.RootElement.GetProperty("canExportComplete").GetBoolean());
        var paths = body.RootElement.GetProperty("issues").EnumerateArray()
            .Select(item => item.GetProperty("path").GetString()).ToArray();
        Assert.Contains("/protocol/protocolNumber", paths);
        Assert.Contains("/selection", paths);
    }

    [Fact]
    public async Task Evaluation_RanksCalculatedLaborButLeavesNegativeMaximumMarginUnresolved()
    {
        var seeded = await SeedAsync();
        await using var update = database.DataSource.CreateCommand("""
            UPDATE pilot.product SET raw_material_cost = revenue + 1 WHERE version_id = @version;
            UPDATE pilot.family_metrics SET labor_mode = 'calculated', direct_labor_hours = 4, output_quantity = 2,
                labor_hours_per_unit = NULL, labor_base_quantity = NULL
            WHERE family_id = @family;
            """);
        update.Parameters.AddWithValue("version", seeded.Version);
        update.Parameters.AddWithValue("family", seeded.Families[0]);
        await update.ExecuteNonQueryAsync();
        using var factory = CreateFactory();
        using var client = Client(factory, seeded.Owner);
        using var response = await client.GetAsync(seeded.Path + "/evaluation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var evaluation = body.RootElement;
        Assert.False(evaluation.GetProperty("canRecommend").GetBoolean());
        var first = evaluation.GetProperty("families").EnumerateArray().Single(family =>
            family.GetProperty("familyId").GetGuid() == seeded.Families[0]);
        var scores = first.GetProperty("scores").EnumerateArray().ToDictionary(score => score.GetProperty("criterion").GetString()!);
        Assert.Equal("unresolved", scores["margin"].GetProperty("status").GetString());
        Assert.Equal("2", scores["labor"].GetProperty("value").GetString());
        Assert.Equal("2", scores["labor"].GetProperty("score").GetString());
        Assert.Equal("complete", scores["labor"].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("totalScore").ValueKind);
    }

    private async Task<(Guid Version, string Path, string Owner, Guid[] Families)> SeedAsync()
    {
        var analysis = Guid.NewGuid();
        var version = Guid.NewGuid();
        var owner = Guid.NewGuid().ToString("N");
        var families = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        await using var seed = database.DataSource.CreateCommand("""
            INSERT INTO pilot.analysis (id, owner_subject) VALUES (@analysis, @owner);
            INSERT INTO pilot.analysis_version (
              id, analysis_id, version_number, rules_version, title, total_revenue,
              financial_start, financial_end, labor_start, labor_end, forecast_start, forecast_end,
              activities_start, activities_end, project_start_date, accounting_basis, revenue_evidence)
            VALUES (@version, @analysis, 1, 'fck-2026-tz-v1', 'Evaluation test', 400,
              '2025-11-01', '2026-10-31', '2026-05-01', '2026-10-31', '2026-11-01', '2027-10-31',
              '2026-11-01', '2027-10-31', '2026-11-01', 'Common accounting basis',
              '{"source":"Ledger","period":{"start":"2025-11-01","end":"2026-10-31"}}');
            INSERT INTO pilot.family (id, version_id, name, confirmed, confirmation_evidence)
              SELECT (@families)[n], @version, 'Family ' || n, true, '{}' FROM generate_series(1, 4) AS n;
            INSERT INTO pilot.product (id, version_id, name, unit_code, revenue, raw_material_cost,
              calendar_duration_days, in_production_plan, financial_evidence, duration_evidence, plan_evidence)
              SELECT (@products)[n], @version, 'Product ' || n, 'piece', (ARRAY[160,120,80,40])[n],
                (ARRAY[110.4,78,60,27.6])[n], (ARRAY[20,25,30,36])[n], CASE WHEN n = 4 THEN NULL ELSE true END,
                '{}', '{}', '{}'
              FROM generate_series(1, 4) AS n;
            INSERT INTO pilot.family_product (version_id, family_id, product_id)
              SELECT @version, (@families)[n], (@products)[n] FROM generate_series(1, 4) AS n;
            INSERT INTO pilot.family_metrics (family_id, version_id, sales_growth_percent, sales_growth_evidence,
              load_percent, load_evidence, production_type, production_type_evidence, labor_mode,
              labor_hours_per_unit, labor_unit_code, labor_base_quantity, labor_evidence)
              SELECT (@families)[n], @version, (ARRAY[35,20,5,100])[n],
                '{"source":"Forecast","period":{"start":"2026-11-01","end":"2027-10-31"}}',
                (ARRAY[65,90,40,5])[n], '{"source":"Load","assessedOn":"2026-11-01"}',
                (ARRAY['largeSeries','mediumSeries','smallSeries','largeSeries'])[n],
                '{"source":"Expert","assessedOn":"2026-11-01"}',
                'manual', (ARRAY[2,1,3,0])[n], 'piece', 1,
                '{"source":"Time sheet","period":{"start":"2026-05-01","end":"2026-10-31"}}'
              FROM generate_series(1, 4) AS n;
            """);
        seed.Parameters.AddWithValue("analysis", analysis);
        seed.Parameters.AddWithValue("version", version);
        seed.Parameters.AddWithValue("owner", owner);
        seed.Parameters.AddWithValue("families", families);
        seed.Parameters.AddWithValue("products", families.Select(_ => Guid.NewGuid()).ToArray());
        await seed.ExecuteNonQueryAsync();
        return (version, $"/api/v1/analyses/{analysis:D}/versions/{version:D}", owner, families);
    }

    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("ConnectionStrings:PilotFlow", database.ConnectionString);
        builder.ConfigureTestServices(services => services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName, static _ => { }));
    });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string owner)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.SubjectHeader, owner);
        return client;
    }
}
