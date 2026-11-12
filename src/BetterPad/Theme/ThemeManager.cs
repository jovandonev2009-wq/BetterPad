using System.Windows;

namespace BetterPad.Theme;

public static class ThemeManager
{
    public static ThemePalette Current { get; private set; } = ThemeCatalog.Light;

    public static void Apply(string? id)
    {
        ThemePalette palette = ThemeCatalog.Find(id);
        Current = palette;

        if (Application.Current is { } app)
        {
            foreach (string key in palette.Keys)
            {
                app.Resources[key] = palette.GetBrush(key);
            }
        }
    }
}
