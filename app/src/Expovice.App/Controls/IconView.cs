using System.Collections.Concurrent;
using Expovice.App.Core.Ui;
using Microsoft.Maui.Graphics;

namespace Expovice.App.Controls;

/// <summary>
/// Draws a Lucide icon (24×24 viewBox) at stroke 2.75 with round caps/joins, as the design system asks.
/// Rendering happens on the UI thread; the parsed-path cache is a ConcurrentDictionary so it is thread-safe.
/// </summary>
public sealed class IconView : GraphicsView
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(IconView), null, propertyChanged: Redraw);

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(IconView), Colors.Black, propertyChanged: Redraw);

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(IconView), 22d, propertyChanged: OnSizeChanged);

    public static readonly BindableProperty FilledProperty =
        BindableProperty.Create(nameof(Filled), typeof(bool), typeof(IconView), false, propertyChanged: Redraw);

    private static readonly ConcurrentDictionary<string, PathF[]> Cache = new();

    public IconView()
    {
        Drawable = new IconDrawable(this);
        BackgroundColor = Colors.Transparent;
        InputTransparent = true; // taps go to the parent button/border
        WidthRequest = HeightRequest = Size;
        HorizontalOptions = VerticalOptions = LayoutOptions.Center;
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    private static void Redraw(BindableObject b, object? oldValue, object? newValue) => ((IconView)b).Invalidate();

    private static void OnSizeChanged(BindableObject b, object? oldValue, object? newValue)
    {
        var view = (IconView)b;
        view.WidthRequest = view.HeightRequest = (double)newValue!;
        view.Invalidate();
    }

    private static PathF[] PathsFor(string key) =>
        Cache.GetOrAdd(key, k => [.. LucideIcons.Get(k).Select(PathBuilder.Build)]);

    private sealed class IconDrawable(IconView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (owner.Icon is not { } key)
            {
                return;
            }

            var scale = Math.Min(dirtyRect.Width, dirtyRect.Height) / 24f;
            canvas.SaveState();
            canvas.Translate((dirtyRect.Width - (24 * scale)) / 2, (dirtyRect.Height - (24 * scale)) / 2);
            canvas.Scale(scale, scale);
            canvas.StrokeColor = owner.Color;
            canvas.FillColor = owner.Color;
            canvas.StrokeSize = 2.75f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            foreach (var path in PathsFor(key))
            {
                if (owner.Filled)
                {
                    canvas.FillPath(path);
                }

                canvas.DrawPath(path);
            }

            canvas.RestoreState();
        }
    }
}
