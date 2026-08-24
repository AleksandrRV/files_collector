using System.Text.Json;
using FilesCollector.Core;
using FilesCollector.Core.Prefixes;
using Microsoft.Extensions.Logging;

namespace FilesCollector.Infrastructure.Prefixes;

public sealed class JsonPrefixPresetRepository : IPrefixPresetRepository
{
    private readonly IAppPaths _appPaths;
    private readonly ILogger<JsonPrefixPresetRepository> _logger;
    private readonly object _syncRoot = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonPrefixPresetRepository(IAppPaths appPaths, ILogger<JsonPrefixPresetRepository> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    public IReadOnlyList<PrefixPreset> GetAll()
    {
        lock (_syncRoot)
        {
            EnsureStorage();
            return Directory.EnumerateFiles(_appPaths.PrefixesDirectory, "*.json")
                .Select(TryRead)
                .Where(preset => preset is not null)
                .Cast<PrefixPreset>()
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToArray();
        }
    }

    public PrefixPreset? Get(Guid id)
    {
        lock (_syncRoot)
        {
            EnsureStorage();
            var preset = TryRead(GetPath(id));
            return preset is null ? null : Clone(preset);
        }
    }

    public void Save(PrefixPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        lock (_syncRoot)
        {
            EnsureStorage();
            var existingPresets = GetAll();
            var existing = existingPresets.FirstOrDefault(item => item.Id == preset.Id);
            var normalized = Clone(preset);
            var now = DateTimeOffset.Now;

            if (normalized.Id == Guid.Empty)
            {
                normalized.Id = Guid.NewGuid();
            }

            normalized.SchemaVersion = PrefixPreset.CurrentSchemaVersion;
            normalized.Name ??= string.Empty;
            normalized.Content ??= string.Empty;
            normalized.Name = normalized.Name.Trim();
            normalized.CreatedAt = existing?.CreatedAt ?? (normalized.CreatedAt == default ? now : normalized.CreatedAt);
            normalized.UpdatedAt = now;
            var validationError = PrefixPresetNameValidator.Validate(
                normalized.Name,
                existingPresets.Select(item => new PrefixPresetListItem(item.Id, item.Name)),
                normalized.Id);
            if (validationError is not null)
            {
                throw new ArgumentException(validationError, nameof(preset));
            }

            WriteAtomically(GetPath(normalized.Id), normalized);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_syncRoot)
        {
            EnsureStorage();
            var path = GetPath(id);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
    }

    private void EnsureStorage()
    {
        Directory.CreateDirectory(_appPaths.PrefixesDirectory);
    }

    private PrefixPreset? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var preset = JsonSerializer.Deserialize<PrefixPreset>(File.ReadAllText(path), _jsonOptions);
            if (preset is null || preset.Id == Guid.Empty || preset.SchemaVersion > PrefixPreset.CurrentSchemaVersion)
            {
                _logger.LogWarning("The prefix preset file {PrefixPresetPath} is invalid or uses an unsupported schema.", path);
                return null;
            }

            preset.SchemaVersion = PrefixPreset.CurrentSchemaVersion;
            preset.Name ??= string.Empty;
            preset.Content ??= string.Empty;
            return preset;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The prefix preset file {PrefixPresetPath} is invalid.", path);
            return null;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The prefix preset file {PrefixPresetPath} could not be read.", path);
            return null;
        }
    }

    private void WriteAtomically(string path, PrefixPreset preset)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preset, _jsonOptions), new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    private PrefixPreset Clone(PrefixPreset preset)
    {
        return JsonSerializer.Deserialize<PrefixPreset>(JsonSerializer.Serialize(preset, _jsonOptions), _jsonOptions)
            ?? throw new InvalidOperationException("The prefix preset could not be cloned.");
    }

    private string GetPath(Guid id)
    {
        return Path.Combine(_appPaths.PrefixesDirectory, $"{id:N}.json");
    }
}
