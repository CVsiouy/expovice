using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class SplashPage : BasePage
{
    public SplashPage(SplashViewModel viewModel)
        : base(viewModel, accentBackground: true)
    {
        InitializeComponent();
    }
}
