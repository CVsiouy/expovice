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

/// <summary>"My work": the pitch to list yourself, or the provider dashboard once listed.</summary>
public sealed partial class WorkViewModel(
    IExpoviceApi api,
    UserContext user,
    INavigationService navigation,
    TimeProvider time,
    ILogger<WorkViewModel> logger,
    IDialogService dialogs) : ViewModelBase(logger, dialogs)
{
    public static IReadOnlyList<ListingStep> Steps { get; } =
    [
        new(1, "About you", "Photo, name, bio and languages"),
        new(2, "What you do", "Services, skills and experience"),
        new(3, "Rate and reach", "Pricing, radius and working hours"),
        new(4, "Verify", "ID check and photos of your work"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotListed))]
    private bool isListed;

    public bool IsNotListed => !IsListed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcceptingLabel), nameof(ToggleBackgroundKey), nameof(ToggleForegroundKey), nameof(TrackKey), nameof(KnobOffset))]
    private bool isAccepting = true;

    public string AcceptingLabel => IsAccepting ? "Accepting" : "Paused";

    public string ToggleBackgroundKey => IsAccepting ? ColorKeys.Sage100 : ColorKeys.Neutral200;

    public string ToggleForegroundKey => IsAccepting ? ColorKeys.Sage800 : ColorKeys.Neutral700;

    public string TrackKey => IsAccepting ? ColorKeys.Sage600 : ColorKeys.Neutral500;

    /// <summary>Knob x-offset inside the 40px track (prototype: 19 on, 3 off).</summary>
    public double KnobOffset => IsAccepting ? 19 : 3;

    [ObservableProperty]
    private int newCount;

    [ObservableProperty]
    private int acceptedCount;

    [ObservableProperty]
    private string bookedValueText = Formatting.Rupees(0);

    [ObservableProperty]
    private string listingLine = string.Empty;

    public ObservableCollection<IncomingItem> Incoming { get; } = [];

    public override Task OnAppearingAsync() => RunSafeAsync(async () =>
    {
        await user.EnsureLoadedAsync();
        user.ProviderProfile = await api.GetMyProviderProfileAsync();
        var profile = user.ProviderProfile;
        IsListed = profile is not null;
        if (profile is null)
        {
            return;
        }

        IsAccepting = profile.IsAccepting;
        ListingLine = $"Listed in {string.Join(", ", profile.CategoryIds.Select(Catalog.CategoryName))} · {profile.RadiusKm} km radius · {Formatting.Hour(profile.From)}–{Formatting.Hour(profile.To)}";

        var incoming = await api.GetIncomingRequestsAsync();
        var today = Formatting.IndiaToday(time);
        Incoming.Clear();
        for (var i = 0; i < incoming.Count; i++)
        {
            Incoming.Add(new IncomingItem(incoming[i], i, today, ActAsync));
        }

        await RefreshSummaryAsync();
    });

    private async Task RefreshSummaryAsync()
    {
        var summary = await api.GetWorkSummaryAsync();
        NewCount = summary.NewCount;
        AcceptedCount = summary.AcceptedCount;
        BookedValueText = Formatting.Rupees(summary.BookedValue);
        user.NewRequestCount = summary.NewCount;
    }

    internal async Task ActAsync(IncomingItem item, WorkAction action)
    {
        var ok = await RunSafeAsync(async () =>
        {
            await api.WorkActionAsync(item.Id, action);
            item.Status = action switch
            {
                WorkAction.Accept => RequestStatus.Accepted,
                WorkAction.Decline => RequestStatus.Declined,
                _ => RequestStatus.Completed,
            };
            await RefreshSummaryAsync();
        }, showBusy: false);
        Logger.LogInformation("Work action {Action} on {Id}: {Result}", action, item.Id, ok ? "ok" : "failed");
    }

    /// <summary>Optimistic toggle, rolled back on failure.</summary>
    [RelayCommand]
    private async Task ToggleAccepting()
    {
        IsAccepting = !IsAccepting;
        var wanted = IsAccepting;
        if (!await RunSafeAsync(() => api.SetAcceptingAsync(wanted), showBusy: false))
        {
            IsAccepting = !wanted;
        }
    }

    [RelayCommand]
    private Task StartSetup() => navigation.GoToAsync(Routes.Setup);
}

/// <summary>Profile: who you are, shortcuts, sign out.</summary>
public sealed partial class ProfileViewModel : ViewModelBase
{
    private readonly IExpoviceApi api;
    private readonly AuthSession session;
    private readonly ISessionStore store;
    private readonly IPhotoPicker photos;
    private readonly INavigationService navigation;

    public ProfileViewModel(
        IExpoviceApi api,
        UserContext user,
        AuthSession session,
        ISessionStore store,
        IPhotoPicker photos,
        INavigationService navigation,
        ILogger<ProfileViewModel> logger,
        IDialogService dialogs)
        : base(logger, dialogs)
    {
        this.api = api;
        this.session = session;
        this.store = store;
        this.photos = photos;
        this.navigation = navigation;
        User = user;
    }

    public UserContext User { get; }

    public string DisplayName => User.DisplayName;

    public string PhoneText => Formatting.PhoneDisplay(User.Me?.PhoneNumber);

    public string? PhotoUrl => User.Me?.PhotoUrl;

    public string TagText => User.IsProvider ? "Listed as a pro" : "Customer";

    public string TagBackgroundKey => User.IsProvider ? ColorKeys.Sage100 : ColorKeys.Neutral100;

    public string TagForegroundKey => User.IsProvider ? ColorKeys.Sage800 : ColorKeys.Neutral800;

    public IReadOnlyList<ProfileRow> Rows => (ProfileRow[])
    [
        new(User.IsProvider ? "Edit provider profile" : "List yourself as a pro", "brief", new AsyncRelayCommand(() => navigation.GoToAsync(Routes.Setup))),
        new("Saved addresses", "map", new AsyncRelayCommand(() => Dialogs.ToastAsync("Saved addresses are coming soon"))),
        new("My requests", "list", new AsyncRelayCommand(() => navigation.GoToAsync(Routes.Requests))),
        new("Help & support", "help", new AsyncRelayCommand(() => Dialogs.ToastAsync("Write to support@expovice.app"))),
    ];

    public override async Task OnAppearingAsync()
    {
        await RunSafeAsync(() => User.EnsureLoadedAsync());
        foreach (var name in (string[])[nameof(DisplayName), nameof(PhoneText), nameof(PhotoUrl), nameof(TagText), nameof(TagBackgroundKey), nameof(TagForegroundKey), nameof(Rows)])
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    private async Task ChangePhoto()
    {
        var photo = await photos.PickAsync();
        if (photo is not null && await RunSafeAsync(() => api.UploadPhotoAsync(PhotoSlot.Profile, photo)))
        {
            await RunSafeAsync(() => User.LoadAsync(), showBusy: false);
            OnPropertyChanged(nameof(PhotoUrl));
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        try
        {
            if (await store.GetRefreshTokenAsync() is { } refresh)
            {
                await api.SignOutAsync(refresh);
            }
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Revoking server-side is best effort; local sign-out always happens.
            Logger.LogWarning(ex, "Server sign-out failed");
        }

        await session.ClearAsync();
        User.Reset();
        await navigation.GoToAsync(Routes.SignIn);
    }
}

/// <summary>The 4-step "list yourself" wizard (also used to edit an existing listing).</summary>
public sealed partial class SetupViewModel : ViewModelBase
{
    private static readonly string[] Titles = ["About you", "What you do", "Rate, reach and hours", "Verify and showcase"];
    private static readonly string[] Hints = ["Name is required", "Pick at least one service", "Set a rate and at least one day", string.Empty];

    private readonly IExpoviceApi api;
    private readonly UserContext user;
    private readonly IPhotoPicker photos;
    private readonly INavigationService navigation;

    // Work/ID photos picked before the first publish: the API only accepts them once the profile exists,
    // so they are held here and uploaded right after the profile is saved. UI-thread only (not thread-safe).
    private readonly Dictionary<PhotoSlot, PickedPhoto> pendingPhotos = [];
    private bool loaded;

    public SetupViewModel(IExpoviceApi api, UserContext user, IPhotoPicker photos, INavigationService navigation, ILogger<SetupViewModel> logger, IDialogService dialogs)
        : base(logger, dialogs)
    {
        this.api = api;
        this.user = user;
        this.photos = photos;
        this.navigation = navigation;
        Bars = [.. Enumerable.Range(0, 4).Select(i => new ProgressMark { IsActive = i == 0 })];
        Languages = [.. Catalog.Languages.Select(l => new SelectableItem<string>(l, l, Toggle, isSelected: l == "English"))];
        Categories = [.. Catalog.Categories.Select(c => new SelectableItem<string>(c.Id, c.Name, ToggleCategory, icon: c.Icon))];
        Units = [new(RateUnit.Visit, "Per visit", PickUnit, isSelected: true), new(RateUnit.Hour, "Per hour", PickUnit)];
        Days = [.. Formatting.Week.Select(d => new SelectableItem<DayOfWeek>(d, Formatting.ShortDay(d)[..2], Toggle, isSelected: d != DayOfWeek.Sunday))];
        IdTypes = [.. Choices.IdTypes.Select(t => new SelectableItem<IdDocumentType>(t.Type, t.Label, PickIdType, isSelected: t.Type == IdDocumentType.Aadhaar))];
    }

    // ---------- step state ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepText), nameof(Title), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(CtaText))]
    private int step = 1;

    public string StepText => $"Step {Step} of 4";

    public string Title => Titles[Step - 1];

    public bool IsStep1 => Step == 1;

    public bool IsStep2 => Step == 2;

    public bool IsStep3 => Step == 3;

    public bool IsStep4 => Step == 4;

    public ObservableCollection<ProgressMark> Bars { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CtaText))]
    private bool wasListed;

    public string CtaText => Step == 4 ? (WasListed ? "Save changes" : "Publish my listing") : "Continue";

    public bool IsInvalid => InvalidAt(Step);

    public string Hint => IsInvalid ? Hints[Step - 1] : Step == 4 ? "You can add these later. Verified pros rank higher." : string.Empty;

    // ---------- step 1 ----------

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string bio = string.Empty;

    [ObservableProperty]
    private string? profilePhotoUrl;

    public ObservableCollection<SelectableItem<string>> Languages { get; }

    // ---------- step 2 ----------

    public ObservableCollection<SelectableItem<string>> Categories { get; }

    public ObservableCollection<SelectableItem<string>> Skills { get; } = [];

    public bool HasSkills => Skills.Count > 0;

    [ObservableProperty]
    private int years = 2;

    // ---------- step 3 ----------

    [ObservableProperty]
    private string rate = string.Empty;

    public ObservableCollection<SelectableItem<RateUnit>> Units { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RadiusText))]
    private double radius = Limits.DefaultSearchRadiusKm;

    public int RadiusMin => Limits.RadiusMinKm;

    public int RadiusMax => Limits.RadiusMaxKm;

    public string RadiusText => $"{(int)Math.Round(Radius)} km";

    public string RadiusCaption => $"Customers within this distance of {user.Location?.Area ?? "your home"} will see you.";

    public ObservableCollection<SelectableItem<DayOfWeek>> Days { get; }

    public IReadOnlyList<string> Hours => Choices.Hours;

    [ObservableProperty]
    private string fromHour = "9 AM";

    [ObservableProperty]
    private string toHour = "7 PM";

    // ---------- step 4 ----------

    public ObservableCollection<SelectableItem<IdDocumentType>> IdTypes { get; }

    [ObservableProperty]
    private bool hasIdDocument;

    [ObservableProperty]
    private string? workPhoto1;

    [ObservableProperty]
    private string? workPhoto2;

    [ObservableProperty]
    private string? workPhoto3;

    // ---------- validation ----------

    internal int RateValue => int.TryParse(Rate, out var r) ? r : 0;

    internal bool InvalidAt(int s) => s switch
    {
        1 => string.IsNullOrWhiteSpace(Name),
        2 => !Categories.Any(c => c.IsSelected),
        3 => RateValue < Limits.RateMin || !Days.Any(d => d.IsSelected),
        _ => false,
    };

    private void Revalidate()
    {
        OnPropertyChanged(nameof(IsInvalid));
        OnPropertyChanged(nameof(Hint));
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnStepChanged(int value)
    {
        for (var i = 0; i < Bars.Count; i++)
        {
            Bars[i].IsActive = i < value;
        }

        Revalidate();
    }

    partial void OnNameChanged(string value) => Revalidate();

    partial void OnRateChanged(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).Take(5).ToArray());
        if (digits != value)
        {
            Rate = digits;
            return;
        }

        Revalidate();
    }

    partial void OnRadiusChanged(double value)
    {
        var snapped = Math.Round(value);
        if (Math.Abs(snapped - value) > double.Epsilon)
        {
            Radius = snapped;
        }
    }

    // ---------- chip handlers ----------

    private void Toggle<T>(SelectableItem<T> item)
    {
        Chips.Toggle(item);
        Revalidate();
    }

    private void PickUnit(SelectableItem<RateUnit> item) => Chips.SelectOnly(Units, item);

    private void PickIdType(SelectableItem<IdDocumentType> item) => Chips.SelectOnly(IdTypes, item);

    private void ToggleCategory(SelectableItem<string> item)
    {
        Chips.Toggle(item);
        RebuildSkills(Chips.Selected(Skills));
        Revalidate();
    }

    /// <summary>Skill pool = union of the chosen categories' skills; previously picked skills outside the pool are dropped.</summary>
    internal void RebuildSkills(ICollection<string> keep)
    {
        var pool = Catalog.Categories
            .Where(c => Categories.Any(x => x.IsSelected && x.Value == c.Id))
            .SelectMany(c => c.Skills)
            .Distinct()
            .ToList();
        Skills.Clear();
        foreach (var s in pool)
        {
            Skills.Add(new SelectableItem<string>(s, s, Toggle, isSelected: keep.Contains(s)));
        }

        OnPropertyChanged(nameof(HasSkills));
    }

    // ---------- lifecycle ----------

    public override async Task OnAppearingAsync()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        await RunSafeAsync(async () =>
        {
            await user.EnsureLoadedAsync();
            Name = user.Me?.Name ?? string.Empty;
            ProfilePhotoUrl = user.Me?.PhotoUrl;
            user.ProviderProfile ??= await api.GetMyProviderProfileAsync();
            if (user.ProviderProfile is { } p)
            {
                Prefill(p);
            }
        });
        OnPropertyChanged(nameof(RadiusCaption));
    }

    internal void Prefill(ProviderProfileDto p)
    {
        WasListed = true;
        Bio = p.Bio;
        foreach (var l in Languages)
        {
            l.IsSelected = p.Languages.Contains(l.Value);
        }

        foreach (var c in Categories)
        {
            c.IsSelected = p.CategoryIds.Contains(c.Value);
        }

        RebuildSkills([.. p.Skills]);
        Years = p.YearsOfExperience;
        Rate = p.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var u in Units)
        {
            u.IsSelected = u.Value == p.Unit;
        }

        Radius = p.RadiusKm;
        foreach (var d in Days)
        {
            d.IsSelected = p.WorkDays.Contains(d.Value);
        }

        FromHour = Formatting.Hour(p.From);
        ToHour = Formatting.Hour(p.To);
        foreach (var t in IdTypes)
        {
            t.IsSelected = t.Value == p.IdType;
        }

        HasIdDocument = p.HasIdDocument;
        WorkPhoto1 = p.WorkPhotoUrls.ElementAtOrDefault(0);
        WorkPhoto2 = p.WorkPhotoUrls.ElementAtOrDefault(1);
        WorkPhoto3 = p.WorkPhotoUrls.ElementAtOrDefault(2);
        Revalidate();
    }

    // ---------- commands ----------

    [RelayCommand]
    private void YearsUp() => Years = Math.Min(Limits.YearsMax, Years + 1);

    [RelayCommand]
    private void YearsDown() => Years = Math.Max(0, Years - 1);

    [RelayCommand]
    private async Task PickPhoto(PhotoSlot slot)
    {
        var photo = await photos.PickAsync();
        if (photo is null)
        {
            return;
        }

        if (slot != PhotoSlot.Profile && !WasListed)
        {
            pendingPhotos[slot] = photo;
            ShowPhoto(slot, $"data:{photo.ContentType};base64,{Convert.ToBase64String(photo.Bytes)}");
            return;
        }

        PhotoUploadResponse? result = null;
        if (await RunSafeAsync(async () => result = await api.UploadPhotoAsync(slot, photo)))
        {
            ShowPhoto(slot, result?.Url);
        }
    }

    private void ShowPhoto(PhotoSlot slot, string? url)
    {
        switch (slot)
        {
            case PhotoSlot.Profile: ProfilePhotoUrl = url; break;
            case PhotoSlot.IdFront: HasIdDocument = true; break;
            case PhotoSlot.Work1: WorkPhoto1 = url; break;
            case PhotoSlot.Work2: WorkPhoto2 = url; break;
            case PhotoSlot.Work3: WorkPhoto3 = url; break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task Next()
    {
        if (Step < 4)
        {
            Step++;
            return;
        }

        if (await SaveAsync())
        {
            await navigation.GoToAsync(WasListed ? Routes.Work : Routes.SetupToLive);
        }
    }

    private bool CanGoNext() => !IsInvalid;

    [RelayCommand]
    private Task Back()
    {
        if (Step > 1)
        {
            Step--;
            return Task.CompletedTask;
        }

        return navigation.GoBackAsync();
    }

    /// <summary>Listed pros save their edits on exit (when valid); a first-time draft is simply left.</summary>
    [RelayCommand]
    private async Task SaveAndExit()
    {
        var valid = Enumerable.Range(1, 3).All(s => !InvalidAt(s));
        if (WasListed && valid && !await SaveAsync())
        {
            return;
        }

        await navigation.GoBackAsync();
    }

    internal UpsertProviderProfileRequest BuildRequest()
    {
        var skills = Chips.Selected(Skills);
        return new UpsertProviderProfileRequest(
            Name.Trim(),
            Bio.Trim(),
            Chips.Selected(Languages),
            Chips.Selected(Categories),
            skills.Count > 0 ? skills : [.. Skills.Select(s => s.Value)],
            Years,
            RateValue,
            Chips.SelectedValue(Units),
            (int)Radius,
            Chips.Selected(Days),
            Formatting.ParseHour(FromHour),
            Formatting.ParseHour(ToHour),
            Chips.SelectedValue(IdTypes));
    }

    private Task<bool> SaveAsync() => RunSafeAsync(async () =>
    {
        await api.UpsertProviderProfileAsync(BuildRequest());
        foreach (var (slot, photo) in pendingPhotos.ToList())
        {
            var uploaded = await api.UploadPhotoAsync(slot, photo);
            pendingPhotos.Remove(slot);
            ShowPhoto(slot, uploaded.Url);
        }

        await user.LoadAsync();
        user.ProviderProfile ??= await api.GetMyProviderProfileAsync();
    });
}

/// <summary>"You're live on Expovice".</summary>
public sealed partial class LiveViewModel(UserContext user, INavigationService navigation, ILogger<LiveViewModel> logger, IDialogService dialogs)
    : ViewModelBase(logger, dialogs)
{
    private ProviderProfileDto? Profile => user.ProviderProfile;

    private string CategoryNames => Profile is null ? "your services" : string.Join(", ", Profile.CategoryIds.Select(Catalog.CategoryName));

    public string DisplayName => user.DisplayName;

    public string? PhotoUrl => user.Me?.PhotoUrl;

    public string Body => $"Customers within {Profile?.RadiusKm ?? Limits.DefaultSearchRadiusKm} km can now find you under {CategoryNames}. New requests will show up in My work.";

    public IReadOnlyList<string> Skills => Profile?.Skills.Take(3).ToList() ?? [];

    public string RateText => Profile is null ? string.Empty : $"{Formatting.Rupees(Profile.Rate)}/{Formatting.Unit(Profile.Unit)}";

    [RelayCommand]
    private Task GoToDashboard() => navigation.GoToAsync(Routes.Work);
}
