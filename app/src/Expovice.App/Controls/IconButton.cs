using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Expovice.App.Controls;

/// <summary>The design's `.btn-secondary.btn-icon`: a 44px circle with a divider stroke and a centred icon.</summary>
public sealed class IconButton : Border
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(IconButton), null, propertyChanged: (b, _, n) => ((IconButton)b).icon.Icon = (string?)n);

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(IconButton), 22d, propertyChanged: (b, _, n) => ((IconButton)b).icon.Size = (double)n!);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(IconButton), null, propertyChanged: (b, _, n) => ((IconButton)b).tap.Command = (ICommand?)n);

    private readonly IconView icon = new();
    private readonly TapGestureRecognizer tap = new();

    public IconButton()
    {
        WidthRequest = HeightRequest = 44;
        StrokeShape = new Ellipse();
        StrokeThickness = 1;
        Stroke = (Brush)Application.Current!.Resources["DividerBrush"];
        Background = Brush.Transparent;
        icon.Color = (Color)Application.Current.Resources["Text"];
        Content = icon;
        GestureRecognizers.Add(tap);
        SemanticProperties.SetHint(this, "Button");
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
