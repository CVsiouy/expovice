using System.Net;
using System.Net.Http.Json;
using Expovice.App.Core.Abstractions;
using Expovice.App.Core.Api;
using Expovice.App.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Expovice.App.Core.Tests;

/// <summary>HttpMessageHandler driven by a delegate; records every request it sees.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int calls;

    public int Calls => calls;

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        lock (Requests)
        {
            Requests.Add(request);
        }

        return respond(request);
    }

    public static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: ExpoviceApiClient.Json) };
}

internal sealed class MemorySessionStore : ISessionStore
{
    public string? RefreshToken { get; set; }

    public bool HasSeenOnboarding { get; set; }

    public Task<string?> GetRefreshTokenAsync() => Task.FromResult(RefreshToken);

    public Task SetRefreshTokenAsync(string? refreshToken)
    {
        RefreshToken = refreshToken;
        return Task.CompletedTask;
    }
}

internal static class Fakes
{
    public static readonly Uri BaseUrl = new("https://api.test");

    public static readonly UserLocation Indiranagar = new(12.9719, 77.6412, "12th Main, Indiranagar, Bengaluru", "Indiranagar");

    public static IHttpClientFactory Factory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false) { BaseAddress = BaseUrl });
        return factory;
    }

    public static AuthSession Session(HttpMessageHandler refreshHandler, MemorySessionStore store) =>
        new(Factory(refreshHandler), store, NullLogger<AuthSession>.Instance);

    public static UserContext User(IExpoviceApi api)
    {
        var location = Substitute.For<ILocationService>();
        location.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(Indiranagar);
        return new UserContext(api, location) { Location = Indiranagar };
    }
}
