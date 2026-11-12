using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace BetterPad;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var window = new MainWindow(e.Args);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("BetterPad could not start.\n\n" + ex.Message, "BetterPad",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        string logPath = Path.Combine(AppPaths.Root, "crash.log");
        try
        {
            AppPaths.EnsureFolders();
            File.AppendAllText(logPath, $"[{DateTime.Now:u}] {e.Exception}\n\n");
        }
        catch
        {
            // Ignore logging failures.
        }

        if (MainWindow is { IsLoaded: true } window)
        {
            Dialogs.AppDialog.Error(window, "Unexpected error",
                e.Exception.Message + "\n\nDetails were written to:\n" + logPath);
        }
        else
        {
            MessageBox.Show(e.Exception.Message, "BetterPad", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
