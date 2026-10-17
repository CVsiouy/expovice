using System.Net;
using System.Net.Http.Headers;

namespace Expovice.App.Core.Api;

/// <summary>
/// Adds "Authorization: Bearer {token}" and, on a 401, refreshes once and replays the request.
/// Thread-safe: the handler is stateless; coordination lives in <see cref="AuthSession"/>.
/// </summary>
public sealed class AuthTokenHandler(AuthSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = session.AccessToken;
        Attach(request, token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null)
        {
            return response;
        }

        if (!await session.RefreshAsync(token, cancellationToken))
        {
            return response;
        }

        response.Dispose();
        // Safe to resend: the client only uses buffered content (JsonContent / ByteArrayContent).
        Attach(request, session.AccessToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private static void Attach(HttpRequestMessage request, string? token) =>
        request.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
}
