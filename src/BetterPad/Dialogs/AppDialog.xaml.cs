using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterPad.Dialogs;

public partial class AppDialog : Window
{
    private int _result = -1;
    private int _cancelIndex;

    public AppDialog()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _result = _cancelIndex;
                Close();
                e.Handled = true;
            }
        };
        Closed += (_, _) =>
        {
            if (_result < 0)
            {
                _result = _cancelIndex;
            }
        };
    }

    public static void Info(Window? owner, string title, string message)
    {
        ShowCore(owner, title, message, [("OK", true)]);
    }

    public static void Error(Window? owner, string title, string message)
    {
        ShowCore(owner, title, message, [("OK", true)]);
    }

    public static bool Confirm(Window? owner, string title, string message,
        string primary, string secondary, string cancel = "Cancel")
    {
        return ShowCore(owner, title, message, [(primary, true), (secondary, false), (cancel, false)]) == 0;
    }

    public static int Choose(Window? owner, string title, string message,
        params (string Label, bool Primary)[] buttons)
    {
        return ShowCore(owner, title, message, buttons);
    }

    private static int ShowCore(Window? owner, string title, string message,
        (string Label, bool Primary)[] buttons)
    {
        var dialog = new AppDialog
        {
            Title = title,
            _cancelIndex = Math.Max(0, buttons.Length - 1)
        };

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;

        for (int i = 0; i < buttons.Length; i++)
        {
            var button = new Button
            {
                Content = buttons[i].Label,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                MinWidth = 84,
                IsDefault = i == 0,
                Style = (Style)dialog.FindResource(buttons[i].Primary ? "AccentButton" : "DialogButton")
            };

            int index = i;
            button.Click += (_, _) =>
            {
                dialog._result = index;
                dialog.Close();
            };
            dialog.ButtonsPanel.Children.Add(button);
        }

        if (owner != null)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog._result;
    }
}
