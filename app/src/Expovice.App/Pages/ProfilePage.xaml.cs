using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class ProfilePage : BasePage
{
    public ProfilePage(ProfileViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
