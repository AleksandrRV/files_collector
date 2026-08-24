using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Presets;

public sealed class Preset
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public CollectionMode GlobalMode { get; set; } = CollectionMode.Full;

    public List<ExtensionRule> ExtensionRules { get; set; } = [];

    public List<PathRule> PathRules { get; set; } = [];

    public ScanOptions ScanOptions { get; set; } = new();

    public string ScanRootPath { get; set; } = string.Empty;

    public Guid? PrefixPresetId { get; set; }
}
