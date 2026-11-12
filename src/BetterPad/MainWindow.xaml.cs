using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BetterPad.Controls;
using BetterPad.Dialogs;
using BetterPad.Documents;
using BetterPad.Language;
using BetterPad.Plugins;
using BetterPad.Running;
using BetterPad.Search;
using BetterPad.Syntax;
using BetterPad.Theme;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace BetterPad;

public partial class MainWindow : Window
{
    private const string OpenFilter =
        "All files (*.*)|*.*|Text and code (*.txt;*.cs;*.js;*.ts;*.py;*.json;*.xml;*.html;*.css;*.sql;*.md;*.ini;*.ps1;*.bat;*.cmd;*.cpp;*.h;*.tiny;*.rtf)|*.txt;*.cs;*.js;*.ts;*.py;*.json;*.xml;*.html;*.htm;*.css;*.sql;*.md;*.ini;*.cfg;*.ps1;*.bat;*.cmd;*.cpp;*.c;*.h;*.tiny;*.rtf|Rich Text (*.rtf)|*.rtf";

    private readonly List<DocumentTab> _documents = new();
    private readonly string[] _startupFiles;

    private AppSettings _settings;
    private SyntaxService _syntax;
    private PluginManager _plugins;
    private RunManager _run;
    private CancellationTokenSource? _tinyRun;
    private FindInFilesWindow? _findInFiles;
    private DocumentTab? _initialDocument;
    private int _untitledCount;
    private bool _suppressFormatEvents;

    public MainWindow(string[] startupFiles)
    {
        InitializeComponent();
        _startupFiles = startupFiles ?? [];
        SourceInitialized += (_, _) => WindowTheme.Apply(this);

        AppPaths.SeedSyntaxRules();
        _settings = AppSettings.Load();
        ThemeManager.Apply(_settings.Theme);
        UpdateThemeChecks();
        _syntax = new SyntaxService();
        _plugins = new PluginManager(
            () => ActiveDocument?.FilePath,
            () => ActiveDocument?.PlainText ?? string.Empty,
            ReplaceActiveText,
            InsertActiveText,
            (title, message) => AppDialog.Info(this, title, message),
            text => StatusText.Text = text);
        _run = new RunManager();
        _run.OutputReceived += OnRunOutput;
        _run.ProcessExited += OnRunExited;

        RestoreWindowPlacement();
        PopulateFormatCombos();

        ShowLineNumbersItem.IsChecked = _settings.ShowLineNumbers;
        OutputPanel.Visibility = Visibility.Collapsed;
        UpdateOutputMenu();

        _initialDocument = NewDocument();
        UpdateActiveDocumentUi();
    }

    private DocumentTab? ActiveDocument =>
        Tabs.SelectedItem is TabItem tab ? _documents.FirstOrDefault(d => d.Tab == tab) : null;

    private CodeEditor? ActiveCodeEditor => ActiveDocument?.Editor;

    private RichTextBox? ActiveRichEditor => ActiveDocument?.RichEditor;

    private void RestoreWindowPlacement()
    {
        double width = Math.Max(MinWidth, _settings.WindowWidth);
        double height = Math.Max(MinHeight, _settings.WindowHeight);
        double left = _settings.WindowLeft;
        double top = _settings.WindowTop;

        bool validPosition = !double.IsNaN(left) && !double.IsNaN(top)
                             && left + 80 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                             && left + width > SystemParameters.VirtualScreenLeft
                             && top + 40 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
                             && top + height > SystemParameters.VirtualScreenTop;

        if (validPosition)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        Width = width;
        Height = height;

        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void PopulateFormatCombos()
    {
        FontFamilyCombo.ItemsSource = Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FontSizeCombo.ItemsSource = new[]
        {
            "8", "9", "10", "11", "12", "13", "14", "16", "18",
            "20", "22", "24", "28", "32", "36", "48", "64", "72"
        };
    }

    // ---------------------------------------------------------------- documents

    private static void ApplyRichTheme(RichTextBox editor)
    {
        editor.SetResourceReference(BackgroundProperty, "EditorBgBrush");
        editor.SetResourceReference(ForegroundProperty, "TextBrush");
        editor.SetResourceReference(TextBoxBase.CaretBrushProperty, "EditorCaretBrush");
        editor.SetResourceReference(TextBoxBase.SelectionBrushProperty, "SelectionBrush");
    }

    private DocumentTab NewDocument(bool rich = false)
    {
        string name = _untitledCount++ == 0 ? "Untitled" : $"Untitled {_untitledCount}";
        var doc = new DocumentTab { UntitledName = name };

        if (rich)
        {
            var editor = new RichTextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Padding = new Thickness(14, 10, 14, 12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                UndoLimit = 100,
                IsDocumentEnabled = false
            };
            SpellCheck.SetIsEnabled(editor, true);
            ApplyRichTheme(editor);
            editor.TextChanged += (_, _) => OnDocumentTextChanged(doc);
            editor.SelectionChanged += (_, _) => OnRichSelectionChanged(doc);
            doc.RichEditor = editor;
            doc.Tab.Content = editor;
        }
        else
        {
            var editor = new CodeEditor();
            editor.SetFontSize(_settings.FontSize);
            editor.SetTabSize(_settings.TabSize);
            editor.ShowLineNumbers = _settings.ShowLineNumbers;
            doc.Syntax = _syntax.Plain;
            editor.SetSyntax(_syntax.Plain);
            editor.TextChanged += (_, _) => OnDocumentTextChanged(doc);
            editor.CaretChanged += (_, _) =>
            {
                if (ReferenceEquals(ActiveDocument, doc))
                {
                    UpdateStatusBar();
                }
            };
            editor.ZoomChanged += (_, _) =>
            {
                if (ReferenceEquals(ActiveDocument, doc))
                {
                    UpdateStatusBar();
                }
            };
            doc.Editor = editor;
            doc.Tab.Content = editor;
        }

        doc.TitleText = new TextBlock
        {
            Text = doc.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        doc.ModifiedDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        doc.ModifiedDot.SetResourceReference(Shape.FillProperty, "AccentBrush");
        var close = new Button
        {
            Content = "\u00D7",
            Style = (Style)FindResource("IconButton"),
            Width = 18,
            Height = 18,
            FontSize = 13,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Close tab"
        };
        close.Click += (_, _) => CloseDocument(doc);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(doc.TitleText);
        header.Children.Add(doc.ModifiedDot);
        header.Children.Add(close);
        doc.Tab.Header = header;

        _documents.Add(doc);
        Tabs.Items.Add(doc.Tab);
        Tabs.SelectedItem = doc.Tab;
        UpdateTabHeader(doc);
        UpdateActiveDocumentUi();
        Dispatcher.InvokeAsync(FocusActiveDocument, DispatcherPriority.Input);
        return doc;
    }

    private void OpenFile(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            DocumentTab? existing = _documents.FirstOrDefault(d =>
                d.FilePath != null &&
                string.Equals(Path.GetFullPath(d.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                Tabs.SelectedItem = existing.Tab;
                FocusActiveDocument();
                return;
            }

            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                AppDialog.Error(this, "Open file", $"File not found:\n{fullPath}");
                return;
            }

            if (info.Length > 25_000_000)
            {
                AppDialog.Error(this, "Open file",
                    "This file is larger than 25 MB. BetterPad opens smaller files so the editor stays responsive.");
                return;
            }

            if (LooksBinary(fullPath))
            {
                AppDialog.Error(this, "Open file",
                    "This looks like a binary file. BetterPad opens text and code files.");
                return;
            }

            if (string.Equals(info.Extension, ".rtf", StringComparison.OrdinalIgnoreCase))
            {
                OpenRichFile(fullPath);
            }
            else
            {
                OpenTextFile(fullPath);
            }

            _settings.LastOpenFolder = info.DirectoryName;
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "Open file", ex.Message);
        }
    }

    private void OpenFileAt(string path, int lineNumber)
    {
        OpenFile(path);
        CodeEditor? editor = ActiveCodeEditor;
        if (editor != null)
        {
            editor.GoToLine(lineNumber);
        }
    }

    private void OpenTextFile(string path)
    {
        LoadedText loaded = TextFileService.Load(path);
        DocumentTab doc = NewDocument();
        doc.SuppressModifiedTracking = true;
        try
        {
            doc.FilePath = path;
            doc.Encoding = loaded.Encoding;
            doc.Eol = loaded.Eol;
            doc.Syntax = _syntax.ForFile(path);
            doc.Editor!.SetSyntax(doc.Syntax);
            doc.Editor.Text = loaded.Text;
            doc.IsModified = false;
        }
        finally
        {
            doc.SuppressModifiedTracking = false;
        }

        UpdateTabHeader(doc);
        UpdateActiveDocumentUi();
    }

    private void OpenRichFile(string path)
    {
        DocumentTab doc = NewDocument(rich: true);
        doc.SuppressModifiedTracking = true;
        try
        {
            using FileStream stream = File.OpenRead(path);
            var range = new TextRange(doc.RichEditor!.Document.ContentStart, doc.RichEditor.Document.ContentEnd);
            range.Load(stream, DataFormats.Rtf);
            doc.FilePath = path;
            doc.IsModified = false;
        }
        catch
        {
            RemoveDocument(doc);
            throw;
        }
        finally
        {
            doc.SuppressModifiedTracking = false;
        }

        UpdateTabHeader(doc);
        UpdateActiveDocumentUi();
    }

    private static bool LooksBinary(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            var buffer = new byte[4096];
            int read = stream.Read(buffer, 0, buffer.Length);

            if (read >= 2 &&
                ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF)))
            {
                return false;
            }

            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void OnDocumentTextChanged(DocumentTab doc)
    {
        if (doc.SuppressModifiedTracking || doc.IsModified)
        {
            return;
        }

        doc.IsModified = true;
        UpdateTabHeader(doc);
    }

    private void OnRichSelectionChanged(DocumentTab doc)
    {
        if (!ReferenceEquals(ActiveDocument, doc))
        {
            return;
        }

        UpdateStatusBar();
        UpdateFormattingState();
    }

    private void UpdateTabHeader(DocumentTab doc)
    {
        doc.TitleText.Text = doc.DisplayName;

        // Keep the tooltip on the title text, not the TabItem: ToolTip inherits
        // down the logical tree, and the TabItem owns the editor as its content.
        doc.TitleText.ToolTip = doc.FilePath ?? doc.DisplayName;
        doc.ModifiedDot.Visibility = doc.IsModified ? Visibility.Visible : Visibility.Collapsed;
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        DocumentTab? doc = ActiveDocument;
        Title = doc == null
            ? "BetterPad"
            : $"{doc.DisplayName}{(doc.IsModified ? " *" : string.Empty)} - BetterPad";
    }

    private void UpdateActiveDocumentUi()
    {
        DocumentTab? doc = ActiveDocument;
        bool rich = doc?.IsRich == true;
        FormatBar.Visibility = rich ? Visibility.Visible : Visibility.Collapsed;

        if (rich)
        {
            UpdateFormattingState();
        }

        if (doc != null)
        {
            UpdateWindowTitle();
        }

        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            PositionText.Text = $"Ln {editor.CaretLineIndex + 1}, Col {editor.CaretColumn}";
            SelectionText.Text = $"Sel {editor.SelectionLength}";
            SyntaxText.Text = doc.Syntax?.Name ?? "Plain Text";
            EncodingText.Text = TextFileService.EncodingLabel(doc.Encoding);
            EolText.Text = doc.Eol == "\n" ? "LF" : "CRLF";
            ZoomText.Text = $"{editor.FontSize:0.#} pt";
            return;
        }

        if (doc?.RichEditor is { } richEditor)
        {
            PositionText.Text = "Rich text";
            SelectionText.Text = $"Sel {richEditor.Selection.Text.Length}";
            SyntaxText.Text = "Rich Text";
            EncodingText.Text = "RTF";
            EolText.Text = "RTF";
            ZoomText.Text = $"{richEditor.FontSize:0.#} pt";
            return;
        }

        PositionText.Text = "Ln 1, Col 1";
        SelectionText.Text = "Sel 0";
        SyntaxText.Text = "Plain Text";
        EncodingText.Text = "UTF-8";
        EolText.Text = "CRLF";
        ZoomText.Text = $"{_settings.FontSize:0.#} pt";
    }

    private void FocusActiveDocument()
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.FocusEditor();
        }
        else
        {
            doc?.RichEditor?.Focus();
        }
    }

    private void RemoveDocument(DocumentTab doc)
    {
        int index = Tabs.Items.IndexOf(doc.Tab);
        bool wasActive = ReferenceEquals(ActiveDocument, doc);

        _documents.Remove(doc);
        Tabs.Items.Remove(doc.Tab);
        doc.Editor?.Dispose();

        if (_documents.Count == 0)
        {
            NewDocument();
            return;
        }

        if (wasActive)
        {
            Tabs.SelectedIndex = Math.Clamp(index, 0, _documents.Count - 1);
        }

        UpdateActiveDocumentUi();
        UpdateWindowTitle();
    }

    private void CloseDocument(DocumentTab doc)
    {
        if (!ConfirmClose(doc))
        {
            return;
        }

        RemoveDocument(doc);
        FocusActiveDocument();
    }

    private bool ConfirmClose(DocumentTab doc)
    {
        if (!doc.IsModified)
        {
            return true;
        }

        int choice = AppDialog.Choose(this, "Unsaved changes",
            $"Save changes to \"{doc.DisplayName}\"?",
            ("Save", true), ("Don't save", false), ("Cancel", false));

        return choice switch
        {
            0 => SaveDocument(doc),
            1 => true,
            _ => false
        };
    }

    private bool SaveDocument(DocumentTab doc)
    {
        if (doc.FilePath == null)
        {
            return SaveDocumentAs(doc);
        }

        try
        {
            bool richRtf = doc.IsRich &&
                           string.Equals(Path.GetExtension(doc.FilePath), ".rtf", StringComparison.OrdinalIgnoreCase);
            if (richRtf)
            {
                SaveRichFile(doc, doc.FilePath);
            }
            else
            {
                TextFileService.Save(doc.FilePath, doc.PlainText, doc.Encoding, doc.Eol);
            }

            doc.IsModified = false;
            UpdateTabHeader(doc);
            StatusText.Text = $"Saved {doc.DisplayName}";
            return true;
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "Save file", ex.Message);
            return false;
        }
    }

    private bool SaveDocumentAs(DocumentTab doc)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save file",
            AddExtension = true,
            OverwritePrompt = true,
            Filter = doc.IsRich
                ? "Rich Text Format (*.rtf)|*.rtf|Plain text (*.txt)|*.txt|All files (*.*)|*.*"
                : "All files (*.*)|*.*|Text files (*.txt)|*.txt|Tiny (*.tiny)|*.tiny|C# (*.cs)|*.cs|JavaScript (*.js)|*.js|Python (*.py)|*.py",
            FilterIndex = 1,
            FileName = doc.FilePath != null
                ? Path.GetFileName(doc.FilePath)
                : doc.IsRich ? doc.UntitledName + ".rtf" : doc.UntitledName + ".txt",
            InitialDirectory = GetInitialFolder()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        doc.FilePath = dialog.FileName;
        if (doc.Editor != null)
        {
            doc.Syntax = _syntax.ForFile(doc.FilePath);
            doc.Editor.SetSyntax(doc.Syntax);
        }

        _settings.LastOpenFolder = Path.GetDirectoryName(dialog.FileName);
        UpdateTabHeader(doc);
        return SaveDocument(doc);
    }

    private static void SaveRichFile(DocumentTab doc, string path)
    {
        RichTextBox rich = doc.RichEditor!;

        // The RTF writer records the inherited foreground in the file. Save with the
        // theme foreground removed so a document saved in a dark theme does not turn
        // into light text when opened elsewhere.
        rich.ClearValue(ForegroundProperty);
        try
        {
            using FileStream stream = File.Create(path);
            var range = new TextRange(rich.Document.ContentStart, rich.Document.ContentEnd);
            range.Save(stream, DataFormats.Rtf);
        }
        finally
        {
            ApplyRichTheme(rich);
        }
    }

    private string GetInitialFolder()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LastOpenFolder) && Directory.Exists(_settings.LastOpenFolder))
        {
            return _settings.LastOpenFolder;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    // ---------------------------------------------------------------- find and replace

    private void ShowFindBar(bool replace)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc == null)
        {
            return;
        }

        if (doc.IsRich)
        {
            AppDialog.Info(this, "Find",
                "Find and replace currently works in code tabs. Rich text tabs are not searchable yet.");
            return;
        }

        FindBar.Visibility = Visibility.Visible;
        ReplaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;

        if (doc.Editor is { } editor)
        {
            string selected = editor.TextArea.SelectedText;
            if (!string.IsNullOrEmpty(selected) && selected.Length <= 200 &&
                !selected.Contains('\n') && !selected.Contains('\r'))
            {
                FindBox.Text = selected;
            }
        }

        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindStatus.Text = string.Empty;
        FocusActiveDocument();
    }

    private void ShowFind_Click(object sender, RoutedEventArgs e) => ShowFindBar(false);

    private void ShowReplace_Click(object sender, RoutedEventArgs e) => ShowFindBar(true);

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext(false);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindNext(true);

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FindStatus.Text = string.Empty;
    }

    private void FindOption_Click(object sender, RoutedEventArgs e)
    {
        FindStatus.Text = string.Empty;
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => CloseFind();

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReplaceCurrent();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private Regex? GetFindRegex()
    {
        if (string.IsNullOrEmpty(FindBox.Text))
        {
            FindStatus.Text = "Type something to find.";
            return null;
        }

        try
        {
            return SearchHelpers.Build(FindBox.Text,
                RegexCheck.IsChecked == true,
                MatchCaseCheck.IsChecked == true,
                WholeWordCheck.IsChecked == true);
        }
        catch (ArgumentException ex)
        {
            AppDialog.Error(this, "Invalid regular expression", ex.Message);
            return null;
        }
    }

    private string GetReplacementForRegex()
    {
        string replacement = ReplaceBox.Text;
        return RegexCheck.IsChecked == true ? replacement : replacement.Replace("$", "$$");
    }

    private bool FindNext(bool backwards)
    {
        CodeEditor? editor = ActiveCodeEditor;
        if (editor == null)
        {
            if (ActiveDocument is { IsRich: true })
            {
                AppDialog.Info(this, "Find",
                    "Find and replace currently works in code tabs. Rich text tabs are not searchable yet.");
            }

            return false;
        }

        Regex? regex = GetFindRegex();
        if (regex == null)
        {
            FindBox.Focus();
            return false;
        }

        string text = editor.TextArea.Text;
        try
        {
            Match? match;
            if (!backwards)
            {
                int from = editor.SelectionLength > 0
                    ? editor.SelectionStart + editor.SelectionLength
                    : editor.SelectionStart;
                from = Math.Clamp(from, 0, text.Length);

                match = regex.Match(text, from);
                if (!match.Success && from > 0)
                {
                    match = regex.Match(text, 0);
                }
            }
            else
            {
                int before = Math.Clamp(editor.SelectionStart, 0, text.Length);
                match = null;
                foreach (Match candidate in regex.Matches(text))
                {
                    if (candidate.Index + candidate.Length <= before)
                    {
                        match = candidate;
                    }
                    else
                    {
                        break;
                    }
                }

                if (match == null)
                {
                    foreach (Match candidate in regex.Matches(text))
                    {
                        match = candidate;
                    }
                }
            }

            if (match == null || !match.Success)
            {
                FindStatus.Text = "No matches";
                return false;
            }

            editor.SelectRange(match.Index, match.Length, focus: false);
            FindStatus.Text = $"Ln {editor.GetLineIndex(match.Index) + 1}";
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            FindStatus.Text = "Pattern took too long";
            return false;
        }
    }

    private void Replace_Click(object sender, RoutedEventArgs e) => ReplaceCurrent();

    private void ReplaceCurrent()
    {
        CodeEditor? editor = ActiveCodeEditor;
        if (editor == null)
        {
            if (ActiveDocument is { IsRich: true })
            {
                AppDialog.Info(this, "Replace",
                    "Find and replace currently works in code tabs. Rich text tabs are not searchable yet.");
            }

            return;
        }

        Regex? regex = GetFindRegex();
        if (regex == null)
        {
            ReplaceBox.Focus();
            return;
        }

        int start = editor.SelectionStart;
        int length = editor.SelectionLength;
        string selected = length > 0 ? editor.TextArea.SelectedText : string.Empty;

        try
        {
            Match current = selected.Length > 0 ? regex.Match(selected) : Match.Empty;
            if (current.Success && current.Index == 0 && current.Length == selected.Length)
            {
                string inserted = NormalizeNewlines(ReplaceBox.Text);
                editor.TextArea.SelectedText = inserted;
                editor.SelectRange(start + inserted.Length, 0, focus: false);
            }

            FindNext(false);
        }
        catch (RegexMatchTimeoutException)
        {
            FindStatus.Text = "Pattern took too long";
        }
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        CodeEditor? editor = ActiveCodeEditor;
        if (editor == null)
        {
            if (ActiveDocument is { IsRich: true })
            {
                AppDialog.Info(this, "Replace",
                    "Find and replace currently works in code tabs. Rich text tabs are not searchable yet.");
            }

            return;
        }

        Regex? regex = GetFindRegex();
        if (regex == null)
        {
            ReplaceBox.Focus();
            return;
        }

        string text = editor.TextArea.Text;
        int count = 0;
        try
        {
            foreach (Match _ in regex.Matches(text))
            {
                count++;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            AppDialog.Error(this, "Replace all", "The search pattern took too long to run on this file.");
            return;
        }

        if (count == 0)
        {
            FindStatus.Text = "No matches";
            return;
        }

        int choice = AppDialog.Choose(this, "Replace all",
            $"Replace {count} occurrence{(count == 1 ? string.Empty : "s")}?",
            ("Replace all", true), ("Cancel", false));

        if (choice != 0)
        {
            return;
        }

        string result;
        try
        {
            result = regex.Replace(text, GetReplacementForRegex());
        }
        catch (RegexMatchTimeoutException)
        {
            AppDialog.Error(this, "Replace all", "The search pattern took too long to run on this file.");
            return;
        }

        result = NormalizeNewlines(result);
        TextBox box = editor.TextArea;
        int caret = Math.Min(editor.SelectionStart, result.Length);
        box.Select(0, box.Text.Length);
        box.SelectedText = result;
        box.Select(caret, 0);
        StatusText.Text = $"Replaced {count} occurrence{(count == 1 ? string.Empty : "s")}.";
        FindStatus.Text = string.Empty;
    }

    private static string NormalizeNewlines(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
    }

    private void FindInFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_findInFiles is { IsVisible: true })
        {
            _findInFiles.Activate();
            return;
        }

        string folder = _settings.LastSearchFolder is { } saved && Directory.Exists(saved)
            ? saved
            : GetInitialFolder();

        var window = new FindInFilesWindow(folder, OpenFileAt) { Owner = this };
        _findInFiles = window;
        window.Closed += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(window.LastFolder))
            {
                _settings.LastSearchFolder = window.LastFolder;
            }

            if (ReferenceEquals(_findInFiles, window))
            {
                _findInFiles = null;
            }
        };
        window.Show();
    }

    // ---------------------------------------------------------------- rich text

    private void UpdateFormattingState()
    {
        RichTextBox? rich = ActiveRichEditor;
        if (rich == null)
        {
            return;
        }

        _suppressFormatEvents = true;
        try
        {
            object weight = rich.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            BoldButton.IsChecked = weight is FontWeight fontWeight && fontWeight == FontWeights.Bold;

            object style = rich.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            ItalicButton.IsChecked = style is FontStyle fontStyle && fontStyle == FontStyles.Italic;

            object decorations = rich.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            UnderlineButton.IsChecked = decorations is TextDecorationCollection collection && collection.Count > 0;

            object family = rich.Selection.GetPropertyValue(TextElement.FontFamilyProperty);
            if (family is FontFamily fontFamily)
            {
                FontFamilyCombo.Text = fontFamily.Source;
            }

            object size = rich.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (size is double fontSize)
            {
                FontSizeCombo.Text = fontSize.ToString("0.#", CultureInfo.CurrentCulture);
            }
        }
        finally
        {
            _suppressFormatEvents = false;
        }
    }

    private void ApplyFormat(DependencyProperty property, object value)
    {
        RichTextBox? rich = ActiveRichEditor;
        if (rich == null)
        {
            return;
        }

        rich.Selection.ApplyPropertyValue(property, value);
        rich.Focus();
    }

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveRichEditor == null)
        {
            return;
        }

        ApplyFormat(TextElement.FontWeightProperty,
            BoldButton.IsChecked == true ? FontWeights.Bold : FontWeights.Normal);
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveRichEditor == null)
        {
            return;
        }

        ApplyFormat(TextElement.FontStyleProperty,
            ItalicButton.IsChecked == true ? FontStyles.Italic : FontStyles.Normal);
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveRichEditor == null)
        {
            return;
        }

        ApplyFormat(Inline.TextDecorationsProperty,
            UnderlineButton.IsChecked == true ? TextDecorations.Underline : new TextDecorationCollection());
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFormatEvents || ActiveRichEditor == null || FontFamilyCombo.SelectedItem is not string name)
        {
            return;
        }

        ApplyFormat(TextElement.FontFamilyProperty, new FontFamily(name));
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFormatEvents || ActiveRichEditor == null || FontSizeCombo.SelectedItem is not string text)
        {
            return;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double size))
        {
            ApplyFormat(TextElement.FontSizeProperty, Math.Clamp(size, 6, 96));
        }
    }

    private void FontCombo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ActiveRichEditor == null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            FocusActiveDocument();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        if (ReferenceEquals(sender, FontFamilyCombo))
        {
            string name = FontFamilyCombo.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            ApplyFormat(TextElement.FontFamilyProperty, new FontFamily(name));
        }
        else if (ReferenceEquals(sender, FontSizeCombo))
        {
            if (!double.TryParse(FontSizeCombo.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double size))
            {
                AppDialog.Error(this, "Font size", "Type a number for the font size.");
                return;
            }

            ApplyFormat(TextElement.FontSizeProperty, Math.Clamp(size, 6, 96));
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- plugins

    private void ReplaceActiveText(string text)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.TextArea.SelectAll();
            editor.TextArea.SelectedText = NormalizeNewlines(text);
        }
        else if (doc is { IsRich: true })
        {
            StatusText.Text = "Plugin text changes apply to code tabs.";
        }
    }

    private void InsertActiveText(string text)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.InsertText(NormalizeNewlines(text));
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.Focus();
            rich.Selection.Text = text;
        }
    }

    private void RebuildPluginMenu()
    {
        for (int i = PluginsMenu.Items.Count - 1; i >= 0; i--)
        {
            if (PluginsMenu.Items[i] is MenuItem item && Equals(item.Tag, "plugin"))
            {
                PluginsMenu.Items.RemoveAt(i);
            }
        }

        for (int i = 0; i < _plugins.Commands.Count; i++)
        {
            PluginCommand command = _plugins.Commands[i];
            var item = new MenuItem
            {
                Header = command.Title.Replace("_", "__"),
                Tag = "plugin"
            };
            item.Click += (_, _) => ExecutePluginCommand(command);
            PluginsMenu.Items.Insert(i, item);
        }

        NoPluginItem.Visibility = _plugins.Commands.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PluginSeparator.Visibility = _plugins.Commands.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExecutePluginCommand(PluginCommand command)
    {
        try
        {
            command.Action();
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "Plugin error", ex.Message);
        }
    }

    private void ReloadPlugins_Click(object sender, RoutedEventArgs e)
    {
        _plugins.LoadAll();
        RebuildPluginMenu();
        StatusText.Text = $"Loaded {_plugins.LoadedCount} plugin(s).";

        if (_plugins.Errors.Count > 0)
        {
            AppDialog.Error(this, "Plugin errors", FormatErrors(_plugins.Errors));
        }
    }

    private void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Install plugin",
            Filter = "Plugin assembly (*.dll)|*.dll|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            string destination = _plugins.InstallPlugin(dialog.FileName);
            _plugins.LoadAll();
            RebuildPluginMenu();
            StatusText.Text = $"Installed {Path.GetFileName(destination)}.";

            if (_plugins.Errors.Count > 0)
            {
                AppDialog.Error(this, "Plugin errors", FormatErrors(_plugins.Errors));
            }
        }
        catch (Exception ex)
        {
            AppDialog.Error(this, "Install plugin", ex.Message);
        }
    }

    private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(AppPaths.PluginsDirectory);
    }

    private static string FormatErrors(IReadOnlyList<string> errors)
    {
        const int max = 8;
        IEnumerable<string> shown = errors.Take(max).Select(error => "- " + error);
        string text = string.Join("\n", shown);
        if (errors.Count > max)
        {
            text += $"\n\n…and {errors.Count - max} more.";
        }

        return text;
    }

    // ---------------------------------------------------------------- syntax

    private void SyntaxFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(AppPaths.SyntaxDirectory);
    }

    private void ReloadSyntax_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.SeedSyntaxRules();
        _syntax.Reload();

        foreach (DocumentTab doc in _documents)
        {
            if (doc.Editor != null)
            {
                doc.Syntax = _syntax.ForFile(doc.FilePath);
                doc.Editor.SetSyntax(doc.Syntax);
            }
        }

        UpdateStatusBar();
        StatusText.Text = $"Loaded {_syntax.Definitions.Count} syntax rule set(s).";

        if (_syntax.Errors.Count > 0)
        {
            AppDialog.Error(this, "Syntax rules", FormatErrors(_syntax.Errors));
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Explorer is a convenience; a missing shell should not crash the editor.
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        const string syntaxPath = @"C:\Users\USER\AppData\Roaming\BetterPad\syntax";
        const string pluginsPath = @"C:\Users\USER\AppData\Roaming\BetterPad\plugins";
        string version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        AppDialog.Info(this, "About BetterPad",
            $"BetterPad {version}\n\n" +
            "A tabbed editor for code and plain text. Syntax rules and plugins are ordinary files on disk, and Run/Build use the compilers already installed on this PC.\n\n" +
            $"Syntax rules:\n{syntaxPath}\n\n" +
            $"Plugins:\n{pluginsPath}\n\n" +
            ".tiny scripts run on the built-in Tiny interpreter (F5). Find and replace searches code tabs only; rich text tabs are skipped.");
    }

    // ---------------------------------------------------------------- running

    private void Run_Click(object sender, RoutedEventArgs e) => RunDocument(build: false);

    private void Build_Click(object sender, RoutedEventArgs e) => RunDocument(build: true);

    private void RunDocument(bool build)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc == null)
        {
            return;
        }

        if (doc.IsRich)
        {
            AppDialog.Error(this, build ? "Build" : "Run",
                "Rich text documents cannot be built or run.");
            return;
        }

        if (doc.FilePath == null || doc.IsModified)
        {
            if (!SaveDocument(doc))
            {
                return;
            }
        }

        if (doc.FilePath == null)
        {
            return;
        }

        if (string.Equals(Path.GetExtension(doc.FilePath), ".tiny", StringComparison.OrdinalIgnoreCase))
        {
            if (build)
            {
                AppDialog.Error(this, "Build", "Tiny is interpreted, so there is nothing to build. Press Run (F5) instead.");
                return;
            }

            RunTiny(doc);
            return;
        }

        ShowOutput(build ? "Build - " + doc.DisplayName : "Run - " + doc.DisplayName);
        OutputBox.Clear();

        string? error = _run.Start(doc.FilePath, build);
        if (error != null)
        {
            AppendOutput(error + "\r\n");
            SetRunning(false);
            StatusText.Text = "Could not start.";
            AppDialog.Error(this, build ? "Build" : "Run", error);
            return;
        }

        SetRunning(true);
        StatusText.Text = build ? "Building…" : "Running…";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_tinyRun != null)
        {
            _tinyRun.Cancel();
            StatusText.Text = "Stopping…";
            return;
        }

        if (!_run.IsRunning)
        {
            StatusText.Text = "Nothing is running.";
            return;
        }

        _run.Stop();
        StatusText.Text = "Stopping…";
    }

    private void RunTiny(DocumentTab doc)
    {
        _tinyRun?.Cancel();
        var cancellation = new CancellationTokenSource();
        _tinyRun = cancellation;

        ShowOutput("Run - " + doc.DisplayName);
        OutputBox.Clear();
        SetRunning(true);
        StatusText.Text = "Running…";

        string source = doc.PlainText;
        _ = Task.Run(() =>
        {
            TinyRunResult result = TinyEngine.Run(source, text =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!Dispatcher.HasShutdownStarted)
                    {
                        AppendOutput(text);
                    }
                }));
            }, cancellation.Token);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Dispatcher.HasShutdownStarted || !ReferenceEquals(_tinyRun, cancellation))
                {
                    return;
                }

                _tinyRun = null;
                cancellation.Dispose();
                SetRunning(false);

                if (result.Cancelled)
                {
                    AppendOutput("[stopped]\r\n");
                    StatusText.Text = "Stopped.";
                    return;
                }

                if (result.Error is { } error)
                {
                    AppendOutput(error.Display + "\r\n");
                    StatusText.Text = "Tiny error.";
                    AppDialog.Error(this, "Tiny", error.Display);
                    return;
                }

                StatusText.Text = "Finished.";
            }));
        });
    }

    private void OnRunOutput(string text)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => AppendOutput(text)));
    }

    private void OnRunExited(int exitCode)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            SetRunning(false);
            StatusText.Text = exitCode == 0
                ? "Finished."
                : $"Finished with exit code {exitCode}.";
        }));
    }

    private void AppendOutput(string text)
    {
        OutputBox.AppendText(text);
        OutputBox.ScrollToEnd();
    }

    private void SetRunning(bool running)
    {
        RunButton.IsEnabled = !running;
        BuildButton.IsEnabled = !running;
        StopRunButton.IsEnabled = running;
        StopButton.IsEnabled = running;
    }

    private void ShowOutput(string title)
    {
        OutputTitle.Text = title;
        OutputPanel.Visibility = Visibility.Visible;
        ViewOutputItem.IsChecked = true;
    }

    private void HideOutput_Click(object sender, RoutedEventArgs e)
    {
        OutputPanel.Visibility = Visibility.Collapsed;
        UpdateOutputMenu();
    }

    private void ViewOutput_Click(object sender, RoutedEventArgs e)
    {
        OutputPanel.Visibility = ViewOutputItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearOutput_Click(object sender, RoutedEventArgs e)
    {
        OutputBox.Clear();
    }

    private void UpdateOutputMenu()
    {
        ViewOutputItem.IsChecked = OutputPanel.Visibility == Visibility.Visible;
    }

    // ---------------------------------------------------------------- menu, edit, view

    private void New_Click(object sender, RoutedEventArgs e) => NewDocument();

    private void NewRich_Click(object sender, RoutedEventArgs e) => NewDocument(rich: true);

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open",
            Filter = OpenFilter,
            FilterIndex = 1,
            Multiselect = true,
            InitialDirectory = GetInitialFolder()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (string file in dialog.FileNames)
        {
            OpenFile(file);
        }

        _settings.LastOpenFolder = Path.GetDirectoryName(dialog.FileNames.FirstOrDefault() ?? string.Empty);
        FocusActiveDocument();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is { } doc)
        {
            SaveDocument(doc);
        }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is { } doc)
        {
            SaveDocumentAs(doc);
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveDocument is { } doc)
        {
            CloseDocument(doc);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.FocusEditor();
            editor.TextArea.Undo();
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.Focus();
            rich.Undo();
        }
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.FocusEditor();
            editor.TextArea.Redo();
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.Focus();
            rich.Redo();
        }
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.FocusEditor();
            editor.TextArea.Cut();
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.Focus();
            rich.Cut();
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.TextArea.Copy();
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.Copy();
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.FocusEditor();
            editor.TextArea.Paste();
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.Focus();
            rich.Paste();
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.TextArea.SelectAll();
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.SelectAll();
        }
    }

    private void LineNumbers_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowLineNumbers = ShowLineNumbersItem.IsChecked == true;
        foreach (DocumentTab doc in _documents)
        {
            if (doc.Editor != null)
            {
                doc.Editor.ShowLineNumbers = _settings.ShowLineNumbers;
            }
        }

        StatusText.Text = _settings.ShowLineNumbers ? "Line numbers shown." : "Line numbers hidden.";
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string id })
        {
            return;
        }

        _settings.Theme = id;
        ThemeManager.Apply(id);
        UpdateThemeChecks();
        WindowTheme.Apply(this);
        if (_findInFiles is { } findWindow)
        {
            WindowTheme.Apply(findWindow);
        }

        foreach (DocumentTab doc in _documents)
        {
            doc.Editor?.RefreshTheme();
        }

        StatusText.Text = $"Theme: {ThemeManager.Current.Name}.";
    }

    private void UpdateThemeChecks()
    {
        string current = ThemeManager.Current.Id;
        ThemeLightItem.IsChecked = current == "light";
        ThemeBlackRedItem.IsChecked = current == "black-red";
        ThemeMidnightItem.IsChecked = current == "midnight";
        ThemeSepiaItem.IsChecked = current == "sepia";
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(-1);

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.ResetZoom(13.5);
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.FontSize = 14;
        }

        UpdateStatusBar();
    }

    private void ZoomBy(double delta)
    {
        DocumentTab? doc = ActiveDocument;
        if (doc?.Editor is { } editor)
        {
            editor.SetFontSize(editor.FontSize + delta);
        }
        else if (doc?.RichEditor is { } rich)
        {
            rich.FontSize = Math.Clamp(rich.FontSize + delta, 8, 48);
        }

        UpdateStatusBar();
    }

    // ---------------------------------------------------------------- window events

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        bool openedAny = false;
        foreach (string file in _startupFiles)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            int before = _documents.Count;
            OpenFile(file);
            openedAny |= _documents.Count > before;
        }

        if (openedAny && _initialDocument is { } initial &&
            _documents.Contains(initial) && !initial.IsModified && initial.FilePath == null)
        {
            RemoveDocument(initial);
        }

        _plugins.SeedSamplePlugin();
        _plugins.LoadAll();
        RebuildPluginMenu();

        if (_syntax.Errors.Count > 0)
        {
            AppDialog.Error(this, "Syntax rules", FormatErrors(_syntax.Errors));
        }

        if (_plugins.Errors.Count > 0)
        {
            AppDialog.Error(this, "Plugin errors", FormatErrors(_plugins.Errors));
        }

        UpdateActiveDocumentUi();
        FocusActiveDocument();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _tinyRun?.Cancel();

        if (_run.IsRunning)
        {
            int choice = AppDialog.Choose(this, "Close BetterPad",
                "A process is still running. Stop it and close?",
                ("Stop and close", true), ("Cancel", false));

            if (choice != 0)
            {
                e.Cancel = true;
                return;
            }

            _run.Stop();
        }

        foreach (DocumentTab doc in _documents.ToList())
        {
            if (!doc.IsModified)
            {
                continue;
            }

            if (!ConfirmClose(doc))
            {
                e.Cancel = true;
                return;
            }
        }

        SaveSettings();
        _run.Dispose();
        _plugins.Dispose();
    }

    private void SaveSettings()
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }

        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        _settings.ShowLineNumbers = ShowLineNumbersItem.IsChecked == true;

        if (ActiveCodeEditor is { } editor)
        {
            _settings.FontSize = editor.FontSize;
        }

        _settings.Save();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (control && !alt)
        {
            if (shift && e.Key == Key.S)
            {
                SaveAs_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (shift && e.Key == Key.F)
            {
                FindInFiles_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (shift && e.Key == Key.B)
            {
                Build_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab)
            {
                CycleTab(shift ? -1 : 1);
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.N:
                    NewDocument();
                    e.Handled = true;
                    return;
                case Key.O:
                    Open_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.S:
                    Save_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.W:
                    CloseTab_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.F:
                    ShowFindBar(false);
                    e.Handled = true;
                    return;
                case Key.H:
                    ShowFindBar(true);
                    e.Handled = true;
                    return;
                case Key.D0:
                case Key.NumPad0:
                    ResetZoom_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.Oem3:
                    ViewOutputItem.IsChecked = OutputPanel.Visibility != Visibility.Visible;
                    ViewOutput_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.B:
                    if (ActiveRichEditor != null)
                    {
                        BoldButton.IsChecked = BoldButton.IsChecked != true;
                        Bold_Click(sender, e);
                        e.Handled = true;
                    }

                    return;
                case Key.I:
                    if (ActiveRichEditor != null)
                    {
                        ItalicButton.IsChecked = ItalicButton.IsChecked != true;
                        Italic_Click(sender, e);
                        e.Handled = true;
                    }

                    return;
                case Key.U:
                    if (ActiveRichEditor != null)
                    {
                        UnderlineButton.IsChecked = UnderlineButton.IsChecked != true;
                        Underline_Click(sender, e);
                        e.Handled = true;
                    }

                    return;
            }
        }

        if (!control && !alt)
        {
            if (shift && e.Key == Key.F5)
            {
                Stop_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5)
            {
                Run_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F3)
            {
                FindNext(shift);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
            {
                CloseFind();
                e.Handled = true;
            }
        }
    }

    private void CycleTab(int delta)
    {
        if (_documents.Count < 2)
        {
            return;
        }

        int count = _documents.Count;
        int index = Tabs.SelectedIndex;
        Tabs.SelectedIndex = ((index + delta) % count + count) % count;
        FocusActiveDocument();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (string file in files)
            {
                OpenFile(file);
            }

            e.Handled = true;
            FocusActiveDocument();
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem == null)
        {
            return;
        }

        if (ActiveDocument?.IsRich == true)
        {
            FindBar.Visibility = Visibility.Collapsed;
        }

        UpdateActiveDocumentUi();
    }
}
