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
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public const string CurrentSchema = "files-collector-preset-v1";

    /// <summary>Hand-edited files may contain comments and trailing commas.</summary>
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

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
                // Values are clamped to the ranges the importer accepts, so an export can always
                // be imported again.
                MaxFileSizeKiB = Math.Clamp(scanOptions.MaxFileSizeBytes / 1024, 1, PresetImportValidator.MaxFileSizeKiB),
                BinaryFileMode = scanOptions.BinaryFileMode,
                IncludePatterns = [.. scanOptions.IncludePatterns],
                ExcludePatterns = [.. scanOptions.ExcludePatterns],
                RedactRootPath = scanOptions.RedactRootPath,
                IncludeFileMetadataBlocks = scanOptions.IncludeFileMetadataBlocks,
                InventoryRefreshMinutes = Math.Clamp(scanOptions.InventoryRefreshMinutes, 0, PresetImportValidator.MaxInventoryRefreshMinutes),
                GitIgnorePath = string.IsNullOrWhiteSpace(scanOptions.GitIgnorePath) ? null : scanOptions.GitIgnorePath
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
            // Null checks below keep ToPreset safe even for data that did not go through
            // Deserialize (and therefore through PresetImportValidator).
            ExtensionRules = (Extensions ?? [])
                .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Extension))
                .Select(entry => new ExtensionRule(PresetImportValidator.NormalizeExtension(entry.Extension), entry.Enabled, entry.Mode))
                .ToList(),
            PathRules = (Paths ?? [])
                .Where(entry => entry is not null && entry.Path is not null)
                .Select(entry => new PathRule(RuleSet.NormalizeRelativePath(entry.Path.Trim()), entry.Type, entry.Mode))
                .ToList(),
            ScanOptions = new ScanOptions
            {
                IncludeAllExtensions = scan.IncludeAllExtensions,
                IncludeHidden = scan.IncludeHidden,
                IncludeSystem = scan.IncludeSystem,
                FollowReparsePoints = scan.FollowReparsePoints,
                MaxFileSizeBytes = Math.Clamp(scan.MaxFileSizeKiB, 1, PresetImportValidator.MaxFileSizeKiB) * 1024L,
                BinaryFileMode = scan.BinaryFileMode,
                IncludePatterns = CleanPatterns(scan.IncludePatterns),
                ExcludePatterns = CleanPatterns(scan.ExcludePatterns),
                RedactRootPath = scan.RedactRootPath,
                IncludeFileMetadataBlocks = scan.IncludeFileMetadataBlocks,
                InventoryRefreshMinutes = Math.Clamp(scan.InventoryRefreshMinutes, 0, PresetImportValidator.MaxInventoryRefreshMinutes),
                GitIgnorePath = string.IsNullOrWhiteSpace(scan.GitIgnorePath) ? string.Empty : scan.GitIgnorePath.Trim()
            },
            ScanRootPath = scanRootPath,
            PrefixPresetId = null
        };
    }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, SerializerOptions);
    }

    /// <summary>
    /// Parses and validates an export file. Every problem is reported through one
    /// <see cref="InvalidDataException"/> whose message lists each problem with its JSON
    /// path (see <see cref="PresetImportException.GetErrors"/>); no other exception type escapes for malformed input.
    /// </summary>
    public static PresetExportData Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            if (!HasCurrentSchemaTag(document.RootElement))
            {
                throw PresetImportException.Create(
                    $"The file is not a Files Collector preset export: the \"$schema\" tag is missing or unknown (expected \"{CurrentSchema}\").",
                    [new PresetImportError("$.$schema", $"expected \"{CurrentSchema}\".")]);
            }

            var errors = PresetImportValidator.Validate(document.RootElement);
            if (errors.Count > 0)
            {
                throw PresetImportException.FromErrors(errors);
            }

            var data = document.Deserialize<PresetExportData>(SerializerOptions)
                ?? throw PresetImportException.Create("The preset file does not contain a preset.", [new PresetImportError("$", "the value is null.")]);
            data.Name ??= string.Empty;
            data.Scan ??= new ScanSection();
            data.Extensions ??= [];
            data.Paths ??= [];
            return data;
        }
        catch (JsonException exception)
        {
            var position = exception.LineNumber is { } line
                ? $" (line {line + 1}, position {(exception.BytePositionInLine ?? 0) + 1})"
                : string.Empty;
            throw PresetImportException.Create(
                $"The preset file is not valid JSON{position}: {exception.Message}",
                [new PresetImportError(exception.Path ?? "$", exception.Message)],
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw PresetImportException.Create($"The preset file cannot be read: {exception.Message}", [new PresetImportError("$", exception.Message)], exception);
        }
    }

    private static List<string> CleanPatterns(List<string>? patterns)
    {
        return (patterns ?? [])
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        /// <summary>
        /// Optional path of a .gitignore file whose rules hide matching files and
        /// folders. Omitted from the export file when the option is not used.
        /// </summary>
        public string? GitIgnorePath { get; set; }
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
