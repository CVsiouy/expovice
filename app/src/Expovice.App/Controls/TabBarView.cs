using System.ComponentModel;
using Expovice.App.Core.Abstractions;
using Expovice.App.Core.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Expovice.App.Controls;

/// <summary>
/// The custom bottom tab bar (Home / Requests / My work / Profile) with the accent-100 pill on the active tab and a
/// pending-request badge on "My work". Shell's own tab bar is hidden because it cannot draw the pill.
/// UI-thread only.
/// </summary>
public sealed class TabBarView : ContentView
{
    public static readonly BindableProperty SelectedProperty =
        BindableProperty.Create(nameof(Selected), typeof(string), typeof(TabBarView), Routes.Home, propertyChanged: (b, _, _) => ((TabBarView)b).Build());

    private static readonly (string Route, string Label, string Icon)[] Tabs =
    [
        (Routes.Home, "Home", "home"),
        (Routes.Requests, "Requests", "list"),
        (Routes.Work, "My work", "brief"),
        (Routes.Profile, "Profile", "user"),
    ];

    private readonly Grid grid = new() { ColumnDefinitions = new ColumnDefinitionCollection(Tabs.Select(_ => new ColumnDefinition(GridLength.Star)).ToArray()) };
    private readonly UserContext? user;
    private readonly INavigationService? navigation;
    private Label? badge;
    private Border? badgeBorder;

    public TabBarView()
    {
        var services = IPlatformApplication.Current?.Services;
        user = services?.GetService<UserContext>();
        navigation = services?.GetService<INavigationService>();

        BackgroundColor = Color("Bg");
        Padding = new Thickness(10, 8, 10, 8);
        Shadow = new Shadow { Brush = new SolidColorBrush(Color("Neutral900")), Offset = new Point(0, -6), Radius = 20, Opacity = 0.08f };
        Content = grid;
        Build();

        // Subscribe only while on screen so the singleton UserContext never keeps a page alive.
        Loaded += (_, _) => { if (user is not null) { user.PropertyChanged += OnUserChanged; UpdateBadge(); } };
        Unloaded += (_, _) => { if (user is not null) { user.PropertyChanged -= OnUserChanged; } };
    }

    /// <summary>The route of the active tab (a <see cref="Routes"/> constant).</summary>
    public string Selected
    {
        get => (string)GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    private static Color Color(string key) => (Color)Application.Current!.Resources[key];

    private void Build()
    {
        grid.Children.Clear();
        for (var i = 0; i < Tabs.Length; i++)
        {
            var (route, label, icon) = Tabs[i];
            var on = route == Selected;
            var fg = Color(on ? "Accent700" : "Neutral700");

            var pill = new Grid { WidthRequest = 56, HeightRequest = 32 };
            pill.Children.Add(new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Background = on ? new SolidColorBrush(Color("Accent100")) : Brush.Transparent,
            });
            pill.Children.Add(new IconView { Icon = icon, Color = fg, Size = 22 });

            if (route == Routes.Work)
            {
                badge = new Label { FontSize = 10, FontFamily = "FigtreeBold", TextColor = Color("Bg"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
                badgeBorder = new Border
                {
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 8 },
                    Background = new SolidColorBrush(Color("Accent")),
                    MinimumWidthRequest = 16,
                    HeightRequest = 16,
                    Padding = new Thickness(4, 0),
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Start,
                    Margin = new Thickness(0, 0, 8, 0),
                    Content = badge,
                };
                pill.Children.Add(badgeBorder);
                UpdateBadge();
            }

            var item = new VerticalStackLayout
            {
                Spacing = 3,
                Padding = new Thickness(0, 4),
                Children =
                {
                    pill,
                    new Label { Text = label, FontSize = 11.5, FontFamily = "FigtreeBold", TextColor = fg, HorizontalOptions = LayoutOptions.Center },
                },
            };
            SemanticProperties.SetDescription(item, label);
            item.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => { if (!on) { _ = navigation?.GoToAsync(route); } }),
            });
            grid.Add(item, i);
        }
    }

    private void OnUserChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UserContext.NewRequestCount) or nameof(UserContext.IsProvider) or nameof(UserContext.ProviderProfile))
        {
            UpdateBadge();
        }
    }

    private void UpdateBadge()
    {
        if (badge is null || badgeBorder is null)
        {
            return;
        }

        var count = user is { IsProvider: true } ? user.NewRequestCount : 0;
        badge.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        badgeBorder.IsVisible = count > 0;
    }
}
