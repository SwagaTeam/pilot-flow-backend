using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Authentication;
using PilotFlow.Api.Concurrency;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Analyses;
using PilotFlow.Domain.Analyses;

namespace PilotFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationExtensions.AnalysisUserPolicy)]
[Route("/api/v1/analyses")]
public sealed class AnalysesController(IAnalysisStore store, AnalysisCommands commands) : ControllerBase
{
    private string Owner => User.FindFirst("sub")!.Value;

    [HttpPost(Name = "createAnalysis")]
    public async Task<IResult> CreateAsync(CreateAnalysisRequest request, CancellationToken cancellationToken)
    {
        var result = await commands.CreateAsync(Owner, request.Data, cancellationToken);
        if (result.Value is not { } analysis)
        {
            return WriteProblem(result, "/data");
        }

        SetEtag(analysis.Version);
        var location = Url.RouteUrl("getAnalysis", new { analysisId = analysis.Id });
        return Results.Created(location, analysis);
    }

    [HttpGet(Name = "listAnalyses")]
    public async Task<IResult> ListAsync([FromQuery] PageQuery query, CancellationToken cancellationToken)
    {
        var page = await store.ListAsync(Owner, query.ToPage(), cancellationToken);
        return Results.Ok(page);
    }

    [HttpGet("{analysisId}", Name = "getAnalysis")]
    public async Task<IResult> GetAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var analysis = await store.GetAsync(Owner, analysisId, cancellationToken);
        if (analysis is null)
        {
            return ProblemResults.NotFound(HttpContext);
        }

        SetEtag(analysis.Version);
        return Results.Ok(analysis);
    }

    [HttpGet("{analysisId}/versions", Name = "listVersions")]
    public async Task<IResult> ListVersionsAsync(
        Guid analysisId, [FromQuery] PageQuery query, CancellationToken cancellationToken)
    {
        var page = await store.ListVersionsAsync(Owner, analysisId, query.ToPage(), cancellationToken);
        return page is null ? ProblemResults.NotFound(HttpContext) : Results.Ok(page);
    }

    [HttpGet("{analysisId}/versions/{versionId}", Name = "getVersion")]
    public async Task<IResult> GetVersionAsync(Guid analysisId, Guid versionId, CancellationToken cancellationToken)
    {
        var version = await store.GetVersionAsync(Owner, analysisId, versionId, cancellationToken);
        return VersionResponse(version);
    }

    [RequireIfMatch]
    [HttpPut("{analysisId}/versions/{versionId}", Name = "replaceVersionData")]
    public async Task<IResult> ReplaceVersionAsync(
        Guid analysisId, Guid versionId, AnalysisData data, CancellationToken cancellationToken)
    {
        var etag = (AggregateEtag)HttpContext.Items[AggregateEtag.HttpContextKey]!;
        var target = new VersionUpdate(analysisId, versionId, etag.VersionId, etag.Revision);
        var result = await commands.ReplaceAsync(Owner, target, data, cancellationToken);
        return result.Value is { } version ? VersionResponse(version) : WriteProblem(result);
    }

    private IResult VersionResponse(AnalysisVersionDetails? version)
    {
        if (version is null)
        {
            return ProblemResults.NotFound(HttpContext);
        }

        SetEtag(version);
        return Results.Ok(version);
    }

    private void SetEtag(AnalysisVersionDetails version) =>
        Response.Headers.ETag = new AggregateEtag(version.Id, version.Revision).ToString();

    private ApiProblemResult WriteProblem<T>(AnalysisWriteResult<T> result, string pathPrefix = "") => result.Status switch
    {
        AnalysisWriteStatus.NotFound => ProblemResults.NotFound(HttpContext),
        AnalysisWriteStatus.Fixed => ProblemResults.FixedVersion(HttpContext),
        AnalysisWriteStatus.RevisionConflict => ProblemResults.RevisionConflict(HttpContext),
        AnalysisWriteStatus.ValidationFailed => ProblemResults.ValidationFailed(HttpContext,
            result.Issues!.Select(issue =>
                new ApiIssue(issue.Code, "error", pathPrefix + issue.Path, issue.Message, issue.Suggestion, null)).ToArray()),
        _ => throw new InvalidOperationException("Expected an analysis write failure."),
    };
}

public sealed class CreateAnalysisRequest
{
    public required AnalysisData Data { get; init; }
}

public class PageQuery
{
    [Range(0, int.MaxValue)]
    public int Offset { get; init; }

    [Range(1, 1000)]
    public int Limit { get; init; } = 100;

    [StringLength(200)]
    [RegularExpression(@"[^\u0000]*")]
    public string? Q { get; init; }

    internal PageRequest ToPage() => new(Offset, Limit, Q);
}
