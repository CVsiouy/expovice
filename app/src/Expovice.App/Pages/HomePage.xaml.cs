using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class HomePage : BasePage
{
    public HomePage(HomeViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
