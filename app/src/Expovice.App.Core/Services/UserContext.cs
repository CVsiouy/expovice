using CommunityToolkit.Mvvm.ComponentModel;
using Expovice.App.Core.Abstractions;
using Expovice.App.Core.Api;
using Expovice.App.Core.Ui;
using Expovice.Contracts;

namespace Expovice.App.Core.Services;

/// <summary>What a just-sent request looked like, for the "Request sent" screen.</summary>
public sealed record SentRequestSummary(string ProviderFirstName, string Skill, string When, string RateText);

/// <summary>
/// App-wide signed-in state (singleton): the user, their location, provider profile and pending-request badge.
/// UI-thread-affine: mutate only from view-model commands, never from background threads.
/// </summary>
public sealed partial class UserContext(IExpoviceApi api, ILocationService locationService) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(FirstName), nameof(IsProvider))]
    private MeDto? me;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProvider))]
    private ProviderProfileDto? providerProfile;

    [ObservableProperty]
    private UserLocation? location;

    /// <summary>Pending incoming requests: the badge on the "My work" tab.</summary>
    [ObservableProperty]
    private int newRequestCount;

    public SentRequestSummary? LastSent { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Me?.Name) ? "there" : Me.Name.Trim();

    public string FirstName => Formatting.FirstName(DisplayName);

    public bool IsProvider => ProviderProfile is not null || Me?.IsProvider == true;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Me = await api.GetMeAsync(ct);
        ProviderProfile = Me.IsProvider ? await api.GetMyProviderProfileAsync(ct) : null;
    }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (Me is null)
        {
            await LoadAsync(ct);
        }
    }

    /// <summary>
    /// Uses the saved home address when there is one; otherwise asks the device and saves it as home,
    /// so listings are always relative to one stable point (like Swiggy/Zomato's delivery address).
    /// </summary>
    public async Task<UserLocation> EnsureLocationAsync(CancellationToken ct = default)
    {
        if (Location is not null)
        {
            return Location;
        }

        if (Me?.Home is { } home)
        {
            return Location = new UserLocation(home.Latitude, home.Longitude, home.Line, Formatting.AreaFromLine(home.Line));
        }

        var current = await locationService.GetCurrentAsync(ct);
        await api.SetHomeAsync(new HomeAddressDto("Home", current.Line, current.Latitude, current.Longitude), ct);
        return Location = current;
    }

    public async Task RefreshWorkCountAsync(CancellationToken ct = default)
    {
        NewRequestCount = IsProvider ? (await api.GetWorkSummaryAsync(ct)).NewCount : 0;
    }

    public void Reset()
    {
        Me = null;
        ProviderProfile = null;
        Location = null;
        NewRequestCount = 0;
        LastSent = null;
    }
}
