using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PilotFlow.Api.Errors;
using PilotFlow.Application.Analyses;

namespace PilotFlow.Api.Hosting;

internal static class ApiExceptionHandler
{
    public static Task WriteAsync(HttpContext context)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var databaseError = error is DbUpdateException updateFailure ? updateFailure.InnerException : error;
        var result = databaseError is AnalysisStoreUnavailableException
            or NpgsqlException { IsTransient: true }
            or PostgresException { SqlState: PostgresErrorCodes.UndefinedTable }
            ? ProblemResults.DatabaseUnavailable(context)
            : ProblemResults.InternalError(context);

        return result.ExecuteAsync(context);
    }
}
