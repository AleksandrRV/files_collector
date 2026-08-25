using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace FilesCollector.App;

/// <summary>
/// Switches the application between the light and dark palettes at runtime and
/// remembers the choice in application data. An empty saved value (or a missing
/// file) means "follow the Windows system theme".
/// </summary>
public static class ThemeManager
{
    private const string LightSource = "Themes/Colors.Light.xaml";
    private const string DarkSource = "Themes/Colors.Dark.xaml";
    private const string ThemeFileName = "theme.txt";

    private static string? _storageDirectory;
    private static bool _isDark;

    public static bool IsDark => _isDark;

    public static void Initialize(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);

        _storageDirectory = storageDirectory;
        var saved = ReadSavedTheme();
        _isDark = saved switch
        {
            "dark" => true,
            "light" => false,
            _ => IsSystemDark()
        };
        Apply(_isDark);
    }

    public static void Toggle()
    {
        Apply(!_isDark);
        WriteSavedTheme(_isDark ? "dark" : "light");
    }

    public static void Apply(bool isDark)
    {
        _isDark = isDark;
        if (Application.Current is null)
        {
            return;
        }

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(isDark ? DarkSource : LightSource, UriKind.Relative)
        };
        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
        for (var index = 0; index < mergedDictionaries.Count; index++)
        {
            var source = mergedDictionaries[index].Source?.OriginalString;
            if (source is not null && source.Contains("Colors.", StringComparison.OrdinalIgnoreCase))
            {
                mergedDictionaries[index] = dictionary;
                return;
            }
        }

        mergedDictionaries.Insert(0, dictionary);
    }

    private static bool IsSystemDark()
    {
        var value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            1);
        return value is int appsUseLightTheme && appsUseLightTheme == 0;
    }

    private static string? ReadSavedTheme()
    {
        try
        {
            if (_storageDirectory is null)
            {
                return null;
            }

            var path = Path.Combine(_storageDirectory, ThemeFileName);
            return File.Exists(path) ? File.ReadAllText(path).Trim().ToLowerInvariant() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteSavedTheme(string value)
    {
        try
        {
            if (_storageDirectory is null)
            {
                return;
            }

            Directory.CreateDirectory(_storageDirectory);
            File.WriteAllText(Path.Combine(_storageDirectory, ThemeFileName), value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The theme preference is cosmetic; failing to persist it must not break the app.
        }
    }
}
