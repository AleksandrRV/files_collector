using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Presets;

public sealed class ScanOptions
{
    public bool IncludeAllExtensions { get; set; } = true;

    public bool IncludeHidden { get; set; }

    public bool IncludeSystem { get; set; }

    public bool FollowReparsePoints { get; set; }

    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    public CollectionMode BinaryFileMode { get; set; } = CollectionMode.Listed;

    public List<string> IncludePatterns { get; set; } = [];

    public List<string> ExcludePatterns { get; set; } = [];

    public bool RedactRootPath { get; set; }

    public bool IncludeFileMetadataBlocks { get; set; } = true;

    public int InventoryRefreshMinutes { get; set; } = 1;
}
