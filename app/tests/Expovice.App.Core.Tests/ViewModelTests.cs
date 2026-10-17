using System.Net;
using Expovice.App.Core.Abstractions;
using Expovice.App.Core.Api;
using Expovice.App.Core.Services;
using Expovice.App.Core.ViewModels;
using Expovice.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Expovice.App.Core.Tests;

public sealed class ViewModelTests
{
    /// <summary>Saturday 17 Oct 2026, 10:00 IST.</summary>
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 17, 4, 30, 0, TimeSpan.Zero));

    private readonly IExpoviceApi api = Substitute.For<IExpoviceApi>();
    private readonly INavigationService nav = Substitute.For<INavigationService>();
    private readonly IDialogService dialogs = Substitute.For<IDialogService>();

    private static ProviderDetailDto Provider(params string[] skills) => new(
        Guid.NewGuid(), "Imran Khan", null, ["plumber"], skills, 4.6, 30, 210, 6, 1.4, 349, RateUnit.Visit, true, true,
        "Bio", ["Hindi", "English"], [DayOfWeek.Monday, DayOfWeek.Tuesday], new TimeOnly(9, 0), new TimeOnly(19, 0), []);

    // ---------- sign in ----------

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("6000000000", true)]
    [InlineData("5876543210", false)]
    [InlineData("987654321", false)]
    public void Indian_mobile_validation(string digits, bool valid) =>
        Assert.Equal(valid, PhoneSignInViewModel.IsValidIndianMobile(digits));

    [Fact]
    public async Task Phone_is_sanitised_and_continue_sends_e164_then_opens_otp()
    {
        var vm = new PhoneSignInViewModel(api, null!, Substitute.For<IExternalAuthService>(), null!, nav, NullLogger<PhoneSignInViewModel>.Instance, dialogs);

        vm.Phone = "98765 43210 99";

        Assert.Equal("9876543210", vm.Phone);
        Assert.True(vm.ContinueCommand.CanExecute(null));
        await vm.ContinueCommand.ExecuteAsync(null);
        await api.Received(1).SendOtpAsync("+919876543210", Arg.Any<CancellationToken>());
        await nav.Received(1).GoToAsync(Routes.Otp, Arg.Is<IDictionary<string, object>>(d => (string)d[NavParams.PhoneNumber] == "+919876543210"));
    }

    [Fact]
    public async Task Otp_boxes_follow_the_code_and_countdown_reaches_zero()
    {
        var time = new FakeTimeProvider(Time.GetUtcNow());
        var vm = new OtpViewModel(api, null!, null!, nav, time, NullLogger<OtpViewModel>.Instance, dialogs);
        vm.ApplyParameters(new Dictionary<string, object> { [NavParams.PhoneNumber] = "+919876543210" });

        vm.Code = "12a34567";

        Assert.Equal("123456", vm.Code);
        Assert.Equal(Limits.OtpLength, vm.Boxes.Count);
        Assert.Equal("3", vm.Boxes[2].Digit);
        Assert.True(vm.VerifyCommand.CanExecute(null));
        Assert.Equal("Sent by SMS to +91 98765 43210", vm.Subtitle);

        Assert.False(vm.CanResend);
        for (var i = 0; i < Limits.OtpResendSeconds; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        Assert.True(vm.CanResend);
        Assert.Equal("Resend code", vm.ResendText);
    }

    // ---------- listing ----------

    [Fact]
    public async Task Listing_chips_map_to_search_parameters()
    {
        api.SearchProvidersAsync(Arg.Any<ProviderQuery>(), Arg.Any<CancellationToken>()).Returns(new ProviderSearchResult([], 3));
        var vm = new ListingViewModel(api, Fakes.User(api), nav, NullLogger<ListingViewModel>.Instance, dialogs);
        vm.ApplyParameters(new Dictionary<string, object> { [NavParams.CategoryId] = "plumber" });

        vm.Chips[1].SelectCommand.Execute(null); // Top rated
        vm.Chips[2].SelectCommand.Execute(null); // Available today
        vm.Chips[3].SelectCommand.Execute(null); // Under ₹400
        await vm.LoadAsync();

        Assert.False(vm.Chips[0].IsSelected);
        await api.Received().SearchProvidersAsync(
            new ProviderQuery("plumber", 12.9719, 77.6412, 5, ProviderSort.Rating, true, 399), Arg.Any<CancellationToken>());
        Assert.Equal("Plumber", vm.Title);
        Assert.True(vm.IsEmpty);
        Assert.Equal("3 more beyond 5 km aren't shown.", vm.BeyondText);
        Assert.Equal("0 pros within 5 km of Indiranagar", vm.Subtitle);
    }

    // ---------- request ----------

    [Fact]
    public async Task Request_days_hint_and_send()
    {
        var user = Fakes.User(api);
        var vm = new RequestViewModel(api, user, nav, Time, NullLogger<RequestViewModel>.Instance, dialogs);
        vm.ApplyParameters(new Dictionary<string, object>
        {
            [NavParams.CategoryId] = "plumber",
            [NavParams.Provider] = Provider("Leak repair", "Tap fitting", "Wiring"),
        });

        Assert.Equal(["Today", "Tomorrow", "Mon", "Tue"], vm.Days.Select(d => d.Caption));
        Assert.Equal(["Sat 17", "Sun 18", "Oct 19", "Oct 20"], vm.Days.Select(d => d.Label));
        Assert.Equal(["Leak repair", "Tap fitting"], vm.Skills.Select(s => s.Label)); // only this category's skills
        Assert.Equal("Pick what you need", vm.Hint);
        Assert.False(vm.SendCommand.CanExecute(null));

        vm.Skills[0].SelectCommand.Execute(null);
        Assert.Equal("Pick a time slot", vm.Hint);
        vm.Days[2].SelectCommand.Execute(null);
        vm.Slots[1].SelectCommand.Execute(null);
        Assert.Equal("Imran usually replies within 15 minutes", vm.Hint);
        Assert.True(vm.SendCommand.CanExecute(null));

        await vm.SendCommand.ExecuteAsync(null);

        await api.Received(1).CreateRequestAsync(
            Arg.Is<CreateServiceRequest>(r => r.Skill == "Leak repair" && r.Slot == "11–1 PM" && r.Date == new DateOnly(2026, 10, 19) && r.Note == null),
            Arg.Any<CancellationToken>());
        Assert.Equal("Mon 19 Oct · 11–1 PM", user.LastSent!.When);
        await nav.Received(1).GoToAsync(Routes.Sent);
    }

    // ---------- setup ----------

    [Fact]
    public void Setup_validation_and_hints_follow_the_prototype()
    {
        var vm = new SetupViewModel(api, Fakes.User(api), Substitute.For<IPhotoPicker>(), nav, NullLogger<SetupViewModel>.Instance, dialogs);

        Assert.True(vm.IsInvalid);
        Assert.Equal("Name is required", vm.Hint);
        vm.Name = "Asha Rao";
        Assert.False(vm.IsInvalid);
        vm.NextCommand.Execute(null);

        Assert.Equal("What you do", vm.Title);
        Assert.Equal("Pick at least one service", vm.Hint);
        vm.Categories.First(c => c.Value == "electrician").SelectCommand.Execute(null);
        Assert.False(vm.IsInvalid);
        vm.NextCommand.Execute(null);

        Assert.Equal("Set a rate and at least one day", vm.Hint);
        vm.Rate = "₹35a0";
        Assert.Equal("350", vm.Rate);
        Assert.False(vm.IsInvalid);
        foreach (var d in vm.Days.Where(d => d.IsSelected).ToList())
        {
            d.SelectCommand.Execute(null);
        }

        Assert.True(vm.IsInvalid);
        vm.Days[0].SelectCommand.Execute(null);
        vm.NextCommand.Execute(null);

        Assert.Equal(4, vm.Step);
        Assert.Equal("Publish my listing", vm.CtaText);
        Assert.Equal("You can add these later. Verified pros rank higher.", vm.Hint);
        Assert.True(vm.Bars.All(b => b.IsActive));
    }

    [Fact]
    public void Deselecting_a_category_prunes_its_skills()
    {
        var vm = new SetupViewModel(api, Fakes.User(api), Substitute.For<IPhotoPicker>(), nav, NullLogger<SetupViewModel>.Instance, dialogs);
        var electrician = vm.Categories.First(c => c.Value == "electrician");
        var plumber = vm.Categories.First(c => c.Value == "plumber");
        electrician.SelectCommand.Execute(null);
        plumber.SelectCommand.Execute(null);
        vm.Skills.First(s => s.Value == "Wiring").SelectCommand.Execute(null);
        vm.Skills.First(s => s.Value == "Leak repair").SelectCommand.Execute(null);

        electrician.SelectCommand.Execute(null);

        Assert.DoesNotContain(vm.Skills, s => s.Value == "Wiring");
        Assert.Equal(["Leak repair"], vm.Skills.Where(s => s.IsSelected).Select(s => s.Value));
    }

    [Fact]
    public async Task First_publish_uploads_work_photos_only_after_the_profile_exists()
    {
        var picker = Substitute.For<IPhotoPicker>();
        var photo = new PickedPhoto([1, 2, 3], "image/jpeg", "work.jpg");
        picker.PickAsync().Returns(photo);
        api.UploadPhotoAsync(PhotoSlot.Work1, photo, Arg.Any<CancellationToken>())
            .Returns(new PhotoUploadResponse(PhotoSlot.Work1, "https://blob/work1.jpg"));
        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(new MeDto(Guid.NewGuid(), "Asha Rao", null, null, null, IsProvider: false));
        var vm = new SetupViewModel(api, Fakes.User(api), picker, nav, NullLogger<SetupViewModel>.Instance, dialogs) { Name = "Asha Rao", Rate = "400" };
        vm.Categories.First(c => c.Value == "dog").SelectCommand.Execute(null);

        await vm.PickPhotoCommand.ExecuteAsync(PhotoSlot.Work1);

        await api.DidNotReceiveWithAnyArgs().UploadPhotoAsync(default, null!, default);
        Assert.StartsWith("data:image/jpeg;base64,", vm.WorkPhoto1);

        for (var i = 0; i < 4; i++)
        {
            await vm.NextCommand.ExecuteAsync(null); // steps 1→2→3→4, then publish
        }

        Received.InOrder(() =>
        {
            api.UpsertProviderProfileAsync(Arg.Any<UpsertProviderProfileRequest>(), Arg.Any<CancellationToken>());
            api.UploadPhotoAsync(PhotoSlot.Work1, photo, Arg.Any<CancellationToken>());
        });
        Assert.Equal("https://blob/work1.jpg", vm.WorkPhoto1);
    }

    [Fact]
    public void Setup_builds_the_upsert_request()
    {
        var vm = new SetupViewModel(api, Fakes.User(api), Substitute.For<IPhotoPicker>(), nav, NullLogger<SetupViewModel>.Instance, dialogs)
        {
            Name = " Asha Rao ",
            Rate = "400",
            Radius = 7.4,
            FromHour = "8 AM",
            ToHour = "6 PM",
        };
        vm.Categories.First(c => c.Value == "dog").SelectCommand.Execute(null);
        vm.Units[1].SelectCommand.Execute(null);

        var r = vm.BuildRequest();

        Assert.Equal("Asha Rao", r.Name);
        Assert.Equal(["dog"], r.CategoryIds);
        Assert.Equal(5, r.Skills.Count); // none picked → whole pool
        Assert.Equal(RateUnit.Hour, r.Unit);
        Assert.Equal(7, r.RadiusKm);
        Assert.Equal(new TimeOnly(8, 0), r.From);
        Assert.Equal(new TimeOnly(18, 0), r.To);
        Assert.Equal(6, r.WorkDays.Count);
        Assert.Equal(["English"], r.Languages);
    }

    // ---------- my requests ----------

    private static ServiceRequestDto Booking(RequestStatus status) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Suresh Gowda", null, "plumber", "Leak repair", null, new DateOnly(2026, 10, 14), "11–1 PM", status, null, 349, RateUnit.Visit);

    [Fact]
    public async Task Rating_is_optimistic_and_rolls_back_on_failure()
    {
        var done = Booking(RequestStatus.Completed);
        api.GetMyRequestsAsync(Arg.Any<CancellationToken>()).Returns([done]);
        api.RateRequestAsync(done.Id, 4, Arg.Any<CancellationToken>()).ThrowsAsync(new ApiException(HttpStatusCode.Conflict, "Already rated"));
        var vm = new MyRequestsViewModel(api, Time, NullLogger<MyRequestsViewModel>.Instance, dialogs);
        await vm.OnAppearingAsync();
        var item = Assert.Single(vm.Items);

        await vm.RateAsync(item, item.Stars[3]);

        Assert.Null(item.MyRating);
        Assert.All(item.Stars, s => Assert.False(s.IsOn));
        await dialogs.Received(1).ToastAsync("Already rated");

        api.RateRequestAsync(done.Id, 5, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await vm.RateAsync(item, item.Stars[4]);
        Assert.Equal(5, item.MyRating);
        Assert.All(item.Stars, s => Assert.True(s.IsOn));
        Assert.Equal("Rate Suresh", item.RateLabel);
        Assert.Equal("Wed 14 Oct · 11–1 PM", item.WhenText);
    }

    [Fact]
    public async Task Cancelling_updates_status_and_actions()
    {
        api.GetMyRequestsAsync(Arg.Any<CancellationToken>()).Returns([Booking(RequestStatus.Pending)]);
        var vm = new MyRequestsViewModel(api, Time, NullLogger<MyRequestsViewModel>.Instance, dialogs);
        await vm.OnAppearingAsync();
        var item = vm.Items[0];
        Assert.True(item.CanCancel);

        await item.CancelCommand.ExecuteAsync(null);

        Assert.Equal(RequestStatus.Cancelled, item.Status);
        Assert.False(item.CanCancel);
        Assert.Equal(ColorKeysOf(RequestStatus.Cancelled), (item.StatusBackgroundKey, item.StatusForegroundKey));
    }

    private static (string, string) ColorKeysOf(RequestStatus s) => Ui.StatusStyle.For(s);

    // ---------- work ----------

    [Fact]
    public async Task Accepting_an_incoming_request_updates_status_and_counts()
    {
        var profile = new ProviderProfileDto("", ["English"], ["electrician"], ["Wiring"], 3, 350, RateUnit.Visit, 5,
            [DayOfWeek.Monday], new TimeOnly(9, 0), new TimeOnly(19, 0), IdDocumentType.Aadhaar, false, false, true, []);
        var incoming = new IncomingRequestDto(Guid.NewGuid(), "Nisha Menon", 0.8, "HAL 2nd Stage", "Fan & light fitting",
            "Two ceiling fans", new DateOnly(2026, 10, 17), "5–7 PM", RequestStatus.Pending);
        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(new MeDto(Guid.NewGuid(), "Asha", "+919876543210", null, null, true));
        api.GetMyProviderProfileAsync(Arg.Any<CancellationToken>()).Returns(profile);
        api.GetIncomingRequestsAsync(Arg.Any<CancellationToken>()).Returns([incoming]);
        api.GetWorkSummaryAsync(Arg.Any<CancellationToken>()).Returns(new WorkSummaryDto(1, 0, 0), new WorkSummaryDto(0, 1, 350));
        var user = Fakes.User(api);
        var vm = new WorkViewModel(api, user, nav, Time, NullLogger<WorkViewModel>.Instance, dialogs);

        await vm.OnAppearingAsync();

        Assert.True(vm.IsListed);
        Assert.Equal("Listed in Electrician · 5 km radius · 9 AM–7 PM", vm.ListingLine);
        var item = Assert.Single(vm.Incoming);
        Assert.Equal("N", item.Initial);
        Assert.Equal("Fan & light fitting · Today · 5–7 PM", item.SkillAndWhen);
        Assert.True(item.IsPending);
        Assert.Equal(1, user.NewRequestCount);

        await item.AcceptCommand.ExecuteAsync(null);

        await api.Received(1).WorkActionAsync(incoming.Id, WorkAction.Accept, Arg.Any<CancellationToken>());
        Assert.True(item.IsAccepted);
        Assert.Equal(0, vm.NewCount);
        Assert.Equal(1, vm.AcceptedCount);
        Assert.Equal("₹350", vm.BookedValueText);
        Assert.Equal(0, user.NewRequestCount);
    }

    [Fact]
    public async Task Accepting_toggle_rolls_back_when_the_api_fails()
    {
        api.SetAcceptingAsync(false, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("offline"));
        var vm = new WorkViewModel(api, Fakes.User(api), nav, Time, NullLogger<WorkViewModel>.Instance, dialogs);

        await vm.ToggleAcceptingCommand.ExecuteAsync(null);

        Assert.True(vm.IsAccepting);
        Assert.Equal(ViewModelBase.OfflineMessage, vm.ErrorMessage);
    }

    // ---------- detail ----------

    [Fact]
    public void Availability_falls_back_to_next_working_day()
    {
        var today = new DateOnly(2026, 10, 17); // Saturday
        var p = Provider() with { AvailableToday = false };

        Assert.Equal("From Mon", ProviderDetailViewModel.Availability(p, today));
        Assert.Equal("From tomorrow", ProviderDetailViewModel.Availability(p with { WorkDays = [DayOfWeek.Sunday] }, today));
        Assert.Equal("Available today", ProviderDetailViewModel.Availability(p with { AvailableToday = true }, today));
    }

    [Fact]
    public async Task Sign_in_flow_routes_new_users_to_the_name_screen()
    {
        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(new MeDto(Guid.NewGuid(), "", "+919876543210", null, null, false));
        var flow = new SignInFlow(Fakes.User(api), nav);

        await flow.CompleteAsync(isNewUser: false, fromSplash: true);

        await nav.Received(1).GoToAsync($"{Routes.SignIn}/{Routes.Name}");
    }
}
