using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BetterPad.Syntax;
using BetterPad.Theme;

namespace BetterPad.Controls;

/// <summary>
/// Paints the visible text in syntax colors. The TextBox above it stays transparent
/// and owns the caret, selection, scrolling and native undo/redo.
/// </summary>
public sealed class SyntaxRenderer : FrameworkElement
{
    private static Brush CurrentLineBrush => ThemeManager.Current.GetBrush("EditorCurrentLineBrush");

    public CodeEditor? Editor { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        CodeEditor? editor = Editor;
        if (editor == null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        EditorLayout layout = editor.ComputeLayout();
        double lineHeight = layout.LineHeight;
        if (lineHeight <= 0)
        {
            return;
        }

        string text = editor.Text;
        var lines = editor.Lines;
        if (lines.Count == 0)
        {
            return;
        }

        int first = Math.Clamp(layout.FirstLine, 0, lines.Count - 1);
        int last = Math.Min(lines.Count - 1, first + (int)Math.Ceiling(ActualHeight / lineHeight) + 1);
        int caretLine = editor.CaretLineIndex;

        if (caretLine >= first && caretLine <= last)
        {
            double highlightY = layout.Top + ((caretLine - first) * lineHeight);
            dc.DrawRectangle(CurrentLineBrush, null, new Rect(0, highlightY, ActualWidth, lineHeight));
        }

        var tokens = editor.Tokens;
        int tokenCursor = 0;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Typeface typeface = editor.EditorTypeface;
        double fontSize = editor.EditorFontSize;
        double tabWidth = fontSize * 4;

        for (int line = first; line <= last; line++)
        {
            LineInfo info = lines[line];
            if (info.Length <= 0 || info.Start >= text.Length)
            {
                continue;
            }

            int length = Math.Min(info.Length, text.Length - info.Start);
            while (tokenCursor < tokens.Count && tokens[tokenCursor].End <= info.Start)
            {
                tokenCursor++;
            }

            double y = layout.Top + ((line - first) * lineHeight);
            string lineText = text.Substring(info.Start, length);

            // FormattedText does not expand tabs, but the TextBox beneath lays them
            // out on fixed 4-em stops. Paint tabbed lines in pieces so the glyphs
            // stay on top of the caret and selection rectangles.
            if (lineText.IndexOf('\t') < 0)
            {
                var formatted = new FormattedText(
                    lineText,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    editor.DefaultBrush,
                    pixelsPerDip);

                for (int t = tokenCursor; t < tokens.Count; t++)
                {
                    SyntaxToken token = tokens[t];
                    if (token.Start >= info.Start + length)
                    {
                        break;
                    }

                    int segmentStart = Math.Max(token.Start, info.Start) - info.Start;
                    int segmentEnd = Math.Min(token.End, info.Start + length) - info.Start;
                    if (segmentEnd > segmentStart)
                    {
                        formatted.SetForegroundBrush(editor.BrushFor(token.Kind), segmentStart, segmentEnd - segmentStart);
                    }
                }

                dc.DrawText(formatted, new Point(layout.Left, y));
                continue;
            }

            DrawTabbedLine(dc, editor, text, info, length, tokenCursor, tokens, layout.Left, y,
                typeface, fontSize, pixelsPerDip, tabWidth);
        }
    }

    private static void DrawTabbedLine(
        DrawingContext dc,
        CodeEditor editor,
        string text,
        LineInfo info,
        int length,
        int tokenCursor,
        IReadOnlyList<SyntaxToken> tokens,
        double lineLeft,
        double y,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip,
        double tabWidth)
    {
        int lineEnd = info.Start + length;
        int cursor = info.Start;
        double x = lineLeft;

        for (int t = tokenCursor; t < tokens.Count; t++)
        {
            SyntaxToken token = tokens[t];
            if (token.Start >= lineEnd)
            {
                break;
            }

            if (token.End <= cursor)
            {
                continue;
            }

            int tokenStart = Math.Max(token.Start, cursor);
            int tokenEnd = Math.Min(token.End, lineEnd);

            if (tokenStart > cursor)
            {
                x = DrawTextWithTabs(dc, text, cursor, tokenStart, x, y, lineLeft,
                    editor.DefaultBrush, typeface, fontSize, pixelsPerDip, tabWidth);
            }

            x = DrawTextWithTabs(dc, text, tokenStart, tokenEnd, x, y, lineLeft,
                editor.BrushFor(token.Kind), typeface, fontSize, pixelsPerDip, tabWidth);
            cursor = Math.Max(cursor, tokenEnd);

            if (cursor >= lineEnd)
            {
                return;
            }
        }

        if (cursor < lineEnd)
        {
            DrawTextWithTabs(dc, text, cursor, lineEnd, x, y, lineLeft,
                editor.DefaultBrush, typeface, fontSize, pixelsPerDip, tabWidth);
        }
    }

    private static double DrawTextWithTabs(
        DrawingContext dc,
        string text,
        int start,
        int end,
        double x,
        double y,
        double lineLeft,
        Brush brush,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip,
        double tabWidth)
    {
        int position = start;
        while (position < end)
        {
            int tab = text.IndexOf('\t', position, end - position);
            int chunkEnd = tab < 0 ? end : tab;

            if (chunkEnd > position)
            {
                var formatted = new FormattedText(
                    text.Substring(position, chunkEnd - position),
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    brush,
                    pixelsPerDip);

                dc.DrawText(formatted, new Point(x, y));
                x += formatted.WidthIncludingTrailingWhitespace;
            }

            if (tab < 0)
            {
                break;
            }

            double relative = x - lineLeft;
            x = lineLeft + ((Math.Floor(relative / tabWidth) + 1) * tabWidth);
            position = tab + 1;
        }

        return x;
    }
}
