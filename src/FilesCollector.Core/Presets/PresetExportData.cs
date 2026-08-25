using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Presets;

/// <summary>
/// Human-friendly preset representation used for export/import files. The format is
/// meant to be reviewed and edited manually in any text editor: indented JSON, string
/// enum values, sizes in KiB, no machine-only fields (identifiers and timestamps are
/// assigned on import).
/// </summary>
public sealed class PresetExportData
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public const string CurrentSchema = "files-collector-preset-v1";

    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = CurrentSchema;

    public string Name { get; set; } = string.Empty;

    /// <summary>Collection mode applied to every item without an explicit rule.</summary>
    public CollectionMode DefaultMode { get; set; } = CollectionMode.Full;

    public ScanSection Scan { get; set; } = new();

    public List<ExtensionEntry> Extensions { get; set; } = [];

    public List<PathEntry> Paths { get; set; } = [];

    public static PresetExportData FromPreset(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var scanOptions = preset.ScanOptions ?? new ScanOptions();
        return new PresetExportData
        {
            Name = preset.Name.Trim(),
            DefaultMode = preset.GlobalMode,
            Scan = new ScanSection
            {
                IncludeAllExtensions = scanOptions.IncludeAllExtensions,
                IncludeHidden = scanOptions.IncludeHidden,
                IncludeSystem = scanOptions.IncludeSystem,
                FollowReparsePoints = scanOptions.FollowReparsePoints,
                MaxFileSizeKiB = Math.Max(1, scanOptions.MaxFileSizeBytes / 1024),
                BinaryFileMode = scanOptions.BinaryFileMode,
                IncludePatterns = [.. scanOptions.IncludePatterns],
                ExcludePatterns = [.. scanOptions.ExcludePatterns],
                RedactRootPath = scanOptions.RedactRootPath,
                IncludeFileMetadataBlocks = scanOptions.IncludeFileMetadataBlocks,
                InventoryRefreshMinutes = scanOptions.InventoryRefreshMinutes
            },
            Extensions = preset.ExtensionRules
                .Select(rule => new ExtensionEntry { Extension = rule.Extension, Enabled = rule.Enabled, Mode = rule.Mode })
                .ToList(),
            Paths = preset.PathRules
                .Select(rule => new PathEntry { Path = rule.RelativePath, Type = rule.Kind, Mode = rule.Mode })
                .ToList()
        };
    }

    public Preset ToPreset(string name, string scanRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var now = DateTimeOffset.Now;
        var scan = Scan ?? new ScanSection();
        return new Preset
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            GlobalMode = DefaultMode,
            ExtensionRules = Extensions
                .Select(entry => new ExtensionRule(entry.Extension.Trim(), entry.Enabled, entry.Mode))
                .ToList(),
            PathRules = Paths
                .Select(entry => new PathRule(RuleSet.NormalizeRelativePath(entry.Path), entry.Type, entry.Mode))
                .ToList(),
            ScanOptions = new ScanOptions
            {
                IncludeAllExtensions = scan.IncludeAllExtensions,
                IncludeHidden = scan.IncludeHidden,
                IncludeSystem = scan.IncludeSystem,
                FollowReparsePoints = scan.FollowReparsePoints,
                MaxFileSizeBytes = Math.Max(1, scan.MaxFileSizeKiB) * 1024L,
                BinaryFileMode = scan.BinaryFileMode,
                IncludePatterns = [.. scan.IncludePatterns],
                ExcludePatterns = [.. scan.ExcludePatterns],
                RedactRootPath = scan.RedactRootPath,
                IncludeFileMetadataBlocks = scan.IncludeFileMetadataBlocks,
                InventoryRefreshMinutes = scan.InventoryRefreshMinutes
            },
            ScanRootPath = scanRootPath,
            PrefixPresetId = null
        };
    }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, SerializerOptions);
    }

    public static PresetExportData Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        PresetExportData? data;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!HasCurrentSchemaTag(document.RootElement))
            {
                throw new InvalidDataException("The file is not a Files Collector preset export: the \"$schema\" tag is missing or unknown.");
            }

            data = document.Deserialize<PresetExportData>(SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The preset file is not valid JSON: {exception.Message}", exception);
        }

        if (data is null)
        {
            throw new InvalidDataException("The preset file does not contain a preset.");
        }

        data.Name ??= string.Empty;
        data.Scan ??= new ScanSection();
        data.Extensions ??= [];
        data.Paths ??= [];
        return data;
    }

    private static bool HasCurrentSchemaTag(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.EnumerateObject().Any(property =>
                string.Equals(property.Name, "$schema", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String &&
                string.Equals(property.Value.GetString(), CurrentSchema, StringComparison.OrdinalIgnoreCase));
    }

    public sealed class ScanSection
    {
        public bool IncludeAllExtensions { get; set; } = true;

        public bool IncludeHidden { get; set; }

        public bool IncludeSystem { get; set; }

        public bool FollowReparsePoints { get; set; }

        /// <summary>Maximum size of a collected file, in KiB (1 KiB = 1024 bytes).</summary>
        public long MaxFileSizeKiB { get; set; } = 5 * 1024;

        public CollectionMode BinaryFileMode { get; set; } = CollectionMode.Listed;

        public List<string> IncludePatterns { get; set; } = [];

        public List<string> ExcludePatterns { get; set; } = [];

        public bool RedactRootPath { get; set; }

        public bool IncludeFileMetadataBlocks { get; set; } = true;

        public int InventoryRefreshMinutes { get; set; } = 1;
    }

    public sealed class ExtensionEntry
    {
        public string Extension { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public CollectionMode Mode { get; set; } = CollectionMode.Full;
    }

    public sealed class PathEntry
    {
        public string Path { get; set; } = string.Empty;

        public PathRuleKind Type { get; set; } = PathRuleKind.Directory;

        public CollectionMode Mode { get; set; } = CollectionMode.Full;
    }
}