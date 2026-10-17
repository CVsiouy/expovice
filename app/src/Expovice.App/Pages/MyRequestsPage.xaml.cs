using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class MyRequestsPage : BasePage
{
    public MyRequestsPage(MyRequestsViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
