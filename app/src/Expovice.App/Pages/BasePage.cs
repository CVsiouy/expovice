#if ANDROID || IOS
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Core;
#endif
using Expovice.App.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace Expovice.App.Pages;

/// <summary>
/// Every screen: hides Shell chrome, paints the warm page ground, forwards navigation parameters and lifecycle to its
/// view model. One place, so pages stay pure XAML.
/// </summary>
public abstract class BasePage : ContentPage, IQueryAttributable
{
    protected BasePage(ViewModelBase viewModel, bool accentBackground = false)
    {
        ViewModel = viewModel;
        BindingContext = viewModel;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        var ground = (Color)Application.Current!.Resources[accentBackground ? "Accent" : "Bg"];
        BackgroundColor = ground;
#if ANDROID || IOS
        // Status bar matches the page ground (only supported on the phone platforms).
        Behaviors.Add(new StatusBarBehavior
        {
            StatusBarColor = ground,
            StatusBarStyle = accentBackground ? StatusBarStyle.LightContent : StatusBarStyle.DarkContent,
        });
#endif
    }

    protected ViewModelBase ViewModel { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query) => ViewModel.ApplyParameters(query);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await ViewModel.OnAppearingAsync();
        }
        catch (Exception ex)
        {
            // async void must never throw: view models already handle API/network errors, this catches the unexpected.
            Handler?.MauiContext?.Services.GetService<ILogger<BasePage>>()?.LogError(ex, "OnAppearing failed for {Page}", GetType().Name);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ViewModel.OnDisappearing();
    }
}
