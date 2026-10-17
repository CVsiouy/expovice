using System.Collections.Frozen;
using System.Globalization;

namespace Expovice.App.Core.Ui;

/// <summary>
/// Lucide icon geometry (24×24 viewBox) ported verbatim from the prototype's SV map. Entries prefixed "c:" (circle)
/// and "r:" (rounded rect) are converted to SVG path data once at start-up, so the renderer only handles paths.
/// Immutable after construction: thread-safe.
/// </summary>
public static class LucideIcons
{
    private static readonly Dictionary<string, string[]> Source = new()
    {
        ["zap"] = ["M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"],
        ["wrench"] = ["M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"],
        ["hammer"] = ["m15 12-8.373 8.373a1 1 0 1 1-3-3L12 9", "m18 15 4-4", "m21.5 11.5-1.914-1.914A2 2 0 0 1 19 8.172V7l-2.26-2.26a6 6 0 0 0-4.202-1.756L9 2.96l.92.82A6.18 6.18 0 0 1 12 8.4V10l2 2h1.172a2 2 0 0 1 1.414.586L18.5 14.5"],
        ["snow"] = ["m10 20-1.25-2.5L6 18", "M10 4 8.75 6.5 6 6", "m14 20 1.25-2.5L18 18", "m14 4 1.25 2.5L18 6", "m17 21-3-6h-4", "m17 3-3 6 1.5 3", "M2 12h6.5L10 9", "m20 10-1.5 2 1.5 2", "M22 12h-6.5L14 15", "m4 10 1.5 2L4 14", "m7 21 3-6-1.5-3", "m7 3 3 6h4"],
        ["fridge"] = ["M5 6a4 4 0 0 1 4-4h6a4 4 0 0 1 4 4v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6Z", "M5 10h14", "M15 7v6"],
        ["washer"] = ["M3 6h3", "M17 6h.01", "r:3,2,18,20,2", "c:12,13,5", "M12 18a2.5 2.5 0 0 0 0-5 2.5 2.5 0 0 1 0-5"],
        ["paw"] = ["c:11,4,2", "c:18,8,2", "c:20,16,2", "M9 10a5 5 0 0 1 5 5v3.5a3.5 3.5 0 0 1-6.84 1.045Q6.52 17.48 4.46 16.84A3.5 3.5 0 0 1 5.5 10Z"],
        ["roller"] = ["r:2,2,16,6,2", "M10 16v-2a2 2 0 0 1 2-2h8a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2", "r:8,16,4,6,1"],
        ["star"] = ["M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z"],
        ["pin"] = ["M20 10c0 4.993-5.539 10.193-7.399 11.799a1 1 0 0 1-1.202 0C9.539 20.193 4 14.993 4 10a8 8 0 0 1 16 0", "c:12,10,3"],
        ["search"] = ["c:11,11,8", "m21 21-4.3-4.3"],
        ["chevL"] = ["m15 18-6-6 6-6"],
        ["chevR"] = ["m9 18 6-6-6-6"],
        ["chevD"] = ["m6 9 6 6 6-6"],
        ["home"] = ["M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8", "M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"],
        ["list"] = ["r:8,2,8,4,1", "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2", "M12 11h4", "M12 16h4", "M8 11h.01", "M8 16h.01"],
        ["brief"] = ["M16 20V4a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16", "r:2,6,20,14,2"],
        ["user"] = ["M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2", "c:12,7,4"],
        ["bell"] = ["M10.268 21a2 2 0 0 0 3.464 0", "M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326"],
        ["check"] = ["M20 6 9 17l-5-5"],
        ["shield"] = ["M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z", "m9 12 2 2 4-4"],
        ["clock"] = ["c:12,12,10", "M12 6v6l4 2"],
        ["plus"] = ["M5 12h14", "M12 5v14"],
        ["minus"] = ["M5 12h14"],
        ["logout"] = ["m16 17 5-5-5-5", "M21 12H9", "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"],
        ["map"] = ["M14.106 5.553a2 2 0 0 0 1.788 0l3.659-1.83A1 1 0 0 1 21 4.619v12.764a1 1 0 0 1-.553.894l-4.553 2.277a2 2 0 0 1-1.788 0l-4.212-2.106a2 2 0 0 0-1.788 0l-3.659 1.83A1 1 0 0 1 3 19.381V6.618a1 1 0 0 1 .553-.894l4.553-2.277a2 2 0 0 1 1.788 0z", "M15 5.764v15", "M9 3.236v15"],
        ["help"] = ["c:12,12,10", "M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3", "M12 17h.01"],
        ["camera"] = ["M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z", "c:12,13,3"],
    };

    /// <summary>Icon key → SVG path data strings.</summary>
    public static FrozenDictionary<string, string[]> Paths { get; } =
        Source.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.Select(ToPath).ToArray());

    public static IReadOnlyList<string> Get(string? key) =>
        key is not null && Paths.TryGetValue(key, out var p) ? p : [];

    internal static string ToPath(string shape)
    {
        if (shape.StartsWith("c:", StringComparison.Ordinal))
        {
            var c = Nums(shape);
            var (cx, cy, r) = (c[0], c[1], c[2]);
            // Two half-circle arcs make a full circle.
            return Svg($"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0z");
        }

        if (shape.StartsWith("r:", StringComparison.Ordinal))
        {
            var n = Nums(shape);
            var (x, y, w, h, rx) = (n[0], n[1], n[2], n[3], n[4]);
            var (iw, ih) = (w - 2 * rx, h - 2 * rx);
            return Svg($"M{x + rx} {y}h{iw}a{rx} {rx} 0 0 1 {rx} {rx}v{ih}a{rx} {rx} 0 0 1 {-rx} {rx}h{-iw}a{rx} {rx} 0 0 1 {-rx} {-rx}v{-ih}a{rx} {rx} 0 0 1 {rx} {-rx}z");
        }

        return shape;
    }

    private static double[] Nums(string shape) =>
        shape[2..].Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

    private static string Svg(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
