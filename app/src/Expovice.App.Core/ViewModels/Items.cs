using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Expovice.App.Core.Ui;
using Expovice.Contracts;

namespace Expovice.App.Core.ViewModels;

// Row/tile models bound inside CollectionViews and BindableLayouts. Each carries its own commands (wired by the
// owning view model through delegates) so XAML never needs RelativeSource bindings back to the page.

/// <summary>A category card on Home.</summary>
public sealed class CategoryTile
{
    public CategoryTile(ServiceCategory category, int index, Action<CategoryTile> open)
    {
        Category = category;
        // The prototype cycles three color pairs: accent, sage, neutral.
        BackgroundKey = (index % 3) switch
        {
            1 => ColorKeys.Sage200,
            2 => ColorKeys.Neutral100,
            _ => ColorKeys.Accent200,
        };
        ForegroundKey = index % 3 == 1 ? ColorKeys.Sage800 : ColorKeys.Accent800;
        OpenCommand = new RelayCommand(() => open(this));
    }

    public ServiceCategory Category { get; }

    public string Id => Category.Id;

    public string Name => Category.Name;

    public string Icon => Category.Icon;

    public string BackgroundKey { get; }

    public string ForegroundKey { get; }

    public IRelayCommand OpenCommand { get; }
}

/// <summary>A provider card (Home "Top rated" and the category listing).</summary>
public sealed class ProviderItem
{
    public ProviderItem(ProviderSummaryDto dto, Action<ProviderItem> open)
    {
        Dto = dto;
        OpenCommand = new RelayCommand(() => open(this));
    }

    public ProviderSummaryDto Dto { get; }

    public Guid Id => Dto.Id;

    public string Name => Dto.Name;

    public string? PhotoUrl => Dto.PhotoUrl;

    public string CategoryId => Dto.CategoryId;

    public string CategoryName => Catalog.CategoryName(Dto.CategoryId);

    public IReadOnlyList<string> Skills => Dto.Skills.Take(3).ToList();

    public bool IsVerified => Dto.IsVerified;

    public string RatingText => Formatting.Rating(Dto.Rating);

    public string RequestsText => $"{Dto.RequestCount} requests";

    public string DistanceText => Formatting.Distance(Dto.DistanceKm);

    public string CategoryAndDistance => $"{CategoryName} · {DistanceText}";

    public string RateText => Formatting.Rupees(Dto.Rate);

    public string UnitText => " /" + Formatting.Unit(Dto.Unit);

    public string AvailabilityText => Dto.AvailableToday ? "Available today" : "From tomorrow";

    public string AvailabilityBackgroundKey => Dto.AvailableToday ? ColorKeys.Sage100 : ColorKeys.Neutral200;

    public string AvailabilityForegroundKey => Dto.AvailableToday ? ColorKeys.Sage800 : ColorKeys.Neutral700;

    public IRelayCommand OpenCommand { get; }
}

/// <summary>One of the five rating stars.</summary>
public sealed partial class StarItem : ObservableObject
{
    public StarItem(int value, Action<StarItem> pick)
    {
        Value = value;
        PickCommand = new RelayCommand(() => pick(this));
    }

    public int Value { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorKey))]
    private bool isOn;

    public string ColorKey => IsOn ? ColorKeys.Accent : ColorKeys.Neutral500;

    public IRelayCommand PickCommand { get; }

    public static List<StarItem> Row(int? rating, Action<StarItem> pick) =>
        Enumerable.Range(1, 5).Select(n => new StarItem(n, pick) { IsOn = n <= (rating ?? 0) }).ToList();
}

/// <summary>Shared status-tag behaviour for customer bookings and provider incoming requests.</summary>
public abstract partial class StatusItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusBackgroundKey), nameof(StatusForegroundKey))]
    private RequestStatus status;

    public string StatusText => Status.ToString();

    public string StatusBackgroundKey => StatusStyle.For(Status).Background;

    public string StatusForegroundKey => StatusStyle.For(Status).Foreground;

    partial void OnStatusChanged(RequestStatus value) => OnStatusUpdated();

    /// <summary>Lets subclasses raise change notifications for status-derived properties.</summary>
    protected virtual void OnStatusUpdated()
    {
    }
}

/// <summary>A booking on "My requests".</summary>
public sealed partial class RequestItem : StatusItem
{
    public RequestItem(ServiceRequestDto dto, DateOnly today, Func<RequestItem, Task> cancel, Func<RequestItem, StarItem, Task> rate)
    {
        Dto = dto;
        Status = dto.Status;
        MyRating = dto.MyRating;
        WhenText = Formatting.When(dto.Date, today, dto.Slot);
        Stars = StarItem.Row(dto.MyRating, star => _ = rate(this, star));
        CancelCommand = new AsyncRelayCommand(() => cancel(this));
    }

    public ServiceRequestDto Dto { get; }

    public Guid Id => Dto.Id;

    public string ProviderName => Dto.ProviderName;

    public string RateLabel => "Rate " + Formatting.FirstName(Dto.ProviderName);

    public string? ProviderPhotoUrl => Dto.ProviderPhotoUrl;

    public string Subtitle => $"{Catalog.CategoryName(Dto.CategoryId)} · {Dto.Skill}";

    public string WhenText { get; }

    public IReadOnlyList<StarItem> Stars { get; }

    [ObservableProperty]
    private int? myRating;

    public bool CanCancel => Status is RequestStatus.Pending or RequestStatus.Accepted;

    public bool CanRate => Status == RequestStatus.Completed;

    public IAsyncRelayCommand CancelCommand { get; }

    protected override void OnStatusUpdated()
    {
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRate));
    }

    public void ShowRating(int? rating)
    {
        MyRating = rating;
        foreach (var s in Stars)
        {
            s.IsOn = s.Value <= (rating ?? 0);
        }
    }
}

/// <summary>An incoming request on the provider dashboard.</summary>
public sealed partial class IncomingItem : StatusItem
{
    private static readonly string[] AvatarKeys = [ColorKeys.Accent500, ColorKeys.Sage600, ColorKeys.Accent700];

    public IncomingItem(IncomingRequestDto dto, int index, DateOnly today, Func<IncomingItem, Api.WorkAction, Task> act)
    {
        Dto = dto;
        Status = dto.Status;
        AvatarKey = AvatarKeys[index % AvatarKeys.Length];
        WhenText = Formatting.When(dto.Date, today, dto.Slot);
        AcceptCommand = new AsyncRelayCommand(() => act(this, Api.WorkAction.Accept));
        DeclineCommand = new AsyncRelayCommand(() => act(this, Api.WorkAction.Decline));
        CompleteCommand = new AsyncRelayCommand(() => act(this, Api.WorkAction.Complete));
    }

    public IncomingRequestDto Dto { get; }

    public Guid Id => Dto.Id;

    public string CustomerName => Dto.CustomerName;

    public string Initial => Dto.CustomerName.Length > 0 ? Dto.CustomerName[..1].ToUpperInvariant() : "?";

    public string AvatarKey { get; }

    public string Subtitle => $"{Formatting.Distance(Dto.DistanceKm)} away · {Dto.Area}";

    public string Skill => Dto.Skill;

    public string WhenText { get; }

    public string SkillAndWhen => $"{Dto.Skill} · {WhenText}";

    public string? Note => string.IsNullOrWhiteSpace(Dto.Note) ? null : $"“{Dto.Note}”";

    public bool HasNote => Note is not null;

    public bool IsPending => Status == RequestStatus.Pending;

    public bool IsAccepted => Status == RequestStatus.Accepted;

    public IAsyncRelayCommand AcceptCommand { get; }

    public IAsyncRelayCommand DeclineCommand { get; }

    public IAsyncRelayCommand CompleteCommand { get; }

    protected override void OnStatusUpdated()
    {
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsAccepted));
    }
}

/// <summary>Onboarding page dot / setup progress bar segment.</summary>
public sealed partial class ProgressMark : ObservableObject
{
    [ObservableProperty]
    private bool isActive;
}

/// <summary>One OTP digit box.</summary>
public sealed partial class OtpBox : ObservableObject
{
    [ObservableProperty]
    private string digit = string.Empty;

    /// <summary>The box the next digit goes into (accent border).</summary>
    [ObservableProperty]
    private bool isActive;
}

/// <summary>Numbered step on the "Earn with your skills" card.</summary>
public sealed record ListingStep(int Number, string Title, string Description);

/// <summary>A row on the Profile screen.</summary>
public sealed record ProfileRow(string Label, string Icon, IRelayCommand Command);
