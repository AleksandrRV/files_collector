namespace FilesCollector.Core.Inventory;

public sealed class FileInventorySnapshot
{
    /// <summary>
    /// Version 2: hidden and system attributes of a directory are inherited by its
    /// descendants, and version-control metadata directories are not traversed. Caches
    /// written by version 1 are discarded and rebuilt.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string RootPath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Whether reparse points (symbolic links, junctions) were followed while the snapshot
    /// was built. A snapshot built with a different setting is refreshed.
    /// </summary>
    public bool FollowsReparsePoints { get; set; }

    public List<FileInventoryEntry> Files { get; set; } = [];

    public Dictionary<string, int> ExtensionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
