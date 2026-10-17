using System.Net;
using System.Net.Http.Json;
using Expovice.Contracts;

namespace Expovice.Api.Tests;

public sealed class AuthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Otp_HappyPath_CreatesUserAndMarksNewThenReturning()
    {
        var phone = api.NewPhone();
        var (client, first) = await api.SignInAsync(phone, name: "Asha Rao");
        Assert.True(first.IsNewUser);

        var me = await client.GetFromJsonAsync<MeDto>(ApiRoutes.Me, Ct);
        Assert.Equal("Asha Rao", me!.Name);
        Assert.Equal(phone, me.PhoneNumber);

        api.Time.Advance(TimeSpan.FromMinutes(1));
        var (_, second) = await api.SignInAsync(phone, name: null);
        Assert.False(second.IsNewUser);
    }

    [Fact]
    public async Task Otp_InvalidPhone_Returns400()
    {
        var res = await api.CreateClient().PostAsJsonAsync(ApiRoutes.AuthOtpSend, new SendOtpRequest("+15551234567"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Otp_ResendTooSoon_Returns429_ThenAllowedAfterCooldown()
    {
        var client = api.CreateClient();
        var phone = api.NewPhone();
        (await client.PostAsJsonAsync(ApiRoutes.AuthOtpSend, new SendOtpRequest(phone), Ct)).EnsureSuccessStatusCode();

        var tooSoon = await client.PostAsJsonAsync(ApiRoutes.AuthOtpSend, new SendOtpRequest(phone), Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);

        api.Time.Advance(TimeSpan.FromSeconds(Limits.OtpResendSeconds + 1));
        (await client.PostAsJsonAsync(ApiRoutes.AuthOtpSend, new SendOtpRequest(phone), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Otp_WrongCodeTooOften_LocksChallenge()
    {
        var client = api.CreateClient();
        var phone = api.NewPhone();
        (await client.PostAsJsonAsync(ApiRoutes.AuthOtpSend, new SendOtpRequest(phone), Ct)).EnsureSuccessStatusCode();
        var code = api.Sms.LastCodeFor(phone);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 1; i < Limits.OtpMaxAttempts; i++)
        {
            var res = await client.PostAsJsonAsync(ApiRoutes.AuthOtpVerify, new VerifyOtpRequest(phone, wrong), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        var last = await client.PostAsJsonAsync(ApiRoutes.AuthOtpVerify, new VerifyOtpRequest(phone, wrong), Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);

        // Even the right code no longer works for this challenge.
        var right = await client.PostAsJsonAsync(ApiRoutes.AuthOtpVerify, new VerifyOtpRequest(phone, code), Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, right.StatusCode);
    }

    [Fact]
    public async Task Otp_Expired_Returns400()
    {
        var client = api.CreateClient();
        var phone = api.NewPhone();
        (await client.PostAsJsonAsync(ApiRoutes.AuthOtpSend, new SendOtpRequest(phone), Ct)).EnsureSuccessStatusCode();
        api.Time.Advance(TimeSpan.FromMinutes(Limits.OtpTtlMinutes + 1));

        var res = await client.PostAsJsonAsync(ApiRoutes.AuthOtpVerify, new VerifyOtpRequest(phone, api.Sms.LastCodeFor(phone)), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndReuseRevokesAllSessions()
    {
        var (_, auth) = await api.SignInAsync();
        var client = api.CreateClient();

        var rotated = await client.PostAsJsonAsync(ApiRoutes.AuthRefresh, new RefreshTokenRequest(auth.RefreshToken), Ct);
        rotated.EnsureSuccessStatusCode();
        var next = (await rotated.Content.ReadFromJsonAsync<AuthResponse>(Ct))!;
        Assert.NotEqual(auth.RefreshToken, next.RefreshToken);

        // Replaying the old token is treated as theft...
        var replay = await client.PostAsJsonAsync(ApiRoutes.AuthRefresh, new RefreshTokenRequest(auth.RefreshToken), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // ...so the legitimately rotated token is revoked too.
        var afterReuse = await client.PostAsJsonAsync(ApiRoutes.AuthRefresh, new RefreshTokenRequest(next.RefreshToken), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task SignOut_RevokesRefreshToken()
    {
        var (_, auth) = await api.SignInAsync();
        var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(ApiRoutes.AuthSignOut, new RefreshTokenRequest(auth.RefreshToken), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(ApiRoutes.AuthRefresh, new RefreshTokenRequest(auth.RefreshToken), Ct)).StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var res = await api.CreateClient().GetAsync(ApiRoutes.Me, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ExternalSignIn_NotConfigured_Returns404()
    {
        var res = await api.CreateClient().GetAsync(ApiRoutes.AuthExternal(ExternalProvider.Google), Ct);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var res = await api.CreateClient().GetAsync(ApiRoutes.Health, Ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
