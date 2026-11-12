using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Shapes;
using BetterPad.Controls;
using BetterPad.Syntax;
using Path = System.IO.Path;

namespace BetterPad.Documents;

public sealed class DocumentTab
{
    public TabItem Tab { get; set; } = new();

    public TextBlock TitleText { get; set; } = new();

    public Ellipse ModifiedDot { get; set; } = new();

    public CodeEditor? Editor { get; set; }

    public RichTextBox? RichEditor { get; set; }

    public bool IsRich => RichEditor != null;

    public string? FilePath { get; set; }

    public Encoding Encoding { get; set; } = new UTF8Encoding(false);

    public string Eol { get; set; } = "\r\n";

    public string UntitledName { get; set; } = "Untitled";

    public bool IsModified { get; set; }

    public bool SuppressModifiedTracking { get; set; }

    public SyntaxDefinition? Syntax { get; set; }

    public string DisplayName => FilePath is null ? UntitledName : Path.GetFileName(FilePath);

    public string PlainText
    {
        get
        {
            if (IsRich && RichEditor != null)
            {
                return new TextRange(RichEditor.Document.ContentStart, RichEditor.Document.ContentEnd).Text;
            }

            return Editor?.Text ?? string.Empty;
        }
    }
}

public sealed record LoadedText(string Text, Encoding Encoding, string Eol);

public static class TextFileService
{
    public static LoadedText Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding;
        int offset = 0;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            offset = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
            offset = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
            offset = 2;
        }
        else
        {
            encoding = new UTF8Encoding(false);
        }

        string text = encoding.GetString(bytes, offset, bytes.Length - offset);
        string eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
            : text.Contains('\n') ? "\n" : "\r\n";

        return new LoadedText(text, encoding, eol);
    }

    public static void Save(string path, string text, Encoding encoding, string eol)
    {
        string converted = eol == "\n" ? text.Replace("\r\n", "\n") : text;
        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes(converted);

        using var stream = File.Create(path);
        if (preamble.Length > 0)
        {
            stream.Write(preamble, 0, preamble.Length);
        }

        stream.Write(body, 0, body.Length);
    }

    public static string EncodingLabel(Encoding encoding)
    {
        if (encoding is UTF8Encoding)
        {
            return encoding.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8";
        }

        if (encoding is UnicodeEncoding unicode)
        {
            return unicode.CodePage == 1201 ? "UTF-16 BE" : "UTF-16 LE";
        }

        return encoding.WebName.ToUpperInvariant();
    }
}
