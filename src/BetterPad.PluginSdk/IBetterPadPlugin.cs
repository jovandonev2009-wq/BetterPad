namespace BetterPad.PluginSdk;

/// <summary>
/// Entry point for a BetterPad plugin. Implement this in a class library that
/// references BetterPad.PluginSdk, drop the built DLL into the plugins folder,
/// and BetterPad will load it from disk. Nothing leaves the machine.
/// </summary>
public interface IBetterPadPlugin
{
    string Name { get; }

    string Description { get; }

    void Initialize(IPluginHost host);
}

/// <summary>
/// The bridge between the editor and a plugin. Everything runs in-process and offline.
/// </summary>
public interface IPluginHost
{
    /// <summary>Full path of the active document, or null for an unsaved tab.</summary>
    string? ActiveFilePath { get; }

    /// <summary>Current text of the active editor (empty for rich-text tabs).</summary>
    string GetActiveText();

    /// <summary>Replace the active document text (code tabs only).</summary>
    void SetActiveText(string text);

    /// <summary>Insert text at the caret of the active editor.</summary>
    void InsertText(string text);

    /// <summary>Show a modal message box owned by the main window.</summary>
    void ShowMessage(string title, string message);

    /// <summary>Register an entry in the Plugins menu.</summary>
    void AddCommand(string title, Action action);

    /// <summary>Set the text in the status bar while a command runs.</summary>
    void SetStatus(string text);
}
