using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BetterPad.Syntax;
using BetterPad.Theme;

namespace BetterPad.Controls;

public readonly record struct LineInfo(int Start, int Length);

public readonly record struct EditorLayout(int FirstLine, double Top, double Left, double LineHeight, int LineCount);

public partial class CodeEditor : UserControl, IDisposable
{
    private readonly List<LineInfo> _lines = new();
    private readonly Dictionary<TokenKind, Brush> _brushes = new();
    private readonly DispatcherTimer _tokenDebounce;
    private List<SyntaxToken> _tokens = new();
    private SyntaxDefinition _syntax = SyntaxDefinition.Plain();
    private ScrollViewer? _scroller;
    private int _tokenVersion;
    private int _textLength;
    private int _tabSize = 4;
    private double _lineHeight;
    private bool _viewportRelativeRects = true;
    private bool _suppressExternalTextChanged;
    private Brush _defaultBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));

    public CodeEditor()
    {
        InitializeComponent();
        Renderer.Editor = this;
        Gutter.Editor = this;

        _tokenDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _tokenDebounce.Tick += (_, _) =>
        {
            _tokenDebounce.Stop();
            BuildTokensAsync();
        };

        Box.TextChanged += Box_TextChanged;
        Box.SelectionChanged += Box_SelectionChanged;
        Box.SizeChanged += (_, _) => InvalidateSurface();
        Box.PreviewKeyDown += Box_PreviewKeyDown;
        Box.PreviewMouseWheel += Box_PreviewMouseWheel;
        Loaded += (_, _) =>
        {
            EnsureScroller();
            DetectRectBehavior();
            RebuildBrushes();
            InvalidateSurface();
        };

        RebuildBrushes();
        RebuildLines();
    }

    public event EventHandler? TextChanged;

    public event EventHandler? CaretChanged;

    public event EventHandler? ZoomChanged;

    public TextBox TextArea => Box;

    public IReadOnlyList<LineInfo> Lines => _lines;

    public IReadOnlyList<SyntaxToken> Tokens => _tokens;

    internal SyntaxDefinition CurrentSyntax => _syntax;

    internal double EditorFontSize => Box.FontSize;

    internal FontFamily EditorFontFamily => Box.FontFamily;

    internal Typeface EditorTypeface => new(Box.FontFamily, Box.FontStyle, Box.FontWeight, Box.FontStretch);

    public int LineCount => _lines.Count;

    public new double FontSize
    {
        get => Box.FontSize;
        set => SetFontSize(value);
    }

    public bool ShowLineNumbers
    {
        get => GutterHost.Visibility == Visibility.Visible;
        set => GutterHost.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public string Text
    {
        get => Box.Text;
        set
        {
            _suppressExternalTextChanged = true;
            try
            {
                Box.Text = NormalizeNewlines(value);
                Box.CaretIndex = 0;
                RebuildLines();
                InvalidateSurface();
                ResetScroll();
            }
            finally
            {
                _suppressExternalTextChanged = false;
            }
        }
    }

    public int CaretLineIndex
    {
        get
        {
            if (_lines.Count == 0)
            {
                return 0;
            }

            return Math.Clamp(GetLineIndex(Box.CaretIndex), 0, _lines.Count - 1);
        }
    }

    public int CaretColumn
    {
        get
        {
            int line = CaretLineIndex;
            if (_lines.Count == 0)
            {
                return 1;
            }

            return Math.Max(1, Box.CaretIndex - _lines[line].Start + 1);
        }
    }

    public int SelectionStart => Box.SelectionStart;

    public int SelectionLength => Box.SelectionLength;

    public void SetSyntax(SyntaxDefinition definition)
    {
        _syntax = definition;
        RebuildBrushes();
        RebuildTokens();
    }

    public void SetTabSize(int size)
    {
        _tabSize = Math.Clamp(size, 1, 12);
    }

    public void SetFontSize(double size)
    {
        size = Math.Clamp(size, 8, 32);
        if (Math.Abs(Box.FontSize - size) < 0.01)
        {
            return;
        }

        Box.FontSize = size;
        _lineHeight = 0;
        Gutter.InvalidateMeasure();
        InvalidateSurface();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetLineText(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _lines.Count)
        {
            return string.Empty;
        }

        LineInfo info = _lines[lineIndex];
        return Box.Text.Substring(info.Start, info.Length);
    }

    public int GetLineIndex(int characterIndex)
    {
        if (_lines.Count == 0)
        {
            return 0;
        }

        int low = 0;
        int high = _lines.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (_lines[mid].Start <= characterIndex)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return Math.Clamp(high, 0, _lines.Count - 1);
    }

    public void SelectRange(int start, int length, bool focus = true)
    {
        int textLength = Box.Text.Length;
        start = Math.Clamp(start, 0, textLength);
        length = Math.Clamp(length, 0, textLength - start);
        if (focus)
        {
            Box.Focus();
        }

        Box.Select(start, length);
        ScrollLineIntoView(GetLineIndex(start));
    }

    public void GoToLine(int lineNumber, bool focus = true)
    {
        if (_lines.Count == 0)
        {
            return;
        }

        int index = Math.Clamp(lineNumber - 1, 0, _lines.Count - 1);
        if (focus)
        {
            Box.Focus();
        }

        Box.Select(_lines[index].Start, 0);
        ScrollLineIntoView(index);
    }

    public void InsertText(string text)
    {
        Box.Focus();
        Box.SelectedText = text;
    }

    public void FocusEditor()
    {
        Box.Focus();
    }

    public void ZoomIn() => SetFontSize(Box.FontSize + 1);

    public void ZoomOut() => SetFontSize(Box.FontSize - 1);

    public void ResetZoom(double size) => SetFontSize(size);

    public void RefreshTheme()
    {
        RebuildBrushes();
        Gutter.InvalidateVisual();
        Renderer.InvalidateVisual();
    }

    public void Dispose()
    {
        _tokenDebounce.Stop();
        _tokenVersion++;
    }

    internal Brush BrushFor(TokenKind kind)
    {
        return _brushes.TryGetValue(kind, out Brush? brush) ? brush : _defaultBrush;
    }

    internal Brush DefaultBrush => _defaultBrush;

    internal EditorLayout ComputeLayout()
    {
        EnsureScroller();
        double scrollTop = _scroller?.VerticalOffset ?? 0;
        double scrollLeft = _scroller?.HorizontalOffset ?? 0;
        double lineHeight = _lineHeight > 0 ? _lineHeight : MeasureLineHeight();
        int count = Math.Max(1, _lines.Count);
        int estimate = Math.Clamp((int)Math.Floor(scrollTop / lineHeight), 0, count - 1);

        int first = estimate;
        double top = 0;
        double left = 0;
        bool found = false;

        for (int line = Math.Max(0, estimate - 2); line <= Math.Min(count - 1, estimate + 8); line++)
        {
            Rect rect = GetRectForLine(line);
            if (rect.IsEmpty || rect.Height <= 0)
            {
                continue;
            }

            first = line;
            top = _viewportRelativeRects ? rect.Top : rect.Top - scrollTop;
            left = _viewportRelativeRects ? rect.Left : rect.Left - scrollLeft;
            found = true;

            Rect next = GetRectForLine(line + 1);
            if (!next.IsEmpty && next.Height > 0)
            {
                double difference = next.Top - rect.Top;
                if (difference > 2 && difference < 200)
                {
                    _lineHeight = difference;
                    lineHeight = difference;
                }
            }

            break;
        }

        if (!found)
        {
            first = estimate;
            top = Box.Padding.Top + (first * lineHeight) - scrollTop;
            left = Box.Padding.Left - scrollLeft;
        }

        if (_lineHeight <= 0)
        {
            _lineHeight = lineHeight;
        }

        return new EditorLayout(first, top, left, _lineHeight, count);
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        RebuildLines();
        RebuildTokens();
        Gutter.InvalidateMeasure();
        InvalidateSurface();

        if (!_suppressExternalTextChanged)
        {
            TextChanged?.Invoke(this, EventArgs.Empty);
            CaretChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Box_SelectionChanged(object sender, RoutedEventArgs e)
    {
        Renderer.InvalidateVisual();
        Gutter.InvalidateVisual();
        CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            HandleTab(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            HandleEnter();
            e.Handled = true;
        }
    }

    private void Box_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        if (e.Delta > 0)
        {
            ZoomIn();
        }
        else
        {
            ZoomOut();
        }

        e.Handled = true;
    }

    private void HandleTab(bool outdent)
    {
        Box.BeginChange();
        try
        {
            if (Box.SelectionLength > 0)
            {
                IndentSelectedLines(outdent);
                return;
            }

            string text = Box.Text;
            int caret = Box.CaretIndex;
            int line = GetLineIndex(caret);
            int lineStart = _lines[line].Start;

            if (!outdent)
            {
                int column = caret - lineStart;
                int spaces = _tabSize - (column % _tabSize);
                string padding = new string(' ', spaces);
                int insertAt = Box.SelectionStart;
                Box.SelectedText = padding;
                Box.CaretIndex = insertAt + padding.Length;
                return;
            }

            int remove = 0;
            while (remove < _tabSize && caret - remove - 1 >= lineStart && text[caret - remove - 1] == ' ')
            {
                remove++;
            }

            if (remove > 0)
            {
                Box.Select(caret - remove, remove);
                Box.SelectedText = string.Empty;
            }
        }
        finally
        {
            Box.EndChange();
        }
    }

    private void IndentSelectedLines(bool outdent)
    {
        int selectionStart = Box.SelectionStart;
        int selectionEnd = selectionStart + Box.SelectionLength;
        int from = GetLineIndex(selectionStart);
        int to = GetLineIndex(selectionEnd);
        if (selectionEnd <= _lines[to].Start && to > from)
        {
            to--;
        }

        for (int line = to; line >= from; line--)
        {
            int start = _lines[line].Start;
            if (outdent)
            {
                string lineText = GetLineText(line);
                int remove = 0;
                while (remove < _tabSize && remove < lineText.Length && lineText[remove] == ' ')
                {
                    remove++;
                }

                if (remove > 0)
                {
                    Box.Select(start, remove);
                    Box.SelectedText = string.Empty;
                }
            }
            else
            {
                Box.Select(start, 0);
                Box.SelectedText = new string(' ', _tabSize);
            }
        }

        int newStart = _lines[from].Start;
        int newEnd = _lines[to].Start + _lines[to].Length;
        Box.Select(newStart, Math.Max(0, newEnd - newStart));
    }

    private void HandleEnter()
    {
        int caret = Box.SelectionStart;
        int line = GetLineIndex(caret);
        int lineStart = _lines[line].Start;
        string text = Box.Text;

        string beforeCaret = text.Substring(lineStart, Math.Max(0, caret - lineStart));
        int indentLength = 0;
        while (indentLength < beforeCaret.Length && (beforeCaret[indentLength] == ' ' || beforeCaret[indentLength] == '\t'))
        {
            indentLength++;
        }

        string indent = beforeCaret.Substring(0, indentLength);
        string trimmed = beforeCaret.TrimEnd();
        if (trimmed.EndsWith('{') || trimmed.EndsWith(':') || trimmed.EndsWith('[') || trimmed.EndsWith('('))
        {
            indent += new string(' ', _tabSize);
        }

        string insertion = "\r\n" + indent;
        int insertAt = Box.SelectionStart;
        Box.SelectedText = insertion;
        Box.CaretIndex = insertAt + insertion.Length;
    }

    private void RebuildLines()
    {
        _lines.Clear();
        string text = Box.Text;
        _textLength = text.Length;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                int end = i;
                if (end > start && text[end - 1] == '\r')
                {
                    end--;
                }

                _lines.Add(new LineInfo(start, end - start));
                start = i + 1;
            }
            else if (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))
            {
                _lines.Add(new LineInfo(start, i - start));
                start = i + 1;
            }
        }

        _lines.Add(new LineInfo(start, text.Length - start));
    }

    private void RebuildTokens()
    {
        _tokenVersion++;
        int version = _tokenVersion;
        string text = Box.Text;

        if (_syntax.Name == "Plain Text" || text.Length == 0)
        {
            _tokens = new List<SyntaxToken>();
            InvalidateSurface();
            return;
        }

        if (text.Length <= 200_000)
        {
            _tokens = SyntaxTokenizer.Tokenize(text, _syntax);
            InvalidateSurface();
            return;
        }

        _tokens = new List<SyntaxToken>();
        InvalidateSurface();
        _tokenDebounce.Stop();
        _tokenDebounce.Start();
    }

    private void BuildTokensAsync()
    {
        _tokenVersion++;
        int version = _tokenVersion;
        string text = Box.Text;
        SyntaxDefinition syntax = _syntax;

        _ = Task.Run(() => SyntaxTokenizer.Tokenize(text, syntax)).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (version == _tokenVersion)
                {
                    _tokens = task.Result;
                    InvalidateSurface();
                }
            });
        });
    }

    private void RebuildBrushes()
    {
        _brushes.Clear();
        ThemePalette theme = ThemeManager.Current;
        if (theme.OverrideSyntaxColors)
        {
            _defaultBrush = (Brush)theme.GetBrush("EditorTextBrush");
            _brushes[TokenKind.Keyword] = theme.GetBrush("SyntaxKeywordBrush");
            _brushes[TokenKind.Type] = theme.GetBrush("SyntaxTypeBrush");
            _brushes[TokenKind.String] = theme.GetBrush("SyntaxStringBrush");
            _brushes[TokenKind.Comment] = theme.GetBrush("SyntaxCommentBrush");
            _brushes[TokenKind.Number] = theme.GetBrush("SyntaxNumberBrush");
            _brushes[TokenKind.Preprocessor] = theme.GetBrush("SyntaxPreprocessorBrush");
        }
        else
        {
            _defaultBrush = CreateBrush(_syntax.Colors.Default, "#1F2937");
            _brushes[TokenKind.Keyword] = CreateBrush(_syntax.Colors.Keyword, "#0B57D0");
            _brushes[TokenKind.Type] = CreateBrush(_syntax.Colors.Type, "#0F766E");
            _brushes[TokenKind.String] = CreateBrush(_syntax.Colors.String, "#B42318");
            _brushes[TokenKind.Comment] = CreateBrush(_syntax.Colors.Comment, "#15803D");
            _brushes[TokenKind.Number] = CreateBrush(_syntax.Colors.Number, "#7C3AED");
            _brushes[TokenKind.Preprocessor] = CreateBrush(_syntax.Colors.Preprocessor, "#6B7280");
        }

        Renderer.InvalidateVisual();
    }

    private static Brush CreateBrush(string? value, string fallback)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(value) ? fallback : value);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            brush.Freeze();
            return brush;
        }
    }

    private double MeasureLineHeight()
    {
        var probe = new TextBlock
        {
            Text = "Xg",
            FontFamily = Box.FontFamily,
            FontSize = Box.FontSize,
            FontStyle = Box.FontStyle,
            FontWeight = Box.FontWeight,
            FontStretch = Box.FontStretch
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Height > 1 ? probe.DesiredSize.Height : Box.FontSize * 1.45;
    }

    private Rect GetRectForLine(int line)
    {
        if (line < 0 || line >= _lines.Count)
        {
            return Rect.Empty;
        }

        int start = _lines[line].Start;
        if (start >= _textLength)
        {
            return Rect.Empty;
        }

        try
        {
            return Box.GetRectFromCharacterIndex(start, false);
        }
        catch
        {
            return Rect.Empty;
        }
    }

    private void EnsureScroller()
    {
        if (_scroller != null)
        {
            return;
        }

        _scroller = FindVisualChild<ScrollViewer>(Box);
        if (_scroller != null)
        {
            _scroller.ScrollChanged += (_, _) => InvalidateSurface();
        }
    }

    private void DetectRectBehavior()
    {
        if (_scroller == null || _scroller.ScrollableHeight <= 4 || Box.Text.Length == 0)
        {
            return;
        }

        Rect before = GetRectForLine(0);
        if (before.IsEmpty)
        {
            return;
        }

        double original = _scroller.VerticalOffset;
        _scroller.ScrollToVerticalOffset(original + 4);
        Box.UpdateLayout();
        Rect after = GetRectForLine(0);
        _scroller.ScrollToVerticalOffset(original);
        Box.UpdateLayout();

        if (!after.IsEmpty)
        {
            _viewportRelativeRects = Math.Abs(after.Top - before.Top) > 0.5;
        }
    }

    private void ScrollLineIntoView(int line)
    {
        EnsureScroller();
        if (_scroller == null)
        {
            return;
        }

        double lineHeight = _lineHeight > 0 ? _lineHeight : MeasureLineHeight();
        double top = line * lineHeight;
        if (top < _scroller.VerticalOffset || top + lineHeight > _scroller.VerticalOffset + _scroller.ViewportHeight)
        {
            _scroller.ScrollToVerticalOffset(Math.Max(0, top - (_scroller.ViewportHeight / 2)));
        }
    }

    private void ResetScroll()
    {
        EnsureScroller();
        _scroller?.ScrollToTop();
        _scroller?.ScrollToLeftEnd();
    }

    private void InvalidateSurface()
    {
        Renderer.InvalidateVisual();
        Gutter.InvalidateVisual();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }

            T? nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string NormalizeNewlines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
    }
}
