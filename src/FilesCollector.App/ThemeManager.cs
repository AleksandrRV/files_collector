using System.Windows;
using Microsoft.Win32;

namespace FilesCollector.App;

public static class ThemeManager
{
    private static readonly Uri LightThemeUri = new("pack://application:,,,/FilesCollector.App;component/Themes/Theme.Light.xaml");
    private static readonly Uri DarkThemeUri = new("pack://application:,,,/FilesCollector.App;component/Themes/Theme.Dark.xaml");
    private static readonly Uri ComfortableDensityUri = new("pack://application:,,,/FilesCollector.App;component/Themes/Theme.Density.Comfortable.xaml");
    private static readonly Uri CompactDensityUri = new("pack://application:,,,/FilesCollector.App;component/Themes/Theme.Density.Compact.xaml");

    private static ThemeMode _appliedMode = ThemeMode.System;
    private static DensityMode _appliedDensity = DensityMode.Comfortable;

    public static event EventHandler? ThemeChanged;

    public static ThemeMode AppliedMode => _appliedMode;

    public static DensityMode AppliedDensity => _appliedDensity;

    public static bool IsDark => ResolveIsDark(_appliedMode);

    public static void ApplyTheme(ThemeMode mode)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        _appliedMode = mode;
        var resolved = mode is ThemeMode.Light or ThemeMode.Dark ? mode : DetectSystemTheme();
        var uri = resolved == ThemeMode.Dark ? DarkThemeUri : LightThemeUri;
        var dictionaries = app.Resources.MergedDictionaries;

        if (dictionaries.Count == 0 || dictionaries[0].Source != uri)
        {
            dictionaries.RemoveAt(0);
            dictionaries.Insert(0, new ResourceDictionary { Source = uri });
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyDensity(DensityMode density)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        _appliedDensity = density;
        var uri = density == DensityMode.Compact ? CompactDensityUri : ComfortableDensityUri;
        var dictionaries = app.Resources.MergedDictionaries;

        if (dictionaries.Count < 2 || dictionaries[1].Source != uri)
        {
            dictionaries.RemoveAt(1);
            dictionaries.Insert(1, new ResourceDictionary { Source = uri });
        }
    }

    public static bool ResolveIsDark(ThemeMode mode)
    {
        return mode is ThemeMode.Light or ThemeMode.Dark ? mode == ThemeMode.Dark : DetectSystemTheme() == ThemeMode.Dark;
    }

    public static ThemeMode DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("ColorMode") is int colorMode && colorMode == 0)
            {
                return ThemeMode.Dark;
            }
        }
        catch (SystemException)
        {
            // Ignore registry access problems and default to light.
        }
        catch (ObjectDisposedException)
        {
            // Ignore registry access problems and default to light.
        }

        return ThemeMode.Light;
    }
}
