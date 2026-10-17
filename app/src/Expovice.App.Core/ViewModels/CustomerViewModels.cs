using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Expovice.App.Core.Abstractions;
using Expovice.App.Core.Api;
using Expovice.App.Core.Services;
using Expovice.App.Core.Ui;
using Expovice.Contracts;
using Microsoft.Extensions.Logging;

namespace Expovice.App.Core.ViewModels;

/// <summary>Home: location, greeting, category search + grid, earn banner, top rated near you.</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly IExpoviceApi api;
    private readonly INavigationService navigation;

    public HomeViewModel(IExpoviceApi api, UserContext user, INavigationService navigation, ILogger<HomeViewModel> logger, IDialogService dialogs)
        : base(logger, dialogs)
    {
        this.api = api;
        this.navigation = navigation;
        User = user;
        ApplyFilter();
    }

    public UserContext User { get; }

    public int RadiusKm => Limits.DefaultSearchRadiusKm;

    public string Greeting => $"Hi {User.FirstName}, what needs fixing today?";

    public string AddressLine => User.Location?.Line ?? "Finding your location…";

    public string RadiusText => $"Pros within {RadiusKm} km";

    [ObservableProperty]
    private string query = string.Empty;

    public ObservableCollection<CategoryTile> Categories { get; } = [];

    public bool HasNoCategories => Categories.Count == 0;

    public string NoMatchText => $"No service matches “{Query.Trim()}”.";

    public ObservableCollection<ProviderItem> TopPros { get; } = [];

    public string BannerKicker => User.IsProvider ? "You are listed" : "Earn on Expovice";

    public string BannerTitle => User.IsProvider
        ? $"{User.NewRequestCount} new request{(User.NewRequestCount == 1 ? string.Empty : "s")} waiting for you"
        : "Got a skill? List yourself and get requests nearby";

    public string BannerCta => User.IsProvider ? "Open dashboard" : "Start listing";

    /// <summary>Case-insensitive match on category name or any of its skills.</summary>
    public static IEnumerable<ServiceCategory> Filter(string query)
    {
        var q = query.Trim();
        return Catalog.Categories.Where(c =>
            q.Length == 0
            || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || c.Skills.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Categories.Clear();
        var i = 0;
        foreach (var c in Filter(Query))
        {
            Categories.Add(new CategoryTile(c, i++, OpenCategory));
        }

        OnPropertyChanged(nameof(HasNoCategories));
        OnPropertyChanged(nameof(NoMatchText));
    }

    /// <summary>Banner, greeting and address follow the shared user state while the page is visible.</summary>
    private void OnUserChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (var name in (string[])[nameof(Greeting), nameof(AddressLine), nameof(BannerKicker), nameof(BannerTitle), nameof(BannerCta)])
        {
            OnPropertyChanged(name);
        }
    }

    public override void OnDisappearing() => User.PropertyChanged -= OnUserChanged;

    public override Task OnAppearingAsync() => RunSafeAsync(async () =>
    {
        User.PropertyChanged -= OnUserChanged;
        User.PropertyChanged += OnUserChanged;
        OnUserChanged(null, new(null));
        await User.EnsureLoadedAsync();
        var loc = await User.EnsureLocationAsync();
        var top = await api.GetTopProvidersAsync(loc.Latitude, loc.Longitude, RadiusKm);
        TopPros.Clear();
        foreach (var p in top)
        {
            TopPros.Add(new ProviderItem(p, OpenProvider));
        }

        await User.RefreshWorkCountAsync();
    });

    private void OpenCategory(CategoryTile tile) =>
        _ = navigation.GoToAsync(Routes.Listing, new Dictionary<string, object> { [NavParams.CategoryId] = tile.Id });

    private void OpenProvider(ProviderItem item) =>
        _ = navigation.GoToAsync(Routes.Detail, new Dictionary<string, object>
        {
            [NavParams.ProviderId] = item.Id,
            [NavParams.CategoryId] = item.CategoryId,
        });

    [RelayCommand]
    private Task Banner() => User.IsProvider ? navigation.GoToAsync(Routes.Work) : navigation.GoToAsync(Routes.Setup);
}

/// <summary>Pros in one category within the search radius, with sort/filter chips.</summary>
public sealed partial class ListingViewModel : ViewModelBase
{
    public enum Filter
    {
        Nearest,
        TopRated,
        AvailableToday,
        UnderBudget,
    }

    private readonly IExpoviceApi api;
    private readonly UserContext user;
    private readonly INavigationService navigation;
    private string categoryId = Catalog.Categories[0].Id;
    private bool loaded;

    public ListingViewModel(IExpoviceApi api, UserContext user, INavigationService navigation, ILogger<ListingViewModel> logger, IDialogService dialogs)
        : base(logger, dialogs)
    {
        this.api = api;
        this.user = user;
        this.navigation = navigation;
        Chips =
        [
            new(Filter.Nearest, "Nearest", OnChip, isSelected: true),
            new(Filter.TopRated, "Top rated", OnChip),
            new(Filter.AvailableToday, "Available today", OnChip),
            new(Filter.UnderBudget, "Under ₹400", OnChip),
        ];
    }

    public IReadOnlyList<SelectableItem<Filter>> Chips { get; }

    public ObservableCollection<ProviderItem> Items { get; } = [];

    public int RadiusKm => Limits.DefaultSearchRadiusKm;

    public string Title => Catalog.CategoryName(categoryId);

    public string Subtitle => $"{Items.Count} pros within {RadiusKm} km of {user.Location?.Area ?? "you"}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBeyond), nameof(BeyondText))]
    private int beyondCount;

    public bool HasBeyond => BeyondCount > 0;

    public string BeyondText => $"{BeyondCount} more beyond {RadiusKm} km aren't shown.";

    [ObservableProperty]
    private bool isEmpty;

    public override void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(NavParams.CategoryId, out var c) && c is string id)
        {
            categoryId = id;
            OnPropertyChanged(nameof(Title));
        }
    }

    public override async Task OnAppearingAsync()
    {
        if (!loaded)
        {
            loaded = true;
            await LoadAsync();
        }
    }

    private void OnChip(SelectableItem<Filter> chip)
    {
        switch (chip.Value)
        {
            case Filter.Nearest or Filter.TopRated:
                // Sort chips behave like radio buttons.
                foreach (var c in Chips.Where(c => c.Value is Filter.Nearest or Filter.TopRated))
                {
                    c.IsSelected = ReferenceEquals(c, chip);
                }

                break;
            default:
                Ui.Chips.Toggle(chip);
                break;
        }

        _ = LoadAsync();
    }

    internal ProviderQuery BuildQuery(UserLocation loc) => new(
        categoryId,
        loc.Latitude,
        loc.Longitude,
        RadiusKm,
        IsOn(Filter.TopRated) ? ProviderSort.Rating : ProviderSort.Distance,
        IsOn(Filter.AvailableToday),
        IsOn(Filter.UnderBudget) ? Choices.BudgetMaxRate : null);

    private bool IsOn(Filter f) => Chips.First(c => c.Value == f).IsSelected;

    [RelayCommand]
    private Task ClearFilters()
    {
        foreach (var c in Chips.Where(c => c.Value is Filter.AvailableToday or Filter.UnderBudget))
        {
            c.IsSelected = false;
        }

        return LoadAsync();
    }

    [RelayCommand]
    private Task Back() => navigation.GoBackAsync();

    internal Task LoadAsync() => RunSafeAsync(async () =>
    {
        var loc = await user.EnsureLocationAsync();
        var result = await api.SearchProvidersAsync(BuildQuery(loc));
        Items.Clear();
        foreach (var p in result.Items)
        {
            Items.Add(new ProviderItem(p, Open));
        }

        BeyondCount = result.BeyondRadiusCount;
        IsEmpty = Items.Count == 0;
        OnPropertyChanged(nameof(Subtitle));
    });

    private void Open(ProviderItem item) =>
        _ = navigation.GoToAsync(Routes.Detail, new Dictionary<string, object>
        {
            [NavParams.ProviderId] = item.Id,
            [NavParams.CategoryId] = categoryId,
        });
}

/// <summary>Provider detail: hero photo, stats, about, skills, hours, recent work, sticky request bar.</summary>
public sealed partial class ProviderDetailViewModel(
    IExpoviceApi api,
    UserContext user,
    INavigationService navigation,
    TimeProvider time,
    ILogger<ProviderDetailViewModel> logger,
    IDialogService dialogs) : ViewModelBase(logger, dialogs)
{
    private Guid providerId;
    private string? categoryId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsLoaded), nameof(Name), nameof(PhotoUrl), nameof(CategoryName), nameof(IsVerified), nameof(Subtitle),
        nameof(RatingText), nameof(RequestCount), nameof(YearsText), nameof(Bio), nameof(Skills), nameof(AvailabilityTitle),
        nameof(HoursText), nameof(WorkPhoto1), nameof(WorkPhoto2), nameof(WorkPhoto3), nameof(RateText), nameof(PriceCaption))]
    private ProviderDetailDto? provider;

    public bool IsLoaded => Provider is not null;

    public string Name => Provider?.Name ?? string.Empty;

    public string? PhotoUrl => Provider?.PhotoUrl;

    public string CategoryName => Catalog.CategoryName(categoryId ?? FirstCategory(Provider) ?? string.Empty);

    public bool IsVerified => Provider?.IsVerified == true;

    public string Subtitle => Provider is null ? string.Empty
        : $"{Formatting.Distance(Provider.DistanceKm)} away · speaks {string.Join(", ", Provider.Languages)}";

    public string RatingText => Formatting.Rating(Provider?.Rating ?? 0);

    public int RequestCount => Provider?.RequestCount ?? 0;

    public string YearsText => $"{Provider?.YearsOfExperience ?? 0} yrs";

    public string Bio => Provider?.Bio ?? string.Empty;

    public IReadOnlyList<string> Skills => Provider?.Skills ?? [];

    public string AvailabilityTitle => Provider is null ? string.Empty : Availability(Provider, Formatting.IndiaToday(time));

    public string HoursText => Provider is null ? string.Empty
        : $"{Formatting.DayRange(Provider.WorkDays)} · {Formatting.Hour(Provider.From)} – {Formatting.Hour(Provider.To)}";

    public string? WorkPhoto1 => Provider?.WorkPhotoUrls.ElementAtOrDefault(0);

    public string? WorkPhoto2 => Provider?.WorkPhotoUrls.ElementAtOrDefault(1);

    public string? WorkPhoto3 => Provider?.WorkPhotoUrls.ElementAtOrDefault(2);

    public string RateText => Formatting.Rupees(Provider?.Rate ?? 0);

    public string PriceCaption => $"per {Formatting.Unit(Provider?.Unit ?? RateUnit.Visit)} · pay after service";

    private static string? FirstCategory(ProviderDetailDto? p) => p is { CategoryIds.Count: > 0 } ? p.CategoryIds[0] : null;

    /// <summary>"Available today", else the next working day ("From tomorrow", "From Mon").</summary>
    public static string Availability(ProviderDetailDto p, DateOnly today)
    {
        if (p.AvailableToday)
        {
            return "Available today";
        }

        for (var i = 1; i <= 7; i++)
        {
            var day = today.AddDays(i);
            if (p.WorkDays.Contains(day.DayOfWeek))
            {
                return i == 1 ? "From tomorrow" : $"From {Formatting.ShortDay(day.DayOfWeek)}";
            }
        }

        return "Not taking requests";
    }

    public override void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(NavParams.ProviderId, out var id) && id is Guid g)
        {
            providerId = g;
        }

        if (parameters.TryGetValue(NavParams.CategoryId, out var c) && c is string cat)
        {
            categoryId = cat;
        }
    }

    public override Task OnAppearingAsync() => Provider is not null ? Task.CompletedTask : RunSafeAsync(async () =>
    {
        var loc = await user.EnsureLocationAsync();
        Provider = await api.GetProviderAsync(providerId, loc.Latitude, loc.Longitude);
    });

    [RelayCommand]
    private Task Back() => navigation.GoBackAsync();

    [RelayCommand]
    private Task RequestService() => Provider is null ? Task.CompletedTask : navigation.GoToAsync(Routes.Request, new Dictionary<string, object>
    {
        [NavParams.Provider] = Provider,
        [NavParams.CategoryId] = categoryId ?? FirstCategory(Provider) ?? string.Empty,
    });
}

/// <summary>Request a service: what, note, day, slot, address.</summary>
public sealed partial class RequestViewModel(
    IExpoviceApi api,
    UserContext user,
    INavigationService navigation,
    TimeProvider time,
    ILogger<RequestViewModel> logger,
    IDialogService dialogs) : ViewModelBase(logger, dialogs)
{
    private string categoryId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProviderName), nameof(FirstName), nameof(ProviderCaption), nameof(RatingText), nameof(PhotoUrl), nameof(SendText))]
    private ProviderDetailDto? provider;

    public string ProviderName => Provider?.Name ?? string.Empty;

    public string FirstName => Formatting.FirstName(Provider?.Name);

    public string? PhotoUrl => Provider?.PhotoUrl;

    public string ProviderCaption => Provider is null ? string.Empty
        : $"{Catalog.CategoryName(categoryId)} · {Formatting.Rupees(Provider.Rate)}/{Formatting.Unit(Provider.Unit)}";

    public string RatingText => Formatting.Rating(Provider?.Rating ?? 0);

    public string SendText => $"Send request to {FirstName}";

    public ObservableCollection<SelectableItem<string>> Skills { get; } = [];

    public ObservableCollection<SelectableItem<DateOnly>> Days { get; } = [];

    public ObservableCollection<SelectableItem<string>> Slots { get; } = [];

    [ObservableProperty]
    private string note = string.Empty;

    public int NoteMaxLength => Limits.NoteMaxLength;

    public string AddressLabel => "Home";

    public string AddressLine => user.Location?.Line ?? string.Empty;

    public bool CanSend => Selected(Skills) is not null && Selected(Slots) is not null;

    public string Hint =>
        Selected(Skills) is null ? "Pick what you need"
        : Selected(Slots) is null ? "Pick a time slot"
        : $"{FirstName} usually replies within 15 minutes";

    public override void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(NavParams.CategoryId, out var c) && c is string cat)
        {
            categoryId = cat;
        }

        if (parameters.TryGetValue(NavParams.Provider, out var p) && p is ProviderDetailDto dto)
        {
            Provider = dto;
            Build(dto, Formatting.IndiaToday(time));
        }
    }

    /// <summary>Skills offered in this category (all of the pro's skills if none match), the next 4 days and the fixed slots.</summary>
    internal void Build(ProviderDetailDto dto, DateOnly today)
    {
        var categorySkills = Catalog.ById.TryGetValue(categoryId, out var cat) ? cat.Skills : [];
        var skills = dto.Skills.Where(categorySkills.Contains).ToList();
        Fill(Skills, (skills.Count > 0 ? skills : dto.Skills).Select(s => new SelectableItem<string>(s, s, PickSkill)));

        Fill(Days, Enumerable.Range(0, Limits.BookingDaysAhead).Select(i =>
        {
            var d = today.AddDays(i);
            var (caption, label) = i switch
            {
                0 => ("Today", d.ToString("ddd d", System.Globalization.CultureInfo.InvariantCulture)),
                1 => ("Tomorrow", d.ToString("ddd d", System.Globalization.CultureInfo.InvariantCulture)),
                _ => (Formatting.ShortDay(d.DayOfWeek), d.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)),
            };
            return new SelectableItem<DateOnly>(d, label, PickDay, isSelected: i == 0, caption: caption);
        }));

        Fill(Slots, Catalog.TimeSlots.Select(s => new SelectableItem<string>(s, s, PickSlot)));
        Refresh();
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items)
        {
            target.Add(i);
        }
    }

    private void PickSkill(SelectableItem<string> item) => PickIn(Skills, item);

    private void PickDay(SelectableItem<DateOnly> item) => PickIn(Days, item);

    private void PickSlot(SelectableItem<string> item) => PickIn(Slots, item);

    private void PickIn<T>(IEnumerable<SelectableItem<T>> group, SelectableItem<T> item)
    {
        Chips.SelectOnly(group, item);
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(Hint));
        SendCommand.NotifyCanExecuteChanged();
    }

    private static T? Selected<T>(IEnumerable<SelectableItem<T>> items) where T : class => Chips.SelectedValue(items);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (Provider is null)
        {
            return;
        }

        var skill = Selected(Skills)!;
        var slot = Selected(Slots)!;
        var date = Chips.SelectedValue(Days);
        var trimmed = Note.Trim();
        var ok = await RunSafeAsync(() => api.CreateRequestAsync(
            new CreateServiceRequest(Provider.Id, categoryId, skill, trimmed.Length == 0 ? null : trimmed, date, slot)));
        if (ok)
        {
            user.LastSent = new SentRequestSummary(
                FirstName,
                skill,
                Formatting.When(date, Formatting.IndiaToday(time), slot),
                $"{Formatting.Rupees(Provider.Rate)}/{Formatting.Unit(Provider.Unit)}");
            await navigation.GoToAsync(Routes.Sent);
        }
    }

    [RelayCommand]
    private Task Back() => navigation.GoBackAsync();
}

/// <summary>"Request sent to {first}".</summary>
public sealed partial class RequestSentViewModel(UserContext user, INavigationService navigation, ILogger<RequestSentViewModel> logger, IDialogService dialogs)
    : ViewModelBase(logger, dialogs)
{
    private SentRequestSummary Summary => user.LastSent ?? new SentRequestSummary(string.Empty, string.Empty, string.Empty, string.Empty);

    public string Title => $"Request sent to {Summary.ProviderFirstName}";

    public string Body => $"{Summary.ProviderFirstName} has been notified and usually replies within 15 minutes. You'll get an SMS when they accept.";

    public string Skill => Summary.Skill;

    public string When => Summary.When;

    public string RateText => Summary.RateText;

    [RelayCommand]
    private Task ViewRequests() => navigation.GoToAsync(Routes.Requests);

    [RelayCommand]
    private Task Home() => navigation.GoToAsync(Routes.Home);
}

/// <summary>"My requests": the customer's bookings with cancel and rating.</summary>
public sealed partial class MyRequestsViewModel(
    IExpoviceApi api,
    TimeProvider time,
    ILogger<MyRequestsViewModel> logger,
    IDialogService dialogs) : ViewModelBase(logger, dialogs)
{
    public ObservableCollection<RequestItem> Items { get; } = [];

    [ObservableProperty]
    private bool isEmpty;

    public override Task OnAppearingAsync() => RunSafeAsync(async () =>
    {
        var list = await api.GetMyRequestsAsync();
        var today = Formatting.IndiaToday(time);
        Items.Clear();
        foreach (var r in list)
        {
            Items.Add(new RequestItem(r, today, CancelAsync, RateAsync));
        }

        IsEmpty = Items.Count == 0;
    });

    internal async Task CancelAsync(RequestItem item)
    {
        if (await RunSafeAsync(() => api.CancelRequestAsync(item.Id), showBusy: false))
        {
            item.Status = RequestStatus.Cancelled;
        }
    }

    /// <summary>Optimistic: stars fill immediately and roll back if the API refuses. A booking can be rated once.</summary>
    internal async Task RateAsync(RequestItem item, StarItem star)
    {
        if (!item.CanRate || item.MyRating is not null)
        {
            return;
        }

        item.ShowRating(star.Value);
        if (!await RunSafeAsync(() => api.RateRequestAsync(item.Id, star.Value), showBusy: false))
        {
            item.ShowRating(null);
        }
    }
}
