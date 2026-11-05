using System.Diagnostics;

namespace PilotFlow.Api.Errors;

public static class ProblemResults
{
    public static IResult Unauthorized(HttpContext context) =>
        Create(
            context,
            "urn:pilot:problem:unauthorized",
            "Не аутентифицирован",
            StatusCodes.Status401Unauthorized,
            "Для доступа к операции требуется действительный Bearer-токен с claim sub.",
            "UNAUTHORIZED");

    public static IResult EtagRequired(HttpContext context) =>
        Create(
            context,
            "urn:pilot:problem:etag-required",
            "Требуется If-Match",
            StatusCodes.Status428PreconditionRequired,
            "Для изменения агрегата передайте его актуальный сильный ETag в заголовке If-Match.",
            "ETAG_REQUIRED");

    public static IResult InvalidEtag(HttpContext context) =>
        Create(
            context,
            "urn:pilot:problem:invalid-etag",
            "Некорректный If-Match",
            StatusCodes.Status400BadRequest,
            "Заголовок If-Match должен содержать один сильный ETag формата \"v:{versionId}:r:{revision}\".",
            "INVALID_ETAG");

    public static IResult NotImplemented(HttpContext context, string operationId)
    {
        var problem = new ApiProblem(
            "urn:pilot:problem:not-implemented",
            "Операция ещё не реализована",
            StatusCodes.Status501NotImplemented,
            $"Операция {operationId} зарегистрирована контрактом, но её обработчик ещё не реализован.",
            context.Request.Path,
            "NOT_IMPLEMENTED",
            Activity.Current?.Id ?? context.TraceIdentifier,
            []);

        return Results.Json(
            problem,
            statusCode: StatusCodes.Status501NotImplemented,
            contentType: "application/problem+json");
    }

    private static IResult Create(
        HttpContext context,
        string type,
        string title,
        int status,
        string detail,
        string code)
    {
        var problem = new ApiProblem(
            type,
            title,
            status,
            detail,
            context.Request.Path,
            code,
            Activity.Current?.Id ?? context.TraceIdentifier,
            []);

        return Results.Json(problem, statusCode: status, contentType: "application/problem+json");
    }
}
