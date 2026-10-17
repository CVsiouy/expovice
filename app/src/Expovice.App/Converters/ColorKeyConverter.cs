using System.Globalization;

namespace Expovice.App.Converters;

/// <summary>
/// Turns a color resource key from a view model (see Core's ColorKeys) into the Color — or a SolidColorBrush when the
/// target property is a Brush (Border.Background, Stroke). Lets view models stay MAUI-free.
/// </summary>
public sealed class ColorKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? parameter as string;
        var color = key is not null && Application.Current?.Resources.TryGetValue(key, out var found) == true && found is Color c
            ? c
            : Colors.Transparent;
        return targetType.IsAssignableFrom(typeof(Color)) ? color : new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
