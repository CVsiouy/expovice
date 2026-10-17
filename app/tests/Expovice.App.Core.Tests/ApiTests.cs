using System.Net;
using System.Text;
using Expovice.App.Core.Api;
using Expovice.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Expovice.App.Core.Tests;

public sealed class AuthTokenHandlerTests
{
    private static AuthResponse Tokens(string access, string refresh) =>
        new(access, DateTimeOffset.UtcNow.AddHours(1), refresh, false);

    private static (HttpClient Client, StubHandler Api, StubHandler Refresh, AuthSession Session, MemorySessionStore Store) Build(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> api,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> refresh)
    {
        var store = new MemorySessionStore { RefreshToken = "r1" };
        var refreshHandler = new StubHandler(refresh);
        var session = Fakes.Session(refreshHandler, store);
        var apiHandler = new StubHandler(api);
        var client = new HttpClient(new AuthTokenHandler(session) { InnerHandler = apiHandler }) { BaseAddress = Fakes.BaseUrl };
        return (client, apiHandler, refreshHandler, session, store);
    }

    [Fact]
    public async Task Concurrent_401s_trigger_exactly_one_refresh()
    {
        var (client, api, refresh, session, store) = Build(
            async req =>
            {
                await Task.Delay(20);
                return req.Headers.Authorization?.Parameter == "new"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            },
            async _ =>
            {
                await Task.Delay(50);
                return StubHandler.Json(Tokens("new", "r2"));
            });
        await session.SignInAsync(Tokens("old", "r1"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.GetAsync(new Uri("/me", UriKind.Relative))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, refresh.Calls);
        Assert.Equal("r2", store.RefreshToken);
        Assert.Equal(20, api.Calls);
    }

    [Fact]
    public async Task Replays_only_once_when_the_new_token_is_also_rejected()
    {
        var (client, api, refresh, session, _) = Build(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            _ => Task.FromResult(StubHandler.Json(Tokens("new", "r2"))));
        await session.SignInAsync(Tokens("old", "r1"));

        var response = await client.GetAsync(new Uri("/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, api.Calls);
        Assert.Equal(1, refresh.Calls);
    }

    [Fact]
    public async Task Rejected_refresh_token_expires_the_session()
    {
        var (client, _, _, session, store) = Build(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        await session.SignInAsync(Tokens("old", "r1"));
        var expired = 0;
        session.SessionExpired += (_, _) => expired++;

        var response = await client.GetAsync(new Uri("/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, expired);
        Assert.Null(store.RefreshToken);
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task Network_failure_during_refresh_keeps_the_stored_token()
    {
        var (client, _, _, session, store) = Build(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            _ => throw new HttpRequestException("offline"));
        await session.SignInAsync(Tokens("old", "r1"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("/me", UriKind.Relative)));
        Assert.Equal("r1", store.RefreshToken);
    }

    [Fact]
    public async Task TryRestore_uses_the_stored_refresh_token()
    {
        var (_, _, refresh, session, store) = Build(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            _ => Task.FromResult(StubHandler.Json(Tokens("fresh", "r2"))));

        Assert.True(await session.TryRestoreAsync());
        Assert.Equal("fresh", session.AccessToken);
        Assert.Equal("r2", store.RefreshToken);
        Assert.Equal(1, refresh.Calls);
    }
}

public sealed class ExpoviceApiClientTests
{
    private static (ExpoviceApiClient Client, StubHandler Handler) Build(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = Fakes.BaseUrl };
        return (new ExpoviceApiClient(http, NullLogger<ExpoviceApiClient>.Instance), handler);
    }

    [Fact]
    public async Task Validation_problem_details_become_an_ApiException_with_the_first_error()
    {
        const string problem = """
            {"title":"One or more validation errors occurred.","status":400,"errors":{"Name":["Name is required."]}}
            """;
        var (client, _) = Build(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(problem, Encoding.UTF8, "application/problem+json"),
        }));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.UpdateNameAsync(""));

        Assert.Equal(HttpStatusCode.BadRequest, ex.Status);
        Assert.Equal("One or more validation errors occurred.", ex.Title);
        Assert.Equal("Name is required.", ex.UserMessage);
    }

    [Fact]
    public async Task Non_json_errors_fall_back_to_the_reason_phrase()
    {
        var (client, _) = Build(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html/>") }));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetMeAsync());

        Assert.Equal(HttpStatusCode.BadGateway, ex.Status);
        Assert.Equal("Bad Gateway", ex.UserMessage);
    }

    [Fact]
    public async Task Missing_provider_profile_returns_null()
    {
        var (client, _) = Build(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await client.GetMyProviderProfileAsync());
    }

    [Fact]
    public async Task Search_sends_filters_as_query_string()
    {
        var (client, handler) = Build(_ => Task.FromResult(StubHandler.Json(new ProviderSearchResult([], 2))));

        var result = await client.SearchProvidersAsync(new ProviderQuery("plumber", 12.9719, 77.6412, 5, ProviderSort.Rating, true, 399));

        Assert.Equal(2, result.BeyondRadiusCount);
        Assert.Equal(
            "/providers?category=plumber&lat=12.9719&lon=77.6412&radiusKm=5&sort=Rating&availableToday=true&maxRate=399",
            handler.Requests[0].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Top_providers_accepts_a_bare_array_or_an_envelope()
    {
        var dto = new ProviderSummaryDto(Guid.NewGuid(), "Ravi Kumar", null, "electrician", ["Wiring"], 4.7, 20, 120, 1.2, 350, RateUnit.Visit, true, true);
        var (arrayClient, _) = Build(_ => Task.FromResult(StubHandler.Json(new[] { dto })));
        var (envelopeClient, _) = Build(_ => Task.FromResult(StubHandler.Json(new ProviderSearchResult([dto], 0))));

        Assert.Equal("Ravi Kumar", Assert.Single(await arrayClient.GetTopProvidersAsync(1, 2, 5)).Name);
        Assert.Equal("Ravi Kumar", Assert.Single(await envelopeClient.GetTopProvidersAsync(1, 2, 5)).Name);
    }

    [Fact]
    public async Task Work_actions_post_to_lowercase_action_routes()
    {
        var id = Guid.NewGuid();
        var (client, handler) = Build(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        await client.WorkActionAsync(id, WorkAction.Complete);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal($"/work/requests/{id}/complete", handler.Requests[0].RequestUri!.AbsolutePath);
    }
}
