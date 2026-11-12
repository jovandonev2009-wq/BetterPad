using System.Text.RegularExpressions;
using BetterPad.PluginSdk;

namespace WordCountPlugin;

public sealed class WordCountPlugin : IBetterPadPlugin
{
    public string Name => "Word Count";
    public string Description => "Counts words, lines and characters, and offers a couple of text transforms.";

    public void Initialize(IPluginHost host)
    {
        host.AddCommand("Word and line count", () =>
        {
            string text = host.GetActiveText();
            int words = Regex.Matches(text, @"\S+").Count;
            int lines = text.Length == 0 ? 0 : text.Replace("\r\n", "\n").Split('\n').Length;
            host.ShowMessage("Word Count",
                $"Words: {words}\nLines: {lines}\nCharacters: {text.Length}");
        });

        host.AddCommand("Trim trailing whitespace", () =>
        {
            string text = host.GetActiveText();
            string trimmed = Regex.Replace(text, @"[ \t]+(?=\r?$)", "", RegexOptions.Multiline);
            if (trimmed == text)
            {
                host.SetStatus("Nothing to trim.");
                return;
            }
            host.SetActiveText(trimmed);
            host.SetStatus("Trailing whitespace removed.");
        });
    }
}
