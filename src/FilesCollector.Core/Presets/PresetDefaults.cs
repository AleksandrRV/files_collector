using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Presets;

public static class PresetDefaults
{
    public static readonly Guid DefaultPresetId = new("00000000-0000-0000-0000-000000000001");

    public static Preset CreateDefault(DateTimeOffset now)
    {
        return new Preset
        {
            Id = DefaultPresetId,
            Name = "Default",
            CreatedAt = now,
            UpdatedAt = now,
            GlobalMode = CollectionMode.Full,
            ExtensionRules = [],
            PathRules = [],
            ScanOptions = new ScanOptions()
        };
    }
}
