using System.Net;
using System.Text.Json;
using Npgsql;

namespace PilotFlow.IntegrationTests;

public sealed partial class AnalysisHttpTests
{
    [Fact]
    public async Task Fork_CopiesOnlyWorkingDataOnceAndLeavesDecisionImmutable()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var stranger = CreateClient(factory);
        var source = await CreateAsync(client);

        using var draft = await ForkAsync(client, source, "Новый цикл");
        await AssertProblemAsync(draft, HttpStatusCode.Conflict, "STATE_CONFLICT");

        var seed = await FixWithDecisionAsync(source);
        using var stale = await ForkAsync(client, source, "Новый цикл");
        await AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, "REVISION_CONFLICT");
        var fixedVersion = source with { Etag = $"\"v:{source.VersionId:D}:r:2\"" };
        using var foreign = await ForkAsync(stranger, fixedVersion, "Чужой анализ");
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "NOT_FOUND");
        using var blank = await ForkAsync(client, fixedVersion, "   ");
        await AssertProblemAsync(blank, HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED");

        var responses = await Task.WhenAll(ForkAsync(client, fixedVersion, "Новый цикл"),
            ForkAsync(client, fixedVersion, "Новый цикл"));
        using var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        using var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var version = await ReadAsync(created);
        var newId = version.GetProperty("id").GetGuid();
        Assert.NotEqual(source.VersionId, newId);
        Assert.Equal(2, version.GetProperty("versionNumber").GetInt32());
        Assert.Equal(1L, version.GetProperty("revision").GetInt64());
        Assert.Equal("draft", version.GetProperty("state").GetString());
        Assert.Equal("fck-2026-tz-v1", version.GetProperty("rulesVersion").GetString());
        Assert.Equal("Цех Север %", version.GetProperty("data").GetProperty("title").GetString());
        Assert.Equal($"\"v:{newId:D}:r:1\"", created.Headers.ETag?.Tag);

        await using var verify = database.DataSource.CreateCommand("""
            SELECT v.fork_reason, product.id, op.id, family.id, product_step.occurrence_id,
                   family_step.occurrence_id, family.confirmed, family.confirmation_evidence->>'source',
                   metrics.sales_growth_percent, protocol.protocol_number, protocol.protocol_date,
                   (SELECT count(*) FROM pilot.selection WHERE version_id = v.id),
                   (SELECT count(*) FROM pilot.decision WHERE version_id = v.id)
            FROM pilot.analysis_version AS v
            JOIN pilot.family_product AS link ON link.version_id = v.id
            JOIN pilot.product AS product ON product.id = link.product_id AND product.version_id = v.id
            JOIN pilot.family AS family ON family.id = link.family_id AND family.version_id = v.id
            JOIN pilot.product_route_step AS product_step ON product_step.product_id = product.id
            JOIN pilot.family_route_step AS family_step ON family_step.family_id = family.id
            JOIN pilot.operation AS op ON op.id = family_step.operation_id
                                      AND op.id = product_step.operation_id AND op.version_id = v.id
            JOIN pilot.family_metrics AS metrics ON metrics.family_id = family.id
            JOIN pilot.protocol AS protocol ON protocol.version_id = v.id
            WHERE v.id = @newVersion;
            """);
        verify.Parameters.AddWithValue("newVersion", newId);
        await using var copied = await verify.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await copied.ReadAsync(CancellationToken.None));
        Assert.Equal("Новый цикл", copied.GetString(0));
        Assert.NotEqual(seed.Product, copied.GetGuid(1));
        Assert.NotEqual(seed.Operation, copied.GetGuid(2));
        Assert.NotEqual(seed.Family, copied.GetGuid(3));
        Assert.NotEqual(seed.ProductOccurrence, copied.GetGuid(4));
        Assert.NotEqual(seed.FamilyOccurrence, copied.GetGuid(5));
        Assert.False(copied.GetBoolean(6));
        Assert.Equal("Source", copied.GetString(7));
        Assert.Equal(17.25m, copied.GetDecimal(8));
        Assert.Equal("№ 17", copied.GetString(9));
        Assert.True(await copied.IsDBNullAsync(10, CancellationToken.None));
        Assert.Equal(0L, copied.GetInt64(11));
        Assert.Equal(0L, copied.GetInt64(12));
        Assert.False(await copied.ReadAsync(CancellationToken.None));

        using var decision = await client.GetAsync(source.AnalysisPath + "/decisions/" + seed.Decision, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        Assert.Equal($"\"sha256:{new string('a', 64)}\"", decision.Headers.ETag?.Tag);
        Assert.Equal(seed.Family, (await ReadAsync(decision)).GetProperty("snapshot")
            .GetProperty("selection").GetProperty("familyId").GetGuid());
    }

    [Fact]
    public async Task Decisions_SearchImmutableSelectedFamilyAndMaskForeignAnalysis()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var stranger = CreateClient(factory);
        var source = await CreateAsync(client);
        var seed = await FixWithDecisionAsync(source);
        var decisions = source.AnalysisPath + "/decisions";
        using var matching = await client.GetAsync(decisions + "?q=" + Uri.EscapeDataString("СНИМОК %") + "&limit=1",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, matching.StatusCode);
        var page = await ReadAsync(matching);
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("unfilteredTotal").GetInt32());
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(seed.Decision, item.GetProperty("id").GetGuid());
        Assert.Equal("Tester", item.GetProperty("createdByName").GetString());

        using var mismatch = await client.GetAsync(decisions + "?q=Live", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        var empty = await ReadAsync(mismatch);
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Equal(1, empty.GetProperty("unfilteredTotal").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());

        using var foreignList = await stranger.GetAsync(decisions, CancellationToken.None);
        using var foreignDecision = await stranger.GetAsync(decisions + "/" + seed.Decision, CancellationToken.None);
        await AssertProblemAsync(foreignList, HttpStatusCode.NotFound, "NOT_FOUND");
        await AssertProblemAsync(foreignDecision, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task RuntimeRole_CannotChangeFixedRootOrDecision()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var source = await CreateAsync(client);
        var seed = await FixWithDecisionAsync(source);
        await using var runtime = NpgsqlDataSource.Create(database.ConnectionString);

        await using var updateVersion = runtime.CreateCommand("""
            UPDATE pilot.analysis_version SET title = 'Edited', revision = revision + 1 WHERE id = $1
            """);
        updateVersion.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = source.VersionId });
        var fixedError = await Assert.ThrowsAsync<PostgresException>(() => updateVersion.ExecuteNonQueryAsync(CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.CheckViolation, fixedError.SqlState);
        Assert.Equal("version_fixed", fixedError.MessageText);

        await using var updateDecision = runtime.CreateCommand("UPDATE pilot.decision SET snapshot = '{}' WHERE id = $1");
        updateDecision.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = seed.Decision });
        var decisionError = await Assert.ThrowsAsync<PostgresException>(() =>
            updateDecision.ExecuteNonQueryAsync(CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, decisionError.SqlState);
    }

    private static async Task<HttpResponseMessage> ForkAsync(HttpClient client, CreatedAnalysis source, string reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, source.VersionPath + "/fork")
        {
            Content = Json(JsonSerializer.Serialize(new { reason })),
        };
        request.Headers.TryAddWithoutValidation("If-Match", source.Etag);
        return await client.SendAsync(request, CancellationToken.None);
    }

    private async Task<FixedSeed> FixWithDecisionAsync(CreatedAnalysis source)
    {
        var seed = new FixedSeed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var fix = database.DataSource.CreateCommand("""
            INSERT INTO pilot.product (id, version_id, name, unit_code, financial_evidence, duration_evidence, plan_evidence)
            VALUES (@product, @version, 'Product', 'pcs', '{}', '{}', '{}');
            INSERT INTO pilot.operation (id, version_id, name, kind, workplace_name, aggregation_evidence)
            VALUES (@operation, @version, 'Op', 'operation', 'Shop', '{}');
            INSERT INTO pilot.family (id, version_id, name, confirmed, confirmation_evidence)
            VALUES (@family, @version, 'Live name', true, '{"source":"Source"}');
            INSERT INTO pilot.family_product (version_id, product_id, family_id) VALUES (@version, @product, @family);
            INSERT INTO pilot.product_route_step (occurrence_id, version_id, product_id, operation_id, position, evidence)
            VALUES (@productOccurrence, @version, @product, @operation, 1, '{}');
            INSERT INTO pilot.family_route_step (occurrence_id, version_id, family_id, operation_id, position, evidence)
            VALUES (@familyOccurrence, @version, @family, @operation, 1, '{}');
            INSERT INTO pilot.family_metrics (family_id, version_id, sales_growth_percent, sales_growth_evidence,
                    load_evidence, production_type_evidence, labor_mode, labor_evidence)
            VALUES (@family, @version, 17.25, '{}', '{}', '{}', 'manual', '{}');
            INSERT INTO pilot.protocol (version_id, protocol_number, protocol_date)
            VALUES (@version, '№ 17', '2026-11-19');
            INSERT INTO pilot.selection (version_id, family_id, mode, direct_basis_evidence)
            VALUES (@version, @family, 'ranked', '{}');
            INSERT INTO pilot.decision (
                id, version_id, analysis_id, family_id, created_by_subject, created_by_name,
                snapshot_schema_version, rules_version, snapshot, snapshot_sha256)
            VALUES (@decision, @version, @analysis, @family, 'owner', 'Tester', '1.0', 'fck-2026-tz-v1',
                jsonb_build_object('selection', jsonb_build_object('familyId', @family), 'families', jsonb_build_array(
                    jsonb_build_object('id', @family, 'data', jsonb_build_object('name', 'Снимок %')))), repeat('a', 64));
            UPDATE pilot.analysis_version SET state = 'fixed', revision = 2 WHERE id = @version;
            """);
        fix.Parameters.AddWithValue("operation", seed.Operation);
        fix.Parameters.AddWithValue("product", seed.Product);
        fix.Parameters.AddWithValue("family", seed.Family);
        fix.Parameters.AddWithValue("productOccurrence", seed.ProductOccurrence);
        fix.Parameters.AddWithValue("familyOccurrence", seed.FamilyOccurrence);
        fix.Parameters.AddWithValue("decision", seed.Decision);
        fix.Parameters.AddWithValue("version", source.VersionId);
        fix.Parameters.AddWithValue("analysis", source.Id);
        await fix.ExecuteNonQueryAsync(CancellationToken.None);
        return seed;
    }

    private sealed record FixedSeed(Guid Operation, Guid Product, Guid Family, Guid ProductOccurrence, Guid FamilyOccurrence,
        Guid Decision);
}
