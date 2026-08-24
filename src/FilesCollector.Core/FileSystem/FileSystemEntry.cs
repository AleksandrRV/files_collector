namespace FilesCollector.Core.FileSystem;

public sealed record FileSystemEntry(
    string FullPath,
    string Name,
    EntryKind Kind,
    bool IsReparsePoint,
    bool IsHidden,
    bool IsSystem,
    long? SizeBytes,
    bool IsAccessible,
    string? AccessError);
