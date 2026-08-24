using System.Text.Json;
using FilesCollector.Core;
using FilesCollector.Core.Settings;

namespace FilesCollector.Infrastructure.Settings;

public sealed class JsonAppSessionStore : IAppSessionStore
{
    private readonly IAppPaths _appPaths;
    private readonly object _syncRoot = new();

    public JsonAppSessionStore(IAppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public Guid? GetLastPresetId()
    {
        lock (_syncRoot)
        {
            try
            {
                if (!File.Exists(GetPath()))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(GetPath()))?.LastPresetId;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public void SetLastPresetId(Guid presetId)
    {
        lock (_syncRoot)
        {
            try
            {
                Directory.CreateDirectory(_appPaths.LocalDataDirectory);
                var path = GetPath();
                var temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new SessionData { LastPresetId = presetId }), new UTF8Encoding(false));
                File.Move(temporaryPath, path, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string GetPath()
    {
        return Path.Combine(_appPaths.LocalDataDirectory, "settings.json");
    }

    private sealed class SessionData
    {
        public Guid? LastPresetId { get; set; }
    }
}
