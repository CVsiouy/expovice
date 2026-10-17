using Microsoft.Maui.Controls.Shapes;

namespace Expovice.App.Controls;

/// <summary>
/// A rounded/circular photo slot. Shows a neutral fill with placeholder text until a URL arrives.
/// The design's `.washed` CSS filter (desaturate, lower contrast, lift) has no native equivalent, so a 14% wash of the
/// page color is laid over the photo to push it back into the warm ground.
/// </summary>
public sealed class WashedImage : ContentView
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(WashedImage), null, propertyChanged: (b, _, _) => ((WashedImage)b).UpdateSource());

    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(CornerRadius), typeof(WashedImage), new CornerRadius(20), propertyChanged: (b, _, _) => ((WashedImage)b).UpdateShape());

    public static readonly BindableProperty IsCircleProperty =
        BindableProperty.Create(nameof(IsCircle), typeof(bool), typeof(WashedImage), false, propertyChanged: (b, _, _) => ((WashedImage)b).UpdateShape());

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(WashedImage), string.Empty, propertyChanged: (b, _, n) => ((WashedImage)b).placeholder.Text = (string?)n);

    private readonly Border frame;
    private readonly Image image = new() { Aspect = Aspect.AspectFill };
    private readonly Label placeholder;

    public WashedImage()
    {
        placeholder = new Label
        {
            FontSize = 11,
            TextColor = Resource("Neutral700"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(4),
        };
        var wash = new BoxView { Color = Resource("Bg"), Opacity = 0.14, InputTransparent = true };
        frame = new Border
        {
            StrokeThickness = 0,
            Background = new SolidColorBrush(Resource("Neutral200")),
            Content = new Grid { Children = { placeholder, image, wash } },
        };
        Content = frame;
        UpdateShape();
        UpdateSource();
    }

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsCircle
    {
        get => (bool)GetValue(IsCircleProperty);
        set => SetValue(IsCircleProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private static Color Resource(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Colors.Transparent;

    private void UpdateShape() =>
        frame.StrokeShape = IsCircle ? new Ellipse() : new RoundRectangle { CornerRadius = CornerRadius };

    private void UpdateSource()
    {
        image.Source = Source switch
        {
            // Local preview of a photo picked but not uploaded yet (see SetupViewModel's pending photos).
            { } s when s.StartsWith("data:", StringComparison.Ordinal) && s.IndexOf(',') is var comma and > 0 =>
                ImageSource.FromStream(() => new MemoryStream(Convert.FromBase64String(s[(comma + 1)..]))),
            { } s when Uri.TryCreate(s, UriKind.Absolute, out var uri) =>
                new UriImageSource { Uri = uri, CachingEnabled = true, CacheValidity = TimeSpan.FromDays(1) },
            _ => null,
        };
        image.IsVisible = image.Source is not null;
        placeholder.IsVisible = image.Source is null;
    }
}
