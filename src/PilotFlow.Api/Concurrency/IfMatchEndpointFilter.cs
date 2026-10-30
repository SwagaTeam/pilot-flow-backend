using PilotFlow.Api.Errors;

namespace PilotFlow.Api.Concurrency;

public sealed class IfMatchEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var values = context.HttpContext.Request.Headers.IfMatch;
        if (values.Count == 0)
        {
            return ProblemResults.EtagRequired(context.HttpContext);
        }

        if (values.Count != 1 || !AggregateEtag.TryParse(values[0], out var etag))
        {
            return ProblemResults.InvalidEtag(context.HttpContext);
        }

        context.HttpContext.Items[AggregateEtag.HttpContextKey] = etag;
        return await next(context);
    }
}
