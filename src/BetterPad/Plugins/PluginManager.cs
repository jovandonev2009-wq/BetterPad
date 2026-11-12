using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using BetterPad.PluginSdk;

namespace BetterPad.Plugins;

public sealed class PluginCommand
{
    public PluginCommand(string title, Action action)
    {
        Title = title;
        Action = action;
    }

    public string Title { get; }

    public Action Action { get; }
}

/// <summary>
/// Loads plugin DLLs from the local plugins folder into collectible load contexts.
/// No package feed or network access is involved.
/// </summary>
public sealed class PluginManager : IPluginHost, IDisposable
{
    private readonly Func<string?> _getActivePath;
    private readonly Func<string> _getActiveText;
    private readonly Action<string> _setActiveText;
    private readonly Action<string> _insertText;
    private readonly Action<string, string> _showMessage;
    private readonly Action<string> _setStatus;
    private readonly List<(PluginLoadContext Context, IBetterPadPlugin Plugin)> _loaded = new();

    public PluginManager(
        Func<string?> getActivePath,
        Func<string> getActiveText,
        Action<string> setActiveText,
        Action<string> insertText,
        Action<string, string> showMessage,
        Action<string> setStatus)
    {
        _getActivePath = getActivePath;
        _getActiveText = getActiveText;
        _setActiveText = setActiveText;
        _insertText = insertText;
        _showMessage = showMessage;
        _setStatus = setStatus;
    }

    public List<PluginCommand> Commands { get; } = new();

    public List<string> Errors { get; } = new();

    public int LoadedCount => _loaded.Count;

    public string? ActiveFilePath => _getActivePath();

    public string GetActiveText() => _getActiveText();

    public void SetActiveText(string text) => _setActiveText(text);

    public void InsertText(string text) => _insertText(text);

    public void ShowMessage(string title, string message) => _showMessage(title, message);

    public void AddCommand(string title, Action action) => Commands.Add(new PluginCommand(title, action));

    public void SetStatus(string text) => _setStatus(text);

    /// <summary>
    /// Installs the bundled sample plugin on the very first run so the plugin
    /// menu has something real in it.
    /// </summary>
    public void SeedSamplePlugin()
    {
        try
        {
            AppPaths.EnsureFolders();
            if (Directory.EnumerateFiles(AppPaths.PluginsDirectory, "*.dll").Any())
            {
                return;
            }

            string sampleDirectory = Path.Combine(AppContext.BaseDirectory, "sample-plugins");
            if (!Directory.Exists(sampleDirectory))
            {
                return;
            }

            foreach (string dll in Directory.EnumerateFiles(sampleDirectory, "*.dll"))
            {
                string destination = Path.Combine(AppPaths.PluginsDirectory, Path.GetFileName(dll));
                File.Copy(dll, destination, overwrite: false);
            }
        }
        catch
        {
            // A missing sample is not a problem.
        }
    }

    public void LoadAll()
    {
        UnloadAll();
        Errors.Clear();
        AppPaths.EnsureFolders();

        foreach (string dll in Directory.EnumerateFiles(AppPaths.PluginsDirectory, "*.dll")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            PluginLoadContext? context = null;
            try
            {
                context = new PluginLoadContext(dll);
                Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));
                var pluginTypes = assembly.GetTypes()
                    .Where(type => typeof(IBetterPadPlugin).IsAssignableFrom(type)
                                   && !type.IsAbstract
                                   && type.GetConstructor(Type.EmptyTypes) != null)
                    .ToList();

                if (pluginTypes.Count == 0)
                {
                    context.Unload();
                    Errors.Add($"{Path.GetFileName(dll)}: no IBetterPadPlugin class found");
                    continue;
                }

                foreach (Type type in pluginTypes)
                {
                    var plugin = (IBetterPadPlugin)Activator.CreateInstance(type)!;
                    plugin.Initialize(this);
                    _loaded.Add((context, plugin));
                }
            }
            catch (Exception ex)
            {
                context?.Unload();
                Errors.Add($"{Path.GetFileName(dll)}: {ex.Message}");
            }
        }
    }

    public string InstallPlugin(string sourcePath)
    {
        AppPaths.EnsureFolders();
        string destination = Path.Combine(AppPaths.PluginsDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private void UnloadAll()
    {
        Commands.Clear();
        foreach (var entry in _loaded)
        {
            try
            {
                if (entry.Plugin is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch
            {
                // Plugin cleanup must not block reloading.
            }

            try
            {
                entry.Context.Unload();
            }
            catch
            {
                // Best-effort unload; a plugin may pin its own assembly.
            }
        }

        _loaded.Clear();
    }

    public void Dispose()
    {
        UnloadAll();
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base($"BetterPad plugin: {Path.GetFileName(pluginPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Share the SDK types with the host so plugins implement the same interface.
            if (string.Equals(assemblyName.Name, "BetterPad.PluginSdk", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
