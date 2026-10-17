using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class ProviderDetailPage : BasePage
{
    public ProviderDetailPage(ProviderDetailViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
