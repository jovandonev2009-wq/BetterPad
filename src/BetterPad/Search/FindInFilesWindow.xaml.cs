using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using BetterPad.Dialogs;
using BetterPad.Theme;
using Microsoft.Win32;

namespace BetterPad.Search;

public partial class FindInFilesWindow : Window
{
    private const int MaxMatches = 10_000;

    private readonly Action<string, int> _openFile;
    private CancellationTokenSource? _cancellation;
    private bool _running;
    private bool _closed;

    public FindInFilesWindow(string? initialFolder, Action<string, int> openFile)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheme.Apply(this);
        _openFile = openFile;
        FolderBox.Text = initialFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Closed += (_, _) =>
        {
            _closed = true;
            _cancellation?.Cancel();
        };
        SearchBox.Focus();
    }

    public string LastFolder => FolderBox.Text;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to search",
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null
        };

        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        string folder = FolderBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            AppDialog.Error(this, "Find in Files", "Choose a folder that exists before searching.");
            return;
        }

        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            AppDialog.Error(this, "Find in Files", "Type something to search for.");
            SearchBox.Focus();
            return;
        }

        Regex regex;
        try
        {
            regex = SearchHelpers.Build(SearchBox.Text, RegexCheck.IsChecked == true,
                MatchCaseCheck.IsChecked == true, WholeWordCheck.IsChecked == true);
        }
        catch (ArgumentException ex)
        {
            AppDialog.Error(this, "Invalid regular expression", ex.Message);
            SearchBox.Focus();
            return;
        }

        List<string> masks = MaskBox.Text
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(mask => mask.Contains('*') || mask.Contains('?') ? mask : mask.StartsWith('.') ? "*" + mask : "*." + mask)
            .DefaultIfEmpty("*.*")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool recursive = SubfoldersCheck.IsChecked == true;
        string root = Path.GetFullPath(folder);
        _running = true;
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        SearchButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        ResultsView.Items.Clear();
        SummaryText.Text = "Searching…";
        StatusText.Text = "Scanning";

        int scanned = 0;
        int matchedFiles = 0;
        int matches = 0;
        int skipped = 0;
        bool limitHit = false;
        var pending = new List<MatchItem>(256);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Task.Run(() =>
            {
                var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = recursive,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                foreach (string mask in masks)
                {
                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(root, mask, options))
                        {
                            files.Add(file);
                        }
                    }
                    catch
                    {
                        // Keep going with the other masks.
                    }
                }

                foreach (string file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if (token.IsCancellationRequested || limitHit)
                    {
                        break;
                    }

                    scanned++;
                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists || info.Length > 5_000_000)
                        {
                            skipped++;
                            continue;
                        }

                        string text;
                        using (var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                        {
                            text = reader.ReadToEnd();
                        }

                        if (text.Contains('\0'))
                        {
                            skipped++;
                            continue;
                        }

                        bool fileMatched = false;
                        string[] lines = text.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (token.IsCancellationRequested)
                            {
                                break;
                            }

                            string line = lines[i].TrimEnd('\r');
                            bool isMatch;
                            try
                            {
                                isMatch = regex.IsMatch(line);
                            }
                            catch (RegexMatchTimeoutException)
                            {
                                break;
                            }

                            if (!isMatch)
                            {
                                continue;
                            }

                            fileMatched = true;
                            matches++;
                            string display = line.Length > 400 ? line[..400] + "…" : line;
                            pending.Add(new MatchItem
                            {
                                Path = file,
                                DisplayPath = Relative(root, file),
                                LineNumber = i + 1,
                                LineText = display.Replace("\t", "    ")
                            });

                            if (matches >= MaxMatches)
                            {
                                limitHit = true;
                                break;
                            }
                        }

                        if (fileMatched)
                        {
                            matchedFiles++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }

                    if (pending.Count >= 200 || (pending.Count > 0 && stopwatch.ElapsedMilliseconds > 150))
                    {
                        List<MatchItem> batch = pending;
                        pending = new List<MatchItem>(256);
                        stopwatch.Restart();
                        Dispatcher.Invoke(() => AddBatch(batch));
                    }

                    if (scanned % 40 == 0)
                    {
                        int localScanned = scanned;
                        int localMatches = matches;
                        Dispatcher.BeginInvoke(() =>
                        {
                            StatusText.Text = $"{localScanned} files scanned · {localMatches} matching lines";
                        });
                    }
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            // Expected when Stop is pressed.
        }

        if (!_closed)
        {
            AddBatch(pending);
            _running = false;
            SearchButton.IsEnabled = true;
            StopButton.IsEnabled = false;

            if (limitHit)
            {
                SummaryText.Text = $"Showing the first {MaxMatches} matching lines from {matchedFiles} files.";
            }
            else if (token.IsCancellationRequested)
            {
                SummaryText.Text = $"Stopped. {matches} matching lines in {matchedFiles} files.";
            }
            else
            {
                SummaryText.Text = matches == 0
                    ? "No matching lines found."
                    : $"{matches} matching lines in {matchedFiles} files.";
            }

            StatusText.Text = skipped > 0 ? $"{skipped} files skipped" : "Done";
        }
    }

    private void AddBatch(List<MatchItem> batch)
    {
        if (_closed || batch.Count == 0)
        {
            return;
        }

        foreach (MatchItem item in batch)
        {
            ResultsView.Items.Add(item);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        StatusText.Text = "Stopping…";
    }

    private void ResultsView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelected();
    }

    private void ResultsView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelected();
            e.Handled = true;
        }
    }

    private void OpenSelected()
    {
        if (ResultsView.SelectedItem is MatchItem item)
        {
            _openFile(item.Path, item.LineNumber);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string Relative(string root, string file)
    {
        try
        {
            string relative = Path.GetRelativePath(root, file);
            return relative.StartsWith("..", StringComparison.Ordinal) ? file : relative;
        }
        catch
        {
            return file;
        }
    }

    private sealed class MatchItem
    {
        public string Path { get; init; } = string.Empty;

        public string DisplayPath { get; init; } = string.Empty;

        public int LineNumber { get; init; }

        public string LineText { get; init; } = string.Empty;
    }
}
