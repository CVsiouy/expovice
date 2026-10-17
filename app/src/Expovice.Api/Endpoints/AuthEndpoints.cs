using System.Security.Claims;
using Expovice.Api.Infrastructure;
using Expovice.Api.Services;
using Expovice.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Expovice.Api.Endpoints;

public static class AuthEndpoints
{
    public const string OtpRateLimitPolicy = "otp";
    public const string ExternalCookieScheme = "External";
    private const string ExternalCallbackPath = "/auth/external/callback/{provider}";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Auth").AllowAnonymous();

        group.MapPost(ApiRoutes.AuthOtpSend, SendOtpAsync).RequireRateLimiting(OtpRateLimitPolicy);
        group.MapPost(ApiRoutes.AuthOtpVerify, VerifyOtpAsync).RequireRateLimiting(OtpRateLimitPolicy);
        group.MapPost(ApiRoutes.AuthRefresh, RefreshAsync);
        group.MapPost(ApiRoutes.AuthSignOut, SignOutAsync);
        group.MapGet(ApiRoutes.AuthExternalStart, StartExternalAsync);
        group.MapGet(ExternalCallbackPath, ExternalCallbackAsync);
        return app;
    }

    private static async Task<Results<Ok<SendOtpResponse>, ValidationProblem, ProblemHttpResult>> SendOtpAsync(
        SendOtpRequest body, OtpService otp, CancellationToken ct)
    {
        if (!OtpService.IsValidPhone(body.PhoneNumber))
        {
            return Problems.Invalid(nameof(body.PhoneNumber), "Enter a valid Indian mobile number.");
        }

        return await otp.SendAsync(body.PhoneNumber, ct) == OtpSendResult.TooSoon
            ? Problems.TooManyRequests($"Please wait {Limits.OtpResendSeconds} seconds before requesting a new code.")
            : TypedResults.Ok(new SendOtpResponse(Limits.OtpResendSeconds, Limits.OtpLength));
    }

    private static async Task<Results<Ok<AuthResponse>, ValidationProblem, ProblemHttpResult>> VerifyOtpAsync(
        VerifyOtpRequest body, OtpService otp, AccountService accounts, TokenService tokens, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!OtpService.IsValidPhone(body.PhoneNumber) || body.Code is not { Length: Limits.OtpLength } || !body.Code.All(char.IsAsciiDigit))
        {
            return Problems.Invalid(nameof(body.Code), $"Enter the {Limits.OtpLength}-digit code.");
        }

        switch (await otp.VerifyAsync(body.PhoneNumber, body.Code, ct))
        {
            case OtpVerifyResult.Expired:
                return Problems.Invalid(nameof(body.Code), "This code has expired. Request a new one.");
            case OtpVerifyResult.TooManyAttempts:
                return Problems.TooManyRequests("Too many wrong attempts. Request a new code.");
            case OtpVerifyResult.Invalid:
                return Problems.Invalid(nameof(body.Code), "That code isn't right.");
        }

        var user = await accounts.FindOrCreateByPhoneAsync(body.PhoneNumber, ct);
        loggers.CreateLogger(nameof(AuthEndpoints)).LogInformation("User {UserId} signed in with phone", user.Id);
        return TypedResults.Ok(await tokens.IssueAsync(user, ct));
    }

    private static async Task<Results<Ok<AuthResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshTokenRequest body, TokenService tokens, CancellationToken ct) =>
        await tokens.RefreshAsync(body.RefreshToken ?? "", ct) is { } response
            ? TypedResults.Ok(response)
            : Problems.Unauthorized("Session expired. Please sign in again.");

    private static async Task<NoContent> SignOutAsync(RefreshTokenRequest body, TokenService tokens, CancellationToken ct)
    {
        await tokens.RevokeAsync(body.RefreshToken ?? "", ct);
        return TypedResults.NoContent();
    }

    /// <summary>Opens Google/Apple sign-in in the system browser (driven by the app's WebAuthenticator).</summary>
    private static async Task<Results<ChallengeHttpResult, ProblemHttpResult>> StartExternalAsync(
        string provider, IAuthenticationSchemeProvider schemes)
    {
        if (!TryParseProvider(provider, out var parsed) || await schemes.GetSchemeAsync(SchemeFor(parsed)) is null)
        {
            return Problems.NotFound($"Sign-in with '{provider}' is not available.");
        }

        var props = new AuthenticationProperties { RedirectUri = ExternalCallbackPath.Replace("{provider}", provider.ToLowerInvariant(), StringComparison.Ordinal) };
        return TypedResults.Challenge(props, [SchemeFor(parsed)]);
    }

    /// <summary>
    /// Lands here after Google/Apple. Creates/loads the account, then redirects to the app with a
    /// refresh token in the URL fragment. The app swaps it at /auth/refresh straight away, which rotates it,
    /// so the token in the URL is single-use.
    /// </summary>
    private static async Task<Results<RedirectHttpResult, ProblemHttpResult>> ExternalCallbackAsync(
        string provider, HttpContext http, AccountService accounts, TokenService tokens, TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        var result = await http.AuthenticateAsync(ExternalCookieScheme);
        await http.SignOutAsync(ExternalCookieScheme);
        var subject = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!result.Succeeded || subject is null || !TryParseProvider(provider, out var parsed))
        {
            return Problems.Unauthorized("External sign-in failed.");
        }

        var name = result.Principal!.FindFirstValue(ClaimTypes.Name)
            ?? string.Join(' ', new[] { result.Principal!.FindFirstValue(ClaimTypes.GivenName), result.Principal!.FindFirstValue(ClaimTypes.Surname) }.Where(s => !string.IsNullOrEmpty(s)));
        var user = await accounts.FindOrCreateExternalAsync(parsed, subject, name, ct);
        var refresh = await tokens.CreateRefreshTokenAsync(user.Id, ct);
        loggers.CreateLogger(nameof(AuthEndpoints)).LogInformation("User {UserId} signed in with {Provider}", user.Id, parsed);

        var isNew = string.IsNullOrWhiteSpace(user.Name) || user.CreatedAt > time.GetUtcNow().AddMinutes(-1);
        var fragment = $"{AuthCallback.RefreshTokenKey}={Uri.EscapeDataString(refresh)}&{AuthCallback.IsNewUserKey}={(isNew ? "true" : "false")}";
        return TypedResults.Redirect($"{AuthCallback.Uri}#{fragment}");
    }

    public static string SchemeFor(ExternalProvider provider) => provider.ToString();

    private static bool TryParseProvider(string value, out ExternalProvider provider) =>
        Enum.TryParse(value, ignoreCase: true, out provider) && Enum.IsDefined(provider);
}
