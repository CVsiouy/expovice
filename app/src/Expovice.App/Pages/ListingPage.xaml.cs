using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class ListingPage : BasePage
{
    public ListingPage(ListingViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
