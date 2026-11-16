namespace PilotFlow.Api.Concurrency;

public sealed class IfMatchEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (IfMatchHeader.Read(context.HttpContext) is { } error)
        {
            return error;
        }

        return await next(context);
    }
}
