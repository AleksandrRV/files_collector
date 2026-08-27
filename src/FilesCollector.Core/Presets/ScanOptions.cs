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

    /// <summary>
    /// Path of the .gitignore file whose rules hide matching files and folders from
    /// the tree, the report and every statistic. An empty value disables the option.
    /// </summary>
    public string GitIgnorePath { get; set; } = string.Empty;
}
