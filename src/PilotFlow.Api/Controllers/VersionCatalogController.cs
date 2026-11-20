using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Authentication;
using PilotFlow.Api.Concurrency;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;

namespace PilotFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthenticationExtensions.AnalysisUserPolicy)]
public abstract class VersionCatalogController : ControllerBase
{
    protected VersionScope Scope(Guid analysisId, Guid versionId) =>
        new(User.FindFirst("sub")!.Value, analysisId, versionId);

    protected CatalogChange Change(Guid analysisId, Guid versionId)
    {
        var etag = (AggregateEtag)HttpContext.Items[AggregateEtag.HttpContextKey]!;
        return new(Scope(analysisId, versionId), etag.VersionId, etag.Revision);
    }

    protected IResult Read<T>(Guid versionId, Versioned<T>? version, Func<T, IResult> response)
    {
        if (version is null)
        {
            return ProblemResults.NotFound(HttpContext);
        }

        Response.Headers.ETag = new AggregateEtag(versionId, version.Revision).ToString();
        return response(version.Value);
    }

    protected IResult Write<T>(
        Guid versionId, AnalysisWriteResult<Versioned<T>> result, Func<T, IResult> response) => result.Status switch
        {
            AnalysisWriteStatus.Success => Read(versionId, result.Value, response),
            AnalysisWriteStatus.NotFound => ProblemResults.NotFound(HttpContext),
            AnalysisWriteStatus.Fixed => ProblemResults.FixedVersion(HttpContext),
            AnalysisWriteStatus.RevisionConflict => ProblemResults.RevisionConflict(HttpContext),
            AnalysisWriteStatus.StateConflict => ProblemResults.LinkedResourceConflict(HttpContext),
            AnalysisWriteStatus.ValidationFailed => ProblemResults.ValidationFailed(HttpContext,
                result.Issues!.Select(issue =>
                    new ApiIssue(issue.Code, "error", issue.Path, issue.Message, issue.Suggestion, issue.EntityId)).ToArray()),
            _ => throw new InvalidOperationException("Unknown catalog write status."),
        };
}
