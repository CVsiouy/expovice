using Expovice.App.Core.ViewModels;

namespace Expovice.App.Pages;

public partial class OtpPage : BasePage
{
    public OtpPage(OtpViewModel viewModel)
        : base(viewModel)
    {
        InitializeComponent();
    }

    /// <summary>Open the keyboard straight away, as the hidden entry sits under the digit boxes.</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Dispatcher.Dispatch(() => CodeEntry.Focus());
    }
}
