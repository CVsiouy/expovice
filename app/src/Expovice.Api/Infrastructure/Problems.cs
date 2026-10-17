using Microsoft.AspNetCore.Http.HttpResults;

namespace Expovice.Api.Infrastructure;

/// <summary>Builds RFC 7807 problem responses consistently (DRY across endpoints).</summary>
public static class Problems
{
    public static ProblemHttpResult NotFound(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not found");

    public static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    public static ProblemHttpResult Forbidden(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");

    public static ProblemHttpResult TooManyRequests(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests");

    public static ProblemHttpResult Unauthorized(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");

    public static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>
/// Collects field errors and turns them into a 400 ValidationProblem.
/// Not thread-safe; create one per validation call.
/// </summary>
public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public Validator Check(bool condition, string field, string message)
    {
        if (!condition)
        {
            if (!_errors.TryGetValue(field, out var list))
            {
                _errors[field] = list = [];
            }
            list.Add(message);
        }
        return this;
    }

    public Validator Required(string? value, string field, int maxLength) =>
        Check(!string.IsNullOrWhiteSpace(value), field, $"{field} is required.")
            .Check((value?.Length ?? 0) <= maxLength, field, $"{field} must be at most {maxLength} characters.");

    public ValidationProblem ToProblem() =>
        TypedResults.ValidationProblem(_errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
}

/// <summary>Logs unhandled exceptions and returns a generic 500 without leaking internals.</summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true; // Client went away; nothing to report.
        }

        logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Title = "Something went wrong", Status = StatusCodes.Status500InternalServerError },
        });
    }
}

public static class PhoneMask
{
    /// <summary>Keeps logs useful without storing full phone numbers: +91******3210.</summary>
    public static string Mask(string? phone) =>
        string.IsNullOrEmpty(phone) || phone.Length <= 4 ? "****" : "******" + phone[^4..];
}
