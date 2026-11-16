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

    public static ApiProblemResult EtagRequired(HttpContext context) =>
        Create(
            context,
            "urn:pilot:problem:etag-required",
            "Требуется If-Match",
            StatusCodes.Status428PreconditionRequired,
            "Для изменения агрегата передайте его актуальный сильный ETag в заголовке If-Match.",
            "IF_MATCH_REQUIRED");

    public static ApiProblemResult InvalidEtag(HttpContext context) =>
        Create(
            context,
            "urn:pilot:problem:invalid-etag",
            "Некорректный If-Match",
            StatusCodes.Status400BadRequest,
            "Заголовок If-Match должен содержать один сильный ETag формата \"v:{versionId}:r:{revision}\".",
            "INVALID_REQUEST");

    public static ApiProblemResult InvalidRequest(HttpContext context) =>
        Create(context, "urn:pilot:problem:invalid-request", "Некорректный запрос", StatusCodes.Status400BadRequest,
            "Проверьте параметры запроса и формат JSON.", "INVALID_REQUEST");

    public static ApiProblemResult NotFound(HttpContext context) =>
        Create(context, "urn:pilot:problem:not-found", "Не найдено", StatusCodes.Status404NotFound,
            "Запрошенный ресурс не найден.", "NOT_FOUND");

    public static ApiProblemResult FixedVersion(HttpContext context) =>
        Create(context, "urn:pilot:problem:state-conflict", "Версия зафиксирована", StatusCodes.Status409Conflict,
            "Изменять можно только черновик.", "STATE_CONFLICT");

    public static ApiProblemResult RevisionConflict(HttpContext context) =>
        Create(context, "urn:pilot:problem:revision-conflict", "Ревизия изменилась", StatusCodes.Status412PreconditionFailed,
            "Загрузите актуальные данные и повторите изменение с актуальным ETag.", "REVISION_CONFLICT");

    public static ApiProblemResult ValidationFailed(HttpContext context, IReadOnlyList<ApiIssue> issues) =>
        Create(context, "urn:pilot:problem:validation", "Проверьте данные", StatusCodes.Status422UnprocessableEntity,
            "Запрос нарушает правила ввода.", "VALIDATION_FAILED", issues);

    public static ApiProblemResult DatabaseUnavailable(HttpContext context) =>
        Create(context, "urn:pilot:problem:unavailable", "Хранилище недоступно", StatusCodes.Status503ServiceUnavailable,
            "Хранилище анализа временно недоступно.", "DATABASE_UNAVAILABLE");

    public static ApiProblemResult InternalError(HttpContext context) =>
        Create(context, "urn:pilot:problem:internal-error", "Внутренняя ошибка", StatusCodes.Status500InternalServerError,
            "Не удалось обработать запрос.", "INTERNAL_ERROR");

    public static IResult NotImplemented(HttpContext context, string operationId) =>
        Create(
            context,
            "urn:pilot:problem:not-implemented",
            "Операция ещё не реализована",
            StatusCodes.Status501NotImplemented,
            $"Операция {operationId} зарегистрирована контрактом, но её обработчик ещё не реализован.",
            "NOT_IMPLEMENTED");

    private static ApiProblemResult Create(
        HttpContext context,
        string type,
        string title,
        int status,
        string detail,
        string code,
        IReadOnlyList<ApiIssue>? issues = null)
    {
        var problem = new ApiProblem(
            type,
            title,
            status,
            detail,
            context.Request.Path,
            code,
            Activity.Current?.Id ?? context.TraceIdentifier,
            issues ?? []);

        return new ApiProblemResult(problem);
    }
}
