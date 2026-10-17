using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class RequestPage : BasePage
{
    public RequestPage(RequestViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
