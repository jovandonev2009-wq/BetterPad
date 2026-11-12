using System.IO;

namespace BetterPad;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterPad");

    public static string SyntaxDirectory => Path.Combine(Root, "syntax");

    public static string PluginsDirectory => Path.Combine(Root, "plugins");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SyntaxDirectory);
        Directory.CreateDirectory(PluginsDirectory);
    }

    /// <summary>
    /// Copies the bundled rule files into the user's local syntax folder the first time
    /// the app runs. Existing files are never overwritten, so edits are preserved.
    /// </summary>
    public static void SeedSyntaxRules()
    {
        EnsureFolders();
        string bundled = Path.Combine(AppContext.BaseDirectory, "syntax");
        if (!Directory.Exists(bundled))
        {
            return;
        }

        foreach (string source in Directory.EnumerateFiles(bundled, "*.json"))
        {
            string destination = Path.Combine(SyntaxDirectory, Path.GetFileName(source));
            if (!File.Exists(destination))
            {
                try
                {
                    File.Copy(source, destination);
                }
                catch
                {
                    // A broken seed file should not stop the app from starting.
                }
            }
        }
    }
}

public sealed class AppSettings
{
    public double FontSize { get; set; } = 13.5;
    public bool ShowLineNumbers { get; set; } = true;
    public double WindowWidth { get; set; } = 1120;
    public double WindowHeight { get; set; } = 720;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }
    public int TabSize { get; set; } = 4;
    public string? LastOpenFolder { get; set; }
    public string? LastSearchFolder { get; set; }
    public bool ShowOutputOnRun { get; set; } = true;
    public string Theme { get; set; } = "light";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                string json = File.ReadAllText(AppPaths.SettingsFile);
                AppSettings? settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    settings.FontSize = Math.Clamp(settings.FontSize, 8, 32);
                    settings.TabSize = Math.Clamp(settings.TabSize, 1, 12);
                    return settings;
                }
            }
        }
        catch
        {
            // Fall back to defaults when the settings file cannot be read.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureFolders();
            string json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch
        {
            // Settings are a convenience; never fail shutdown over them.
        }
    }
}
