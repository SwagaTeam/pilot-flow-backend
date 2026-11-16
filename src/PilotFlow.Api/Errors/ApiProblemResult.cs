using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PilotFlow.Api.Errors;

public sealed class ApiProblemResult(ApiProblem problem) : IResult, IActionResult
{
    public Task ExecuteAsync(HttpContext httpContext) =>
        Results.Json(problem, statusCode: problem.Status, contentType: "application/problem+json")
            .ExecuteAsync(httpContext);

    public Task ExecuteResultAsync(ActionContext context) => ExecuteAsync(context.HttpContext);
}

internal sealed class JsonInputProblemFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is UnsupportedMediaTypeResult)
        {
            context.Result = ProblemResults.InvalidRequest(context.HttpContext);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
