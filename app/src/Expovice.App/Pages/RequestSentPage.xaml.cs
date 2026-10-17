using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class RequestSentPage : BasePage
{
    public RequestSentPage(RequestSentViewModel viewModel)
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
