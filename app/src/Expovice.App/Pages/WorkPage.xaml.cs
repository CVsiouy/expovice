using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class WorkPage : BasePage
{
    public WorkPage(WorkViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }
}
