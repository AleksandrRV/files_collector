using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Planning;

public sealed class CollectionPlanner
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".bmp", ".dll", ".exe", ".gif", ".ico", ".jar", ".jpeg", ".jpg", ".pdf", ".png", ".zip"
    };

    private readonly IFileSystem _fileSystem;

    public CollectionPlanner(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public CollectionPlan CreatePlan(FileInventorySnapshot inventory, RuleSet ruleSet, IReadOnlyList<ExtensionRule> extensionRules, ScanOptions scanOptions)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(extensionRules);
        ArgumentNullException.ThrowIfNull(scanOptions);

        var items = new List<CollectionPlanItem>(inventory.Files.Count);
        foreach (var file in inventory.Files)
        {
            items.Add(BuildItem(
                file.FullPath,
                file.RelativePath,
                file.Extension,
                file.SizeBytes,
                file.IsAccessible,
                file.IsHidden,
                file.IsSystem,
                ruleSet,
                extensionRules,
                scanOptions,
                extensionCountsToUpdate: null));
        }

        return new CollectionPlan(items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), inventory.ExtensionCounts);
    }

    public CollectionPlan CreatePlan(string rootPath, string? excludedDirectoryPath, RuleSet ruleSet, IReadOnlyList<ExtensionRule> extensionRules, ScanOptions scanOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(extensionRules);
        ArgumentNullException.ThrowIfNull(scanOptions);

        var items = new List<CollectionPlanItem>();
        var extensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var normalizedRootPath = Path.GetFullPath(rootPath);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VisitDirectory(normalizedRootPath, normalizedRootPath, excludedDirectoryPath, ruleSet, extensionRules, scanOptions, items, extensionCounts, visitedDirectories);
        return new CollectionPlan(items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), extensionCounts);
    }

    private void VisitDirectory(
        string rootPath,
        string directoryPath,
        string? excludedDirectoryPath,
        RuleSet ruleSet,
        IReadOnlyList<ExtensionRule> extensionRules,
        ScanOptions scanOptions,
        List<CollectionPlanItem> items,
        Dictionary<string, int> extensionCounts,
        HashSet<string> visitedDirectories)
    {
        if (!visitedDirectories.Add(Path.GetFullPath(directoryPath)))
        {
            return;
        }

        var result = _fileSystem.GetChildren(directoryPath, excludedDirectoryPath);
        foreach (var entry in result.Entries)
        {
            var relativePath = RuleSet.NormalizeRelativePath(Path.GetRelativePath(rootPath, entry.FullPath));
            ProcessEntry(entry, relativePath, ruleSet, extensionRules, scanOptions, items, extensionCounts);

            if (entry.Kind == EntryKind.Directory && entry.IsAccessible && (scanOptions.FollowReparsePoints || !entry.IsReparsePoint))
            {
                VisitDirectory(rootPath, entry.FullPath, excludedDirectoryPath, ruleSet, extensionRules, scanOptions, items, extensionCounts, visitedDirectories);
            }
        }
    }

    private static void ProcessEntry(
        FileSystemEntry entry,
        string relativePath,
        RuleSet ruleSet,
        IReadOnlyList<ExtensionRule> extensionRules,
        ScanOptions scanOptions,
        List<CollectionPlanItem> items,
        Dictionary<string, int> extensionCounts)
    {
        if (entry.Kind != EntryKind.File)
        {
            return;
        }

        var extension = GetExtension(entry.Name);
        items.Add(BuildItem(
            entry.FullPath,
            relativePath,
            extension,
            entry.SizeBytes,
            entry.IsAccessible,
            entry.IsHidden,
            entry.IsSystem,
            ruleSet,
            extensionRules,
            scanOptions,
            extensionCounts));
    }

    private static CollectionPlanItem BuildItem(
        string fullPath,
        string relativePath,
        string extension,
        long? sizeBytes,
        bool isAccessible,
        bool isHidden,
        bool isSystem,
        RuleSet ruleSet,
        IReadOnlyList<ExtensionRule> extensionRules,
        ScanOptions scanOptions,
        Dictionary<string, int>? extensionCountsToUpdate)
    {
        if (extensionCountsToUpdate is not null)
        {
            extensionCountsToUpdate[extension] = extensionCountsToUpdate.TryGetValue(extension, out var count) ? count + 1 : 1;
        }

        var resolution = ruleSet.Resolve(relativePath, PathRuleKind.File);
        var extensionRule = extensionRules.LastOrDefault(rule => string.Equals(NormalizeExtension(rule.Extension), extension, StringComparison.OrdinalIgnoreCase));
        var explicitFileRule = resolution.Source == RuleSource.Local;
        var mode = resolution.Mode;
        string? reason = null;

        if (!isAccessible)
        {
            mode = CollectionMode.Listed;
            reason = "file_unavailable";
        }
        else if (GlobMatcher.IsMatch(relativePath, scanOptions.ExcludePatterns))
        {
            mode = CollectionMode.Excluded;
            reason = "excluded_pattern";
        }
        else if (scanOptions.IncludePatterns.Count > 0 && !GlobMatcher.IsMatch(relativePath, scanOptions.IncludePatterns))
        {
            mode = CollectionMode.Excluded;
            reason = "not_included_pattern";
        }
        else if (isHidden && !scanOptions.IncludeHidden)
        {
            mode = CollectionMode.Excluded;
            reason = "hidden_file";
        }
        else if (isSystem && !scanOptions.IncludeSystem)
        {
            mode = CollectionMode.Excluded;
            reason = "system_file";
        }
        else if (!IsExtensionAllowed(extensionRule, scanOptions) && !explicitFileRule)
        {
            mode = CollectionMode.Excluded;
            reason = "extension_disabled";
        }
        else if (resolution.Source == RuleSource.Global && extensionRule is { Enabled: true })
        {
            mode = extensionRule.Mode;
        }

        if (mode != CollectionMode.Excluded && sizeBytes is > 0 && sizeBytes > scanOptions.MaxFileSizeBytes)
        {
            mode = CollectionMode.Listed;
            reason = "size_limit";
        }
        else if (mode != CollectionMode.Excluded && BinaryExtensions.Contains(extension))
        {
            mode = scanOptions.BinaryFileMode;
            reason = "binary_extension";
        }

        return new CollectionPlanItem(fullPath, relativePath, mode, sizeBytes, reason);
    }

    private static bool IsExtensionAllowed(ExtensionRule? extensionRule, ScanOptions scanOptions)
    {
        if (extensionRule is not null)
        {
            return extensionRule.Enabled;
        }

        return scanOptions.IncludeAllExtensions;
    }

    private static string GetExtension(string name)
    {
        var extension = Path.GetExtension(name);
        return string.IsNullOrEmpty(extension) ? "[no extension]" : extension.ToLowerInvariant();
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.Equals(extension, "[no extension]", StringComparison.OrdinalIgnoreCase))
        {
            return extension;
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }
}
