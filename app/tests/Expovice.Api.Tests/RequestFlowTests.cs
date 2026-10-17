using System.Net;
using System.Net.Http.Json;
using Expovice.Api.Services;
using Expovice.Contracts;

namespace Expovice.Api.Tests;

public sealed class RequestFlowTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const double Lat = 12.9719, Lon = 77.6412;

    private static DateOnly Today => DateOnly.FromDateTime(ApiFactory.Start.ToOffset(TimeSpan.FromHours(5.5)).DateTime);

    private async Task<(HttpClient Pro, Guid ProId, HttpClient Customer)> SetupAsync()
    {
        var (pro, proId) = await api.CreateProviderAsync(Lat, Lon);
        var (customer, _) = await api.SignInAsync(name: "Nisha Menon");
        var (lat, lon) = Geo.Offset(Lat, Lon, 1, 90);
        await ApiFactory.SetHomeAsync(customer, lat, lon, "HAL 2nd Stage");
        return (pro, proId, customer);
    }

    private static async Task<ServiceRequestDto> CreateAsync(HttpClient customer, Guid proId)
    {
        var res = await customer.PostAsJsonAsync(ApiRoutes.Requests,
            new CreateServiceRequest(proId, "electrician", "Wiring", "Kitchen socket sparks", Today, Catalog.TimeSlots[0]), Ct);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ServiceRequestDto>(Ct))!;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string url) => client.PostAsync(url, null, Ct);

    [Fact]
    public async Task FullLifecycle_Create_Accept_Complete_Rate()
    {
        var (pro, proId, customer) = await SetupAsync();
        var request = await CreateAsync(customer, proId);
        Assert.Equal(RequestStatus.Pending, request.Status);
        Assert.Equal(300, request.Rate);
        Assert.Equal(1, api.WithDb(db => db.Providers.Single(p => p.UserId == proId).RequestCount));

        var incoming = (await pro.GetFromJsonAsync<List<IncomingRequestDto>>(ApiRoutes.WorkRequests, Ct))!;
        var row = Assert.Single(incoming);
        Assert.Equal("Nisha M.", row.CustomerName);
        Assert.Equal("HAL 2nd Stage", row.Area);
        Assert.Equal(1.0, row.DistanceKm, 1);

        Assert.Equal(HttpStatusCode.NoContent, (await Post(pro, ApiRoutes.WorkActionFor(request.Id, "accept"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(pro, ApiRoutes.WorkActionFor(request.Id, "accept"))).StatusCode);

        var summary = (await pro.GetFromJsonAsync<WorkSummaryDto>(ApiRoutes.WorkSummary, Ct))!;
        Assert.Equal(new WorkSummaryDto(0, 1, 300), summary);

        // Can't rate before the job is done.
        var early = await customer.PostAsJsonAsync(ApiRoutes.RequestRateFor(request.Id), new RateServiceRequest(5), Ct);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Post(pro, ApiRoutes.WorkActionFor(request.Id, "complete"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await customer.PostAsJsonAsync(ApiRoutes.RequestRateFor(request.Id), new RateServiceRequest(4), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsJsonAsync(ApiRoutes.RequestRateFor(request.Id), new RateServiceRequest(5), Ct)).StatusCode);

        var p = api.WithDb(db => db.Providers.Single(x => x.UserId == proId));
        Assert.Equal((4, 1, 4.0), (p.RatingSum, p.RatingCount, p.Rating));

        var mine = (await customer.GetFromJsonAsync<List<ServiceRequestDto>>(ApiRoutes.Requests, Ct))!;
        Assert.Equal(4, Assert.Single(mine).MyRating);
        Assert.Equal(RequestStatus.Completed, mine[0].Status);
    }

    [Fact]
    public async Task Decline_ThenCustomerCannotCancel()
    {
        var (pro, proId, customer) = await SetupAsync();
        var request = await CreateAsync(customer, proId);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(pro, ApiRoutes.WorkActionFor(request.Id, "decline"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(customer, ApiRoutes.RequestCancelFor(request.Id))).StatusCode);
    }

    [Fact]
    public async Task Cancel_Pending_Works_AndOthersCannotTouchIt()
    {
        var (pro, proId, customer) = await SetupAsync();
        var request = await CreateAsync(customer, proId);
        var (stranger, _) = await api.SignInAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, ApiRoutes.RequestCancelFor(request.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(customer, ApiRoutes.RequestCancelFor(request.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(pro, ApiRoutes.WorkActionFor(request.Id, "accept"))).StatusCode);
    }

    [Fact]
    public async Task Create_RejectsBadDateSkillSelfAndPausedPro()
    {
        var (pro, proId, customer) = await SetupAsync();

        async Task<HttpStatusCode> Try(HttpClient c, CreateServiceRequest body) =>
            (await c.PostAsJsonAsync(ApiRoutes.Requests, body, Ct)).StatusCode;

        var ok = new CreateServiceRequest(proId, "electrician", "Wiring", null, Today, Catalog.TimeSlots[1]);
        Assert.Equal(HttpStatusCode.BadRequest, await Try(customer, ok with { Date = Today.AddDays(Limits.BookingDaysAhead) }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(customer, ok with { Date = Today.AddDays(-1) }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(customer, ok with { Skill = "Gas refill" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(customer, ok with { Slot = "3 AM" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(pro, ok));
        Assert.Equal(HttpStatusCode.NotFound, await Try(customer, ok with { ProviderId = Guid.NewGuid() }));

        (await pro.PutAsJsonAsync(ApiRoutes.MeProviderAccepting, new SetAcceptingRequest(false), Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, await Try(customer, ok));
    }

    [Fact]
    public async Task WorkEndpoints_ForNonProvider_Return403()
    {
        var (customer, _) = await api.SignInAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(ApiRoutes.WorkSummary, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(customer, ApiRoutes.WorkActionFor(Guid.NewGuid(), "accept"))).StatusCode);
    }

    [Fact]
    public async Task ConcurrentAccepts_OnlyOneWins()
    {
        var (pro, proId, customer) = await SetupAsync();
        var request = await CreateAsync(customer, proId);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post(pro, ApiRoutes.WorkActionFor(request.Id, "accept"))));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.NoContent), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }
}
