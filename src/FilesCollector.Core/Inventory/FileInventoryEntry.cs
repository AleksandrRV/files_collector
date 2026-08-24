namespace FilesCollector.Core.Inventory;

public sealed record FileInventoryEntry(
    string FullPath,
    string RelativePath,
    string Extension,
    long? SizeBytes,
    bool IsHidden,
    bool IsSystem,
    bool IsAccessible,
    string? AccessError);
