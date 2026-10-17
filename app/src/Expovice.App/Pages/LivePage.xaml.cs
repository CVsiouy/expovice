using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class LivePage : BasePage
{
    public LivePage(LiveViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Animations.PopInAsync(CheckBadge);
    }
}
