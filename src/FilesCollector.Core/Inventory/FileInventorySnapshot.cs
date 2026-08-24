namespace FilesCollector.Core.Inventory;

public sealed class FileInventorySnapshot
{
    public int SchemaVersion { get; set; } = 1;

    public string RootPath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public List<FileInventoryEntry> Files { get; set; } = [];

    public Dictionary<string, int> ExtensionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
