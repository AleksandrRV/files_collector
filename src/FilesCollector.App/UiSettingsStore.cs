using System.Text.Json;
using FilesCollector.Core;

namespace FilesCollector.App;

public sealed class UiSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _filePath;
    private UiSettings _settings;

    public UiSettingsStore(IAppPaths appPaths)
    {
        Directory.CreateDirectory(appPaths.LocalDataDirectory);
        _filePath = Path.Combine(appPaths.LocalDataDirectory, "ui-settings.json");
        _settings = Load();
    }

    public UiSettings Settings => _settings;

    public void Save(UiSettings settings)
    {
        _settings = settings;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch (IOException)
        {
            // Persisting UI preferences is best-effort.
        }
    }

    private UiSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(_filePath), SerializerOptions) ?? new UiSettings(ThemeMode.System, DensityMode.Comfortable, false);
            }
        }
        catch (JsonException)
        {
            // Fall through to defaults.
        }

        return new UiSettings(ThemeMode.System, DensityMode.Comfortable, false);
    }
}
