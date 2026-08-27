using System.Diagnostics.CodeAnalysis;
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
            foreach (var item in ReadIndexItems())
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
            normalized.ScanOptions.GitIgnorePath = NormalizeGitIgnorePath(normalized.ScanOptions.GitIgnorePath);
            normalized.PathRules = normalized.PathRules
                .Select(rule => rule with { RelativePath = RuleSet.NormalizeRelativePath(rule.RelativePath) })
                .ToList();

            WriteJsonAtomically(GetPresetPath(normalized.Id), normalized);
            List<PresetIndexItem> index;
            if (!TryReadIndex(out var readIndex))
            {
                // The index is temporarily unreadable; rebuild it from the preset files
                // instead of overwriting it with a partial list that would hide presets.
                _logger.LogWarning("The preset index could not be read; rebuilding it from the preset files.");
                index = RebuildIndex();
            }
            else
            {
                index = readIndex;
            }

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
            List<PresetIndexItem> index;
            if (!TryReadIndex(out var readIndex))
            {
                _logger.LogWarning("The preset index could not be read; rebuilding it from the preset files.");
                index = RebuildIndex();
            }
            else
            {
                index = readIndex;
            }

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
        return ReadIndexItems()
            .Select(item => ReadPreset(item.Id))
            .Where(preset => preset is not null)
            .Cast<Preset>()
            .ToList();
    }

    private bool TryReadIndex([NotNullWhen(true)] out List<PresetIndexItem>? index)
    {
        index = null;
        EnsureStorage();
        var indexPath = GetIndexPath();
        if (!File.Exists(indexPath))
        {
            var rebuiltIndex = RebuildIndex();
            WriteJsonAtomically(indexPath, rebuiltIndex);
            index = rebuiltIndex;
            return true;
        }

        try
        {
            index = JsonSerializer.Deserialize<List<PresetIndexItem>>(File.ReadAllText(indexPath), _jsonOptions) ?? [];
            return true;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The preset index is invalid and will be rebuilt.");
            var rebuiltIndex = RebuildIndex();
            WriteJsonAtomically(indexPath, rebuiltIndex);
            index = rebuiltIndex;
            return true;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The preset index could not be read.");
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Access to the preset index was denied.");
            return false;
        }
    }

    private List<PresetIndexItem> ReadIndexItems()
    {
        return TryReadIndex(out var index) ? index : [];
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
            preset.ScanOptions.GitIgnorePath = NormalizeGitIgnorePath(preset.ScanOptions.GitIgnorePath);
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
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Access to the preset file {PresetPath} was denied.", path);
            return null;
        }
    }

    private static string NormalizeGitIgnorePath(string? gitIgnorePath)
    {
        return string.IsNullOrWhiteSpace(gitIgnorePath) ? string.Empty : gitIgnorePath.Trim();
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
