using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BetterPad.Theme;

namespace BetterPad.Controls;

public sealed class LineNumberGutter : FrameworkElement
{
    private static Brush NumberBrush => ThemeManager.Current.GetBrush("GutterNumberBrush");
    private static Brush CurrentBrush => ThemeManager.Current.GetBrush("GutterNumberActiveBrush");

    public CodeEditor? Editor { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        int digits = Math.Max(2, (Editor?.LineCount ?? 1).ToString(CultureInfo.InvariantCulture).Length);
        double fontSize = FontSize;
        var formatted = new FormattedText(
            new string('0', digits),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            fontSize,
            NumberBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return new Size(Math.Ceiling(formatted.Width) + 26, 0);
    }

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

        int count = editor.LineCount;
        int first = Math.Clamp(layout.FirstLine, 0, Math.Max(0, count - 1));
        int last = Math.Min(count - 1, first + (int)Math.Ceiling(ActualHeight / lineHeight) + 1);
        int caretLine = editor.CaretLineIndex;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = editor.EditorTypeface;
        double fontSize = FontSize;

        for (int line = first; line <= last; line++)
        {
            double y = layout.Top + ((line - first) * lineHeight);
            if (y + lineHeight < 0 || y > ActualHeight)
            {
                continue;
            }

            var formatted = new FormattedText(
                (line + 1).ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                line == caretLine ? CurrentBrush : NumberBrush,
                pixelsPerDip)
            {
                TextAlignment = TextAlignment.Right,
                MaxTextWidth = Math.Max(0, ActualWidth - 16)
            };

            dc.DrawText(formatted, new Point(8, y + ((lineHeight - formatted.Height) / 2)));
        }
    }

    private double FontSize => Editor == null ? 11.5 : Math.Clamp(Editor.EditorFontSize - 2, 9, 13);

    private Typeface Typeface => Editor?.EditorTypeface ?? new Typeface("Cascadia Mono");
}
