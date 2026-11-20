using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PilotFlow.Api.Concurrency;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Decisions;

namespace PilotFlow.Api.Controllers;

[Route("/api/v1/analyses/{analysisId}")]
public sealed class DecisionExportController(DecisionExport export) : VersionCatalogController
{
    [HttpGet("versions/{versionId}/export", Name = "exportDraft")]
    public async Task<IResult> ExportDraftAsync(
        Guid analysisId, Guid versionId, [FromQuery] DraftExportQuery query, CancellationToken cancellationToken)
    {
        var result = await export.GetDraftAsync(Scope(analysisId, versionId), query.Kind, query.Draft, cancellationToken);
        return Export(result, versionId);
    }

    [HttpGet("decisions/{decisionId}/export", Name = "exportDecision")]
    public async Task<IResult> ExportDecisionAsync(
        Guid analysisId, Guid decisionId, [FromQuery] DecisionExportQuery query, CancellationToken cancellationToken)
    {
        var result = await export.GetDecisionAsync(User.FindFirst("sub")!.Value, analysisId, decisionId, query.Kind, cancellationToken);
        return Export(result, null);
    }

    private IResult Export(ExportedDecision? result, Guid? versionId)
    {
        if (result is null)
        {
            return ProblemResults.NotFound(HttpContext);
        }

        if (result.Content is null)
        {
            return ProblemResults.ValidationFailed(HttpContext, result.Issues.Select(issue =>
                new ApiIssue(issue.Code, issue.Severity, issue.Path, issue.Message, issue.Suggestion, issue.EntityId)).ToArray());
        }

        Response.Headers.ETag = result.SnapshotHash is { } hash
            ? DecisionEtag.Format(hash)
            : new AggregateEtag(versionId!.Value, result.Revision!.Value).ToString();
        return Results.File(result.Content, "application/pdf", "pilot-selection.pdf");
    }
}

public class DecisionExportQuery
{
    [Required]
    [RegularExpression("bundle|protocol|familyMatrix|selectionMatrix")]
    public string Document { get; init; } = "bundle";

    internal DecisionDocument Kind => Document switch
    {
        "protocol" => DecisionDocument.Protocol,
        "familyMatrix" => DecisionDocument.FamilyMatrix,
        "selectionMatrix" => DecisionDocument.SelectionMatrix,
        _ => DecisionDocument.Bundle,
    };
}

public sealed class DraftExportQuery : DecisionExportQuery
{
    public bool Draft { get; init; } = true;
}
