using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class OnboardingPage : BasePage
{
    public OnboardingPage(OnboardingViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
