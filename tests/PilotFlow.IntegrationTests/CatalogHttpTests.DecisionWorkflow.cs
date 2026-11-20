using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PilotFlow.IntegrationTests;

public sealed partial class CatalogHttpTests
{
    [Fact]
    public async Task Finalize_ConcurrentControlExampleCommitsOneImmutableDecision()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        using var foreignClient = Client(factory);
        var ready = await CreateReadyControlExampleAsync(client);
        var draft = await GetAsync(client, ready.VersionPath);
        var expectedRevision = draft.Body.GetProperty("revision").GetInt64() + 1;
        var validation = await GetAsync(client, ready.VersionPath + "/validation");
        Assert.True(validation.Body.GetProperty("canFinalize").GetBoolean(), validation.Body.GetRawText());
        var preview = await GetAsync(client, ready.VersionPath + "/protocol-preview");
        Assert.Equal(ready.Etag, preview.Etag);
        Assert.True(preview.Body.GetProperty("ready").GetBoolean(), preview.Body.GetRawText());
        Assert.Equal(expectedRevision - 1, preview.Body.GetProperty("revision").GetInt64());
        var previewSnapshot = preview.Body.GetProperty("snapshot");
        Assert.Equal(expectedRevision - 1, previewSnapshot.GetProperty("familyMatrix").GetProperty("revision").GetInt64());
        Assert.Equal(ready.FamilyId, previewSnapshot.GetProperty("selection").GetProperty("familyId").GetGuid());
        Assert.Equal("2026-11-18", previewSnapshot.GetProperty("protocol").GetProperty("protocolDate").GetString());

        using var first = FinalizeRequest(ready.VersionPath, ready.Etag);
        using var second = FinalizeRequest(ready.VersionPath, ready.Etag);
        var responses = await Task.WhenAll(client.SendAsync(first, CancellationToken.None),
            client.SendAsync(second, CancellationToken.None));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed);
        var decision = await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var snapshot = decision.GetProperty("snapshot");
        var version = snapshot.GetProperty("version");
        var versionId = version.GetProperty("id").GetGuid();
        Assert.Equal($"\"v:{versionId:D}:r:{expectedRevision}\"", created.Headers.ETag?.Tag);
        Assert.Equal(expectedRevision, version.GetProperty("revision").GetInt64());
        Assert.Equal("fixed", version.GetProperty("state").GetString());
        Assert.Equal(expectedRevision, snapshot.GetProperty("familyMatrix").GetProperty("revision").GetInt64());
        Assert.Equal(expectedRevision, snapshot.GetProperty("evaluation").GetProperty("revision").GetInt64());
        Assert.Equal(ready.FamilyId, snapshot.GetProperty("selection").GetProperty("familyId").GetGuid());
        var chosen = Assert.Single(snapshot.GetProperty("evaluation").GetProperty("families").EnumerateArray(),
            family => family.GetProperty("familyId").GetGuid() == ready.FamilyId);
        Assert.Equal("13", chosen.GetProperty("totalScore").GetString());
        Assert.Equal("2026-11-18", snapshot.GetProperty("protocol").GetProperty("protocolDate").GetString());
        Assert.Equal(client.DefaultRequestHeaders.GetValues(
            PilotFlow.Api.Tests.Infrastructure.TestAuthenticationHandler.SubjectHeader).Single(),
            decision.GetProperty("createdBySubject").GetString());

        var hash = decision.GetProperty("snapshotSha256").GetString();
        Assert.Matches("^[a-f0-9]{64}$", hash!);
        var id = decision.GetProperty("id").GetGuid();
        var decisionPath = ready.AnalysisPath + "/decisions/" + id;
        var persisted = await GetAsync(client, decisionPath);
        Assert.Equal($"\"sha256:{hash}\"", persisted.Etag);
        Assert.True(JsonElement.DeepEquals(snapshot, persisted.Body.GetProperty("snapshot")));
        var list = await GetAsync(client, ready.AnalysisPath + "/decisions");
        Assert.Equal(id, Assert.Single(list.Body.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        await ProblemAsync(foreignClient, HttpMethod.Get, decisionPath, HttpStatusCode.NotFound);

        var fork = await SendAsync(client, HttpMethod.Post, ready.VersionPath + "/fork",
            new { reason = "Новый цикл анализа" }, created.Headers.ETag?.Tag, HttpStatusCode.Created);
        Assert.Equal(2, fork.Body.GetProperty("versionNumber").GetInt32());
        Assert.Equal(1, fork.Body.GetProperty("revision").GetInt64());
        var repeat = await GetAsync(client, decisionPath);
        Assert.Equal(persisted.Etag, repeat.Etag);
        Assert.True(JsonElement.DeepEquals(persisted.Body, repeat.Body));
    }

    private static HttpRequestMessage FinalizeRequest(string path, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path + "/finalize");
        request.Headers.IfMatch.ParseAdd(etag);
        return request;
    }

    private static async Task<(string AnalysisPath, string VersionPath, string Etag, Guid FamilyId)>
        CreateReadyControlExampleAsync(HttpClient client)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "control-example.json"),
            Encoding.UTF8, CancellationToken.None);
        var example = JsonNode.Parse(json)!;
        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/analyses",
            new { data = example["version"]!["data"] }, null, HttpStatusCode.Created);
        var analysisPath = "/api/v1/analyses/" + created.Body.GetProperty("id").GetGuid();
        var versionPath = analysisPath + "/versions/" + created.Body.GetProperty("version").GetProperty("id").GetGuid();
        var etag = created.Etag!;
        await ProblemAsync(client, HttpMethod.Get, versionPath + "/protocol-preview", HttpStatusCode.UnprocessableEntity);
        await ProblemAsync(client, HttpMethod.Post, versionPath + "/finalize", HttpStatusCode.UnprocessableEntity, etag);

        var operations = new Dictionary<Guid, Guid>();
        foreach (var operation in example["operations"]!.AsArray())
        {
            var added = await SendAsync(client, HttpMethod.Post, versionPath + "/operations",
                operation!["data"], etag, HttpStatusCode.Created);
            operations.Add(FixtureId(operation["id"]), added.Body.GetProperty("id").GetGuid());
            etag = added.Etag!;
        }

        var products = new Dictionary<Guid, Guid>();
        foreach (var product in example["products"]!.AsArray())
        {
            var added = await SendAsync(client, HttpMethod.Post, versionPath + "/products",
                product!["data"], etag, HttpStatusCode.Created);
            products.Add(FixtureId(product["id"]), added.Body.GetProperty("id").GetGuid());
            etag = added.Etag!;
        }

        foreach (var route in example["productRoutes"]!.AsArray())
        {
            var productId = products[FixtureId(route!["productId"])];
            var updated = await SendAsync(client, HttpMethod.Put, $"{versionPath}/products/{productId:D}/route",
                RemapRoute(route["route"]!, operations), etag, HttpStatusCode.OK);
            etag = updated.Etag!;
        }

        var assignments = new JsonArray();
        var families = new Dictionary<Guid, (Guid Id, JsonObject Data)>();
        foreach (var family in example["families"]!.AsArray())
        {
            var data = family!["data"]!.DeepClone().AsObject();
            data["confirmed"] = false;
            data["route"] = RemapRoute(data["route"]!, operations);
            var added = await SendAsync(client, HttpMethod.Post, versionPath + "/families", data, etag, HttpStatusCode.Created);
            var familyId = added.Body.GetProperty("id").GetGuid();
            families.Add(FixtureId(family["id"]), (familyId, data));
            etag = added.Etag!;
            foreach (var product in family["productIds"]!.AsArray())
            {
                assignments.Add(new JsonObject
                {
                    ["productId"] = products[FixtureId(product)].ToString("D"),
                    ["familyId"] = familyId.ToString("D"),
                });
            }
        }

        var assigned = await SendAsync(client, HttpMethod.Put, versionPath + "/assignments",
            new { assignments, reason = "Контрольный пример" }, etag, HttpStatusCode.OK);
        etag = assigned.Etag!;
        foreach (var family in families.Values)
        {
            family.Data["confirmed"] = true;
            var confirmed = await SendAsync(client, HttpMethod.Put, $"{versionPath}/families/{family.Id:D}",
                family.Data, etag, HttpStatusCode.OK);
            Assert.True(confirmed.Body.GetProperty("data").GetProperty("confirmed").GetBoolean());
            etag = confirmed.Etag!;
        }

        foreach (var metric in example["metrics"]!.AsArray())
        {
            var familyId = families[FixtureId(metric!["familyId"])].Id;
            var data = metric["data"]!.AsObject();
            data["loadActual"] = null;
            data["loadCapacity"] = null;
            data["loadUnit"] = null;
            data["directLaborHours"] = null;
            data["outputQuantity"] = null;
            var updated = await SendAsync(client, HttpMethod.Put, $"{versionPath}/families/{familyId:D}/metrics",
                data, etag, HttpStatusCode.OK);
            etag = updated.Etag!;
        }

        var selection = example["selection"]!.AsObject();
        var selected = families[FixtureId(selection["familyId"])].Id;
        selection["familyId"] = selected.ToString("D");
        var chosen = await SendAsync(client, HttpMethod.Put, versionPath + "/selection", selection, etag, HttpStatusCode.OK);
        var draftPreview = await GetAsync(client, versionPath + "/protocol-preview");
        Assert.Equal(chosen.Etag, draftPreview.Etag);
        Assert.False(draftPreview.Body.GetProperty("ready").GetBoolean());
        Assert.Equal(JsonValueKind.Null, draftPreview.Body.GetProperty("snapshot").GetProperty("protocol")
            .GetProperty("protocolDate").ValueKind);
        var protocol = await SendAsync(client, HttpMethod.Put, versionPath + "/protocol",
            example["protocol"], chosen.Etag, HttpStatusCode.OK);
        return (analysisPath, versionPath, protocol.Etag!, selected);
    }

    private static JsonNode RemapRoute(JsonNode route, Dictionary<Guid, Guid> operations)
    {
        var copy = route.DeepClone();
        foreach (var step in copy["steps"]!.AsArray())
        {
            step!["occurrenceId"] = Guid.NewGuid().ToString("D");
            step["operationId"] = operations[FixtureId(step["operationId"])].ToString("D");
        }

        return copy;
    }

    private static Guid FixtureId(JsonNode? value) => Guid.Parse(value!.GetValue<string>());
}
