using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BetterPad.Theme;

/// <summary>
/// Tells the Windows compositor to paint the native title bar dark when the
/// active theme is dark. No-op on systems that do not support the attribute.
/// </summary>
internal static class WindowTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const uint NcActivate = 0x0086;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    public static void Apply(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int dark = ThemeManager.Current.IsDark ? 1 : 0;
        bool applied = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int)) == 0;
        if (!applied)
        {
            applied = DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref dark, sizeof(int)) == 0;
        }

        // DWM records the attribute but keeps painting the old frame until the window
        // gets a non-client activation cycle. Fake one so a live theme switch repaints
        // the title bar in place. Skipped before first show, where the attribute alone
        // is enough.
        if (applied && window.IsVisible)
        {
            SendMessage(handle, NcActivate, IntPtr.Zero, IntPtr.Zero);
            SendMessage(handle, NcActivate, new IntPtr(1), IntPtr.Zero);
        }
    }
}
