namespace Expovice.App.Pages;

/// <summary>Shared motion from the prototype (DRY).</summary>
public static class Animations
{
    /// <summary>The prototype's ex-pop keyframes: scale .6 → 1 and fade in over 400 ms, ease-out.</summary>
    public static Task PopInAsync(VisualElement view)
    {
        view.Scale = 0.6;
        view.Opacity = 0;
        return Task.WhenAll(
            view.ScaleToAsync(1, 400, Easing.CubicOut),
            view.FadeToAsync(1, 400, Easing.CubicOut));
    }
}
