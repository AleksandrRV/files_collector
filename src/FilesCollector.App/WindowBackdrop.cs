using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FilesCollector.App;

public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int BackdropTypeMica = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Applies the Windows 11 Mica backdrop and aligns the non-client dark mode with the app theme.
    /// Safe to call on any Windows version: failures are ignored and the solid window background stays.
    /// </summary>
    public static bool TryApplyMica(Window window, bool isDark)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var handle = helper.Handle;
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var build = Environment.OSVersion.Version.Build;
            if (build < 22000)
            {
                return false;
            }

            var darkMode = isDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

            var backdropType = BackdropTypeMica;
            var result = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdropType, sizeof(int));
            if (result != 0)
            {
                return false;
            }

            window.Background = System.Windows.Media.Brushes.Transparent;
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
