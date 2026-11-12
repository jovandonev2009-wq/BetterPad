using System.Windows;
using System.Windows.Media;

namespace BetterPad.Theme;

/// <summary>
/// One color set for the window chrome and the code editor. Every entry is exposed
/// as an application resource so XAML can follow theme changes via DynamicResource.
/// </summary>
public sealed class ThemePalette
{
    private readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.Ordinal);

    public ThemePalette(string id, string name, IReadOnlyDictionary<string, string> colors,
        bool overrideSyntaxColors = false)
    {
        Id = id;
        Name = name;
        OverrideSyntaxColors = overrideSyntaxColors;
        IsDark = ComputeIsDark(colors);
        foreach (KeyValuePair<string, string> pair in colors)
        {
            _brushes[pair.Key] = CreateBrush(pair.Value);
        }
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// Dark themes replace the colors from the syntax rule files so tokens stay
    /// readable; the light themes keep whatever palette the rule files define.
    /// </summary>
    public bool OverrideSyntaxColors { get; }

    /// <summary>True when the window background is dark; drives the native title bar.</summary>
    public bool IsDark { get; }

    public IEnumerable<string> Keys => _brushes.Keys;

    public Brush GetBrush(string key) =>
        _brushes.TryGetValue(key, out SolidColorBrush? brush) ? brush : Brushes.Transparent;

    private static bool ComputeIsDark(IReadOnlyDictionary<string, string> colors)
    {
        if (!colors.TryGetValue("BgBrush", out string? hex))
        {
            return false;
        }

        try
        {
            Color color = (Color)ColorConverter.ConvertFromString(hex);
            return (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B) < 128;
        }
        catch
        {
            return false;
        }
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch
        {
            var brush = new SolidColorBrush(Colors.Transparent);
            brush.Freeze();
            return brush;
        }
    }
}
