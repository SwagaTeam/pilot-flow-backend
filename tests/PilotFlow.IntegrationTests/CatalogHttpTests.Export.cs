using System.Net;
using System.Text;
using PdfSharp.Pdf.IO;

namespace PilotFlow.IntegrationTests;

public sealed partial class CatalogHttpTests
{
    [Fact]
    public async Task ExportDraft_WithoutSelectionReturnsOnlyDraftMatrices()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var version = await CreateVersionAsync(client);
        var path = version.Path + "/export";

        using var response = await client.GetAsync(path + "?document=familyMatrix", CancellationToken.None);
        var pdf = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(version.Etag, response.Headers.ETag?.Tag);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Single(document.Pages);
        await ProblemAsync(client, HttpMethod.Get, path, HttpStatusCode.UnprocessableEntity);
        await ProblemAsync(client, HttpMethod.Get, path + "?draft=false&document=familyMatrix", HttpStatusCode.UnprocessableEntity);
        await ProblemAsync(client, HttpMethod.Get, path + "?document=other", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Export_DraftAndDecisionHaveContractHeadersAndStableSavedSnapshot()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        using var foreignClient = Client(factory);
        var ready = await CreateReadyControlExampleAsync(client);
        await AssertPdfAsync(client, ready.VersionPath + "/export?draft=true", ready.Etag);
        await AssertPdfAsync(client, ready.VersionPath + "/export?draft=false", ready.Etag);

        var finalized = await SendAsync(client, HttpMethod.Post, ready.VersionPath + "/finalize", null,
            ready.Etag, HttpStatusCode.Created);
        var id = finalized.Body.GetProperty("id").GetGuid();
        var etag = $"\"sha256:{finalized.Body.GetProperty("snapshotSha256").GetString()}\"";
        var export = $"{ready.AnalysisPath}/decisions/{id:D}/export";
        await AssertPdfAsync(client, export, etag);
        await ProblemAsync(foreignClient, HttpMethod.Get, export, HttpStatusCode.NotFound);

        await SendAsync(client, HttpMethod.Post, ready.VersionPath + "/fork",
            new { reason = "Новый цикл анализа" }, finalized.Etag, HttpStatusCode.Created);
        await AssertPdfAsync(client, export, etag);
    }

    private static async Task AssertPdfAsync(HttpClient client, string path, string etag)
    {
        using var response = await client.GetAsync(path, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(etag, response.Headers.ETag?.Tag);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Equal("pilot-selection.pdf", disposition?.FileNameStar ?? disposition?.FileName?.Trim('"'));
        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        Assert.True(content.AsSpan().StartsWith("%PDF-"u8));
        using var document = PdfReader.Open(new MemoryStream(content), PdfDocumentOpenMode.Import);
        Assert.Equal(3, document.PageCount);
    }
}
