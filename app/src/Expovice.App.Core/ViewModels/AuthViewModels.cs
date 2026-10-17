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

/// <summary>Splash: shows for 1.8 s (or until tapped) while the stored session is restored.</summary>
public sealed partial class SplashViewModel(
    AuthSession session,
    ISessionStore store,
    SignInFlow flow,
    INavigationService navigation,
    TimeProvider time,
    ILogger<SplashViewModel> logger,
    IDialogService dialogs) : ViewModelBase(logger, dialogs)
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(1800);

    private readonly TaskCompletionSource tapped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool started;

    public override async Task OnAppearingAsync()
    {
        if (started)
        {
            return;
        }

        started = true;
        var minimumShow = Task.WhenAny(Task.Delay(Duration, time), tapped.Task);
        var restored = false;
        try
        {
            restored = await session.TryRestoreAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Offline at launch: send the user to sign-in; the stored token is kept for next time.
            Logger.LogWarning(ex, "Session restore failed");
        }

        await minimumShow;
        if (restored && await RunSafeAsync(() => flow.CompleteAsync(isNewUser: false, fromSplash: true), showBusy: false))
        {
            return;
        }

        await navigation.GoToAsync(store.HasSeenOnboarding ? Routes.SignIn : Routes.Onboarding);
    }

    [RelayCommand]
    private void Skip() => tapped.TrySetResult();
}

/// <summary>Three onboarding pages with dots and Skip / Next / Get started.</summary>
public sealed partial class OnboardingViewModel : ViewModelBase
{
    public sealed record Page(string Title, string Body, string Icon, bool IconFilled, string Circle1Key, string Circle2Key);

    private readonly ISessionStore store;
    private readonly INavigationService navigation;

    public OnboardingViewModel(ISessionStore store, INavigationService navigation, ILogger<OnboardingViewModel> logger, IDialogService dialogs)
        : base(logger, dialogs)
    {
        this.store = store;
        this.navigation = navigation;
        Pages =
        [
            new("Help, a few streets away", $"See electricians, plumbers, dog walkers and more within {Limits.DefaultSearchRadiusKm} km of your home.", "pin", false, ColorKeys.Accent200, ColorKeys.Sage300),
            new("Pick by rating and track record", "Every pro shows their skills, how many requests they have handled and a rating out of 5.", "star", true, ColorKeys.Sage200, ColorKeys.Accent300),
            new("Earn with your skills", "One account does both. List yourself in a few minutes and get requests from neighbours.", "brief", false, ColorKeys.Accent300, ColorKeys.Sage200),
        ];
        Dots = new(Pages.Select((_, i) => new ProgressMark { IsActive = i == 0 }));
    }

    public IReadOnlyList<Page> Pages { get; }

    public ObservableCollection<ProgressMark> Dots { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(CtaText))]
    private int index;

    public Page Current => Pages[Index];

    public string CtaText => Index == Pages.Count - 1 ? "Get started" : "Next";

    partial void OnIndexChanged(int value)
    {
        for (var i = 0; i < Dots.Count; i++)
        {
            Dots[i].IsActive = i == value;
        }
    }

    [RelayCommand]
    private Task Next()
    {
        if (Index < Pages.Count - 1)
        {
            Index++;
            return Task.CompletedTask;
        }

        return Skip();
    }

    [RelayCommand]
    private Task Skip()
    {
        store.HasSeenOnboarding = true;
        return navigation.GoToAsync(Routes.SignIn);
    }
}

/// <summary>"Sign in or create an account": +91 mobile number, Google, Apple.</summary>
public sealed partial class PhoneSignInViewModel(
    IExpoviceApi api,
    AuthSession session,
    IExternalAuthService external,
    SignInFlow flow,
    INavigationService navigation,
    ILogger<PhoneSignInViewModel> logger,
    IDialogService dialogs) : ViewModelBase(logger, dialogs)
{
    public const string CountryCode = "+91";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string phone = string.Empty;

    /// <summary>Indian mobile numbers: 10 digits starting 6–9.</summary>
    public static bool IsValidIndianMobile(string digits) =>
        digits.Length == 10 && digits[0] is >= '6' and <= '9' && digits.All(char.IsAsciiDigit);

    partial void OnPhoneChanged(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).Take(10).ToArray());
        if (digits != value)
        {
            Phone = digits;
        }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task Continue()
    {
        var e164 = CountryCode + Phone;
        if (await RunSafeAsync(() => api.SendOtpAsync(e164)))
        {
            await navigation.GoToAsync(Routes.Otp, new Dictionary<string, object> { [NavParams.PhoneNumber] = e164 });
        }
    }

    private bool CanContinue() => IsValidIndianMobile(Phone);

    [RelayCommand]
    private Task Google() => ExternalAsync(ExternalProvider.Google);

    [RelayCommand]
    private Task Apple() => ExternalAsync(ExternalProvider.Apple);

    private async Task ExternalAsync(ExternalProvider provider)
    {
        await RunSafeAsync(async () =>
        {
            var result = await external.AuthenticateAsync(provider);
            if (result is null)
            {
                return; // user closed the browser
            }

            await session.SignInWithRefreshTokenAsync(result.RefreshToken);
            await flow.CompleteAsync(result.IsNewUser);
        });
    }
}

/// <summary>"Enter the code" with one box per digit and a resend countdown.</summary>
public sealed partial class OtpViewModel : ViewModelBase
{
    private readonly IExpoviceApi api;
    private readonly AuthSession session;
    private readonly SignInFlow flow;
    private readonly INavigationService navigation;
    private readonly TimeProvider time;
    private int countdownVersion;

    public OtpViewModel(IExpoviceApi api, AuthSession session, SignInFlow flow, INavigationService navigation, TimeProvider time, ILogger<OtpViewModel> logger, IDialogService dialogs)
        : base(logger, dialogs)
    {
        this.api = api;
        this.session = session;
        this.flow = flow;
        this.navigation = navigation;
        this.time = time;
        Boxes = new(Enumerable.Range(0, Limits.OtpLength).Select(i => new OtpBox { IsActive = i == 0 }));
    }

    public ObservableCollection<OtpBox> Boxes { get; }

    public int CodeLength => Limits.OtpLength;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private string phoneNumber = string.Empty;

    public string Subtitle => $"Sent by SMS to {Formatting.PhoneDisplay(PhoneNumber)}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResendText), nameof(CanResend))]
    [NotifyCanExecuteChangedFor(nameof(ResendCommand))]
    private int resendSeconds;

    public bool CanResend => ResendSeconds == 0;

    public string ResendText => CanResend ? "Resend code" : $"Resend in 0:{ResendSeconds:00}";

    public override void ApplyParameters(IDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue(NavParams.PhoneNumber, out var p) && p is string phone)
        {
            PhoneNumber = phone;
            StartCountdown();
        }
    }

    /// <summary>Stops the countdown loop on its next tick.</summary>
    public override void OnDisappearing() => countdownVersion++;

    partial void OnCodeChanged(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).Take(Limits.OtpLength).ToArray());
        if (digits != value)
        {
            Code = digits;
            return;
        }

        for (var i = 0; i < Boxes.Count; i++)
        {
            Boxes[i].Digit = i < digits.Length ? digits[i].ToString() : string.Empty;
            Boxes[i].IsActive = i == digits.Length;
        }
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task Verify()
    {
        var ok = await RunSafeAsync(async () =>
        {
            var response = await api.VerifyOtpAsync(PhoneNumber, Code);
            await session.SignInAsync(response);
            await flow.CompleteAsync(response.IsNewUser);
        });
        if (!ok)
        {
            Code = string.Empty;
        }
    }

    private bool CanVerify() => Code.Length == Limits.OtpLength;

    [RelayCommand(CanExecute = nameof(CanResend))]
    private async Task Resend()
    {
        if (await RunSafeAsync(() => api.SendOtpAsync(PhoneNumber)))
        {
            Code = string.Empty;
            StartCountdown();
        }
    }

    [RelayCommand]
    private Task Back() => navigation.GoBackAsync();

    private void StartCountdown() => _ = CountdownAsync(++countdownVersion);

    /// <summary>
    /// Awaited on the UI context, so each decrement lands on the UI thread. A version number (instead of a
    /// CancellationTokenSource) stops stale loops without owning a disposable.
    /// </summary>
    internal async Task CountdownAsync(int version)
    {
        ResendSeconds = Limits.OtpResendSeconds;
        while (ResendSeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), time);
            if (version != countdownVersion)
            {
                return;
            }

            ResendSeconds--;
        }
    }
}

/// <summary>"What should we call you?"</summary>
public sealed partial class NameViewModel(
    IExpoviceApi api,
    UserContext user,
    INavigationService navigation,
    ILogger<NameViewModel> logger,
    IDialogService dialogs) : ViewModelBase(logger, dialogs)
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
    private string name = string.Empty;

    public int MaxLength => Limits.NameMaxLength;

    public override Task OnAppearingAsync()
    {
        if (string.IsNullOrEmpty(Name))
        {
            Name = user.Me?.Name ?? string.Empty;
        }

        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAccount()
    {
        var ok = await RunSafeAsync(async () =>
        {
            await api.UpdateNameAsync(Name.Trim());
            await user.LoadAsync();
            await user.EnsureLocationAsync();
        });
        if (ok)
        {
            await navigation.GoToAsync(Routes.Home);
        }
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(Name);
}
