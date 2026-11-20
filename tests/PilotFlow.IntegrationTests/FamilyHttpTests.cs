using System.Net;
using System.Text.Json.Nodes;

namespace PilotFlow.IntegrationTests;

public sealed partial class CatalogHttpTests
{
    [Fact]
    public async Task Families_CompareRepeatedEffectiveWorkplacesAndInvalidateConfirmationOnChanges()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        using var foreignClient = Client(factory);
        var version = await CreateVersionAsync(client);
        var productPath = version.Path + "/products";
        var familyPath = version.Path + "/families";
        var operation = await SendAsync(client, HttpMethod.Post, version.Path + "/operations", Operation("Work"),
            version.Etag, HttpStatusCode.Created);
        var operationId = operation.Body.GetProperty("id").GetGuid();
        var first = await SendAsync(client, HttpMethod.Post, productPath, Product("First", null),
            operation.Etag, HttpStatusCode.Created);
        var firstId = first.Body.GetProperty("id").GetGuid();
        var second = await SendAsync(client, HttpMethod.Post, productPath, Product("Second", null),
            first.Etag, HttpStatusCode.Created);
        var secondId = second.Body.GetProperty("id").GetGuid();
        var firstRoute = await SendAsync(client, HttpMethod.Put, productPath + "/" + firstId + "/route",
            Route(Step(Guid.NewGuid(), operationId, 1), Step(Guid.NewGuid(), operationId, 2)),
            second.Etag, HttpStatusCode.OK);
        var secondRoute = await SendAsync(client, HttpMethod.Put, productPath + "/" + secondId + "/route",
            Route(Step(Guid.NewGuid(), operationId, 1), Step(Guid.NewGuid(), operationId, 2, "Different bench")),
            firstRoute.Etag, HttpStatusCode.OK);

        var suggestions = await GetAsync(client, version.Path + "/family-suggestions");
        Assert.Equal(secondRoute.Etag, suggestions.Etag);
        var groups = suggestions.Body.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal(2, groups.Length);
        Assert.All(groups, group =>
        {
            Assert.Single(group.GetProperty("productIds").EnumerateArray());
            Assert.All(group.GetProperty("operationIdsInOrder").EnumerateArray(), id => Assert.Equal(operationId, id.GetGuid()));
            Assert.Equal(2, group.GetProperty("operationIdsInOrder").GetArrayLength());
        });

        var familyStep1 = Guid.NewGuid();
        var familyStep2 = Guid.NewGuid();
        var route = Route(Step(familyStep1, operationId, 1), Step(familyStep2, operationId, 2, "Bench"));
        await ProblemAsync(client, HttpMethod.Post, familyPath, HttpStatusCode.UnprocessableEntity,
            secondRoute.Etag, Family(true, route));
        var draft = await SendAsync(client, HttpMethod.Post, familyPath, Family(false, route), secondRoute.Etag, HttpStatusCode.Created);
        var familyId = draft.Body.GetProperty("id").GetGuid();
        var item = familyPath + "/" + familyId;
        await ProblemAsync(foreignClient, HttpMethod.Get, item, HttpStatusCode.NotFound);
        var options = await GetAsync(client, productPath + "/" + secondId + "/assignment-options");
        Assert.False(options.Body.GetProperty("options")[0].GetProperty("canAssign").GetBoolean());
        var assignments = version.Path + "/assignments";
        var rejected = await SendAsync(client, HttpMethod.Put, assignments,
            Assign((firstId, familyId), (secondId, familyId)), draft.Etag, HttpStatusCode.UnprocessableEntity);
        Assert.Contains(rejected.Body.GetProperty("issues").EnumerateArray(),
            issue => issue.GetProperty("code").GetString() == "ROUTE_INCOMPATIBLE");
        var unchanged = await GetAsync(client, item);
        Assert.Equal(draft.Etag, unchanged.Etag);
        Assert.Empty(unchanged.Body.GetProperty("productIds").EnumerateArray());

        var assigned = await SendAsync(client, HttpMethod.Put, assignments, Assign((firstId, familyId)),
            draft.Etag, HttpStatusCode.OK);
        var confirmed = await SendAsync(client, HttpMethod.Put, item, Family(true, route), assigned.Etag, HttpStatusCode.OK);
        Assert.True(confirmed.Body.GetProperty("data").GetProperty("confirmed").GetBoolean());
        await using (var seed = database.DataSource.CreateCommand("""
            INSERT INTO pilot.family_metrics (
                family_id,version_id,sales_growth_percent,sales_growth_evidence,load_percent,load_evidence,
                production_type,production_type_evidence,labor_mode,labor_hours_per_unit,labor_evidence)
            VALUES (@family,@version,12,'{}',70,'{}','largeSeries','{}','manual',4,'{}')
            """))
        {
            seed.Parameters.AddWithValue("family", familyId);
            seed.Parameters.AddWithValue("version", version.Id);
            await seed.ExecuteNonQueryAsync();
        }

        var modified = Route(Step(familyStep1, operationId, 1), Step(familyStep2, operationId, 2));
        var changed = await SendAsync(client, HttpMethod.Put, item, Family(true, modified), confirmed.Etag, HttpStatusCode.OK);
        Assert.False(changed.Body.GetProperty("data").GetProperty("confirmed").GetBoolean());
        await AssertFamilyMetricsAsync(familyId, routeChanged: true);
        var list = await GetAsync(client, familyPath + "?q=family&limit=1");
        Assert.Equal(changed.Etag, list.Etag);
        Assert.Equal(familyId, list.Body.GetProperty("items")[0].GetProperty("id").GetGuid());
        await ProblemAsync(client, HttpMethod.Delete, item, HttpStatusCode.Conflict, changed.Etag);
        var removed = await SendAsync(client, HttpMethod.Put, assignments, Assign((firstId, null)),
            changed.Etag, HttpStatusCode.OK);
        await AssertFamilyMetricsAsync(familyId, routeChanged: false);
        await SendAsync(client, HttpMethod.Delete, item, null, removed.Etag, HttpStatusCode.NoContent);
    }

    private async Task AssertFamilyMetricsAsync(Guid familyId, bool routeChanged)
    {
        await using var probe = database.DataSource.CreateCommand("""
            SELECT sales_growth_percent, load_percent, labor_hours_per_unit,
                sales_growth_evidence->>'missingReason', load_evidence->>'missingReason', labor_evidence->>'missingReason'
            FROM pilot.family_metrics WHERE family_id=@family
            """);
        probe.Parameters.AddWithValue("family", familyId);
        await using var result = await probe.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.True(result.IsDBNull(1));
        Assert.True(result.IsDBNull(2));
        if (routeChanged)
        {
            Assert.Equal(12, result.GetDecimal(0));
            Assert.True(result.IsDBNull(3));
            Assert.Equal("ROUTE_CHANGED", result.GetString(4));
            Assert.Equal("ROUTE_CHANGED", result.GetString(5));
        }
        else
        {
            Assert.True(result.IsDBNull(0));
            Assert.Equal("COMPOSITION_CHANGED", result.GetString(3));
            Assert.Equal("COMPOSITION_CHANGED", result.GetString(4));
            Assert.Equal("COMPOSITION_CHANGED", result.GetString(5));
        }
    }

    private static object Family(bool confirmed, object route) => new
    {
        name = "Family",
        startBoundary = "Start",
        endBoundary = "End",
        compositionReason = (string?)null,
        confirmed,
        confirmationEvidence = JsonNode.Parse(Evidence),
        route,
    };

    private static object Assign(params (Guid ProductId, Guid? FamilyId)[] assignments) => new
    {
        assignments = assignments.Select(item => new { productId = item.ProductId, familyId = item.FamilyId }).ToArray(),
        reason = "Test",
    };
}
