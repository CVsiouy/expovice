using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class SetupPage : BasePage
{
    public SetupPage(SetupViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
