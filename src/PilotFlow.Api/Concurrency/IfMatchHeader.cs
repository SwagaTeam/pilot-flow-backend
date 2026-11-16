using Microsoft.AspNetCore.Mvc.Filters;
using PilotFlow.Api.Errors;

namespace PilotFlow.Api.Concurrency;

internal static class IfMatchHeader
{
    public static ApiProblemResult? Read(HttpContext context)
    {
        var values = context.Request.Headers.IfMatch;
        if (values.Count == 0)
        {
            return ProblemResults.EtagRequired(context);
        }

        if (values.Count != 1 || !AggregateEtag.TryParse(values[0], out var etag))
        {
            return ProblemResults.InvalidEtag(context);
        }

        context.Items[AggregateEtag.HttpContextKey] = etag;
        return null;
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequireIfMatchAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context) =>
        context.Result = IfMatchHeader.Read(context.HttpContext);

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
