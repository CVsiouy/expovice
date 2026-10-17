using System.Net;

namespace Expovice.App.Core.Api;

/// <summary>A non-success API response, carrying the RFC 7807 ProblemDetails title/detail when the server sent one.</summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode status, string title, string? detail = null, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(detail ?? title)
    {
        Status = status;
        Title = title;
        Detail = detail;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public HttpStatusCode Status { get; }

    public string Title { get; }

    public string? Detail { get; }

    /// <summary>Validation errors keyed by field (from ValidationProblemDetails).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>The best single sentence to show the user.</summary>
    public string UserMessage =>
        Errors.Values.SelectMany(v => v).FirstOrDefault() ?? Detail ?? Title;
}

/// <summary>Wire shape of ProblemDetails / ValidationProblemDetails (only the fields we read).</summary>
internal sealed record ProblemPayload(string? Title, string? Detail, int? Status, Dictionary<string, string[]>? Errors);
