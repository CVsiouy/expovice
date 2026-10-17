using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class PhoneSignInPage : BasePage
{
    public PhoneSignInPage(PhoneSignInViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
