using System.Text.Json;
using System.Text.Json.Serialization;
using FilesCollector.Core;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using Microsoft.Extensions.Logging;

namespace FilesCollector.Infrastructure.Presets;

public sealed class JsonPresetRepository : IPresetRepository
{
    private const string IndexFileName = "index.json";
    private readonly IAppPaths _appPaths;
    private readonly ILogger<JsonPresetRepository> _logger;
    private readonly object _syncRoot = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonPresetRepository(IAppPaths appPaths, ILogger<JsonPresetRepository> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    public IReadOnlyList<Preset> GetAll()
    {
        lock (_syncRoot)
        {
            EnsureDefaultPreset();
            var presets = new List<Preset>();
            foreach (var item in ReadIndex())
            {
                var preset = ReadPreset(item.Id);
                if (preset is not null)
                {
                    presets.Add(preset);
                }
            }

            return presets
                .OrderBy(preset => preset.Id == PresetDefaults.DefaultPresetId ? 0 : 1)
                .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ClonePreset)
                .ToArray();
        }
    }

    public Preset? Get(Guid id)
    {
        lock (_syncRoot)
        {
            EnsureDefaultPreset();
            var preset = ReadPreset(id);
            return preset is null ? null : ClonePreset(preset);
        }
    }

    public void Save(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        lock (_syncRoot)
        {
            EnsureStorage();
            var existingPresets = LoadPresetsWithoutInitialization();
            var existing = existingPresets.FirstOrDefault(item => item.Id == preset.Id);
            var now = DateTimeOffset.Now;
            var normalized = ClonePreset(preset);

            if (normalized.Id == Guid.Empty)
            {
                normalized.Id = Guid.NewGuid();
            }

            normalized.SchemaVersion = Preset.CurrentSchemaVersion;
            normalized.Name = normalized.Name.Trim();
            normalized.CreatedAt = existing?.CreatedAt ?? (normalized.CreatedAt == default ? now : normalized.CreatedAt);
            normalized.UpdatedAt = now;

            if (normalized.Id == PresetDefaults.DefaultPresetId)
            {
                normalized.Name = "Default";
            }

            var validationError = PresetNameValidator.Validate(
                normalized.Name,
                existingPresets.Select(item => new PresetListItem(item.Id, item.Name, item.Id == PresetDefaults.DefaultPresetId)),
                normalized.Id);
            if (validationError is not null)
            {
                throw new ArgumentException(validationError, nameof(preset));
            }

            normalized.PathRules ??= [];
            normalized.ExtensionRules ??= [];
            normalized.ScanOptions ??= new ScanOptions();
            normalized.ScanOptions.IncludePatterns ??= [];
            normalized.ScanOptions.ExcludePatterns ??= [];
            normalized.PathRules = normalized.PathRules
                .Select(rule => rule with { RelativePath = RuleSet.NormalizeRelativePath(rule.RelativePath) })
                .ToList();

            WriteJsonAtomically(GetPresetPath(normalized.Id), normalized);
            var index = ReadIndex();
            var existingIndex = index.FindIndex(item => item.Id == normalized.Id);
            var indexItem = new PresetIndexItem(normalized.Id, normalized.Name);
            if (existingIndex >= 0)
            {
                index[existingIndex] = indexItem;
            }
            else
            {
                index.Add(indexItem);
            }

            WriteJsonAtomically(GetIndexPath(), index);
        }
    }

    public bool Delete(Guid id)
    {
        if (id == PresetDefaults.DefaultPresetId)
        {
            return false;
        }

        lock (_syncRoot)
        {
            EnsureDefaultPreset();
            var presetPath = GetPresetPath(id);
            if (!File.Exists(presetPath))
            {
                return false;
            }

            File.Delete(presetPath);
            var index = ReadIndex();
            index.RemoveAll(item => item.Id == id);
            WriteJsonAtomically(GetIndexPath(), index);
            return true;
        }
    }

    private void EnsureDefaultPreset()
    {
        EnsureStorage();
        if (ReadPreset(PresetDefaults.DefaultPresetId) is null)
        {
            Save(PresetDefaults.CreateDefault(DateTimeOffset.Now));
        }
    }

    private void EnsureStorage()
    {
        Directory.CreateDirectory(_appPaths.PresetsDirectory);
    }

    private List<Preset> LoadPresetsWithoutInitialization()
    {
        return ReadIndex()
            .Select(item => ReadPreset(item.Id))
            .Where(preset => preset is not null)
            .Cast<Preset>()
            .ToList();
    }

    private List<PresetIndexItem> ReadIndex()
    {
        EnsureStorage();
        var indexPath = GetIndexPath();
        if (!File.Exists(indexPath))
        {
            var rebuiltIndex = RebuildIndex();
            WriteJsonAtomically(indexPath, rebuiltIndex);
            return rebuiltIndex;
        }

        try
        {
            var index = JsonSerializer.Deserialize<List<PresetIndexItem>>(File.ReadAllText(indexPath), _jsonOptions);
            return index ?? [];
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The preset index is invalid and will be rebuilt.");
            var rebuiltIndex = RebuildIndex();
            WriteJsonAtomically(indexPath, rebuiltIndex);
            return rebuiltIndex;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The preset index could not be read.");
            return [];
        }
    }

    private List<PresetIndexItem> RebuildIndex()
    {
        return Directory.EnumerateFiles(_appPaths.PresetsDirectory, "*.json")
            .Where(path => !string.Equals(Path.GetFileName(path), IndexFileName, StringComparison.OrdinalIgnoreCase))
            .Select(path => TryReadPreset(path))
            .Where(preset => preset is not null)
            .Cast<Preset>()
            .Select(preset => new PresetIndexItem(preset.Id, preset.Name))
            .DistinctBy(item => item.Id)
            .ToList();
    }

    private Preset? ReadPreset(Guid id)
    {
        return TryReadPreset(GetPresetPath(id));
    }

    private Preset? TryReadPreset(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var preset = JsonSerializer.Deserialize<Preset>(File.ReadAllText(path), _jsonOptions);
            if (preset is null || preset.Id == Guid.Empty || preset.SchemaVersion > Preset.CurrentSchemaVersion)
            {
                _logger.LogWarning("The preset file {PresetPath} is invalid or uses an unsupported schema.", path);
                return null;
            }

            preset.SchemaVersion = Preset.CurrentSchemaVersion;
            preset.ExtensionRules ??= [];
            preset.PathRules ??= [];
            preset.ScanOptions ??= new ScanOptions();
            preset.ScanOptions.IncludePatterns ??= [];
            preset.ScanOptions.ExcludePatterns ??= [];
            return preset;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The preset file {PresetPath} is invalid.", path);
            return null;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The preset file {PresetPath} could not be read.", path);
            return null;
        }
    }

    private void WriteJsonAtomically<T>(string path, T value)
    {
        var temporaryPath = path + ".tmp";
        var content = JsonSerializer.Serialize(value, _jsonOptions);
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    private Preset ClonePreset(Preset preset)
    {
        return JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(preset, _jsonOptions), _jsonOptions)
            ?? throw new InvalidOperationException("The preset could not be cloned.");
    }

    private string GetIndexPath()
    {
        return Path.Combine(_appPaths.PresetsDirectory, IndexFileName);
    }

    private string GetPresetPath(Guid id)
    {
        return Path.Combine(_appPaths.PresetsDirectory, $"{id:N}.json");
    }

    private sealed record PresetIndexItem(Guid Id, string Name);
}
