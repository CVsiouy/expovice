using Expovice.Contracts;

namespace Expovice.App.Core.Ui;

/// <summary>
/// Names of the color resources in Resources/Styles/Colors.xaml. View models expose these keys (not colors) so
/// they stay MAUI-free; the app's ColorKeyConverter turns a key into the themed Color.
/// "Sage" is the design system's accent-2 ramp.
/// </summary>
public static class ColorKeys
{
    public const string Bg = "Bg";
    public const string Surface = "Surface";
    public const string Text = "Text";
    public const string Accent = "Accent";
    public const string Accent100 = "Accent100";
    public const string Accent200 = "Accent200";
    public const string Accent300 = "Accent300";
    public const string Accent400 = "Accent400";
    public const string Accent500 = "Accent500";
    public const string Accent700 = "Accent700";
    public const string Accent800 = "Accent800";
    public const string Sage100 = "Sage100";
    public const string Sage200 = "Sage200";
    public const string Sage300 = "Sage300";
    public const string Sage600 = "Sage600";
    public const string Sage800 = "Sage800";
    public const string Neutral100 = "Neutral100";
    public const string Neutral200 = "Neutral200";
    public const string Neutral300 = "Neutral300";
    public const string Neutral500 = "Neutral500";
    public const string Neutral700 = "Neutral700";
    public const string Neutral800 = "Neutral800";
}

/// <summary>Tag colors per request status — the prototype's STATUS map, shared by My requests and My work.</summary>
public static class StatusStyle
{
    public static (string Background, string Foreground) For(RequestStatus status) => status switch
    {
        RequestStatus.Pending => (ColorKeys.Accent100, ColorKeys.Accent800),
        RequestStatus.Accepted => (ColorKeys.Sage100, ColorKeys.Sage800),
        RequestStatus.Completed => (ColorKeys.Neutral300, ColorKeys.Neutral800),
        _ => (ColorKeys.Neutral200, ColorKeys.Neutral700),
    };
}
