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
            items.Add(CreatePlanItem(file, ruleSet, extensionRules, scanOptions));
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
        extensionCounts[extension] = extensionCounts.TryGetValue(extension, out var count) ? count + 1 : 1;
        var resolution = ruleSet.Resolve(relativePath, PathRuleKind.File);
        var extensionRule = extensionRules.LastOrDefault(rule => string.Equals(NormalizeExtension(rule.Extension), extension, StringComparison.OrdinalIgnoreCase));
        var explicitFileRule = resolution.Source == RuleSource.Local;
        var mode = resolution.Mode;
        string? reason = null;

        if (!entry.IsAccessible)
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
        else if (entry.IsHidden && !scanOptions.IncludeHidden)
        {
            mode = CollectionMode.Excluded;
            reason = "hidden_file";
        }
        else if (entry.IsSystem && !scanOptions.IncludeSystem)
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

        if (mode != CollectionMode.Excluded && entry.SizeBytes is > 0 && entry.SizeBytes > scanOptions.MaxFileSizeBytes)
        {
            mode = CollectionMode.Listed;
            reason = "size_limit";
        }
        else if (mode != CollectionMode.Excluded && BinaryExtensions.Contains(extension))
        {
            mode = scanOptions.BinaryFileMode;
            reason = "binary_extension";
        }

        items.Add(new CollectionPlanItem(entry.FullPath, relativePath, mode, entry.SizeBytes, reason));
    }

    private static CollectionPlanItem CreatePlanItem(FileInventoryEntry file, RuleSet ruleSet, IReadOnlyList<ExtensionRule> extensionRules, ScanOptions scanOptions)
    {
        var extension = file.Extension;
        var resolution = ruleSet.Resolve(file.RelativePath, PathRuleKind.File);
        var extensionRule = extensionRules.LastOrDefault(rule => string.Equals(NormalizeExtension(rule.Extension), extension, StringComparison.OrdinalIgnoreCase));
        var explicitFileRule = resolution.Source == RuleSource.Local;
        var mode = resolution.Mode;
        string? reason = null;

        if (!file.IsAccessible)
        {
            mode = CollectionMode.Listed;
            reason = "file_unavailable";
        }
        else if (GlobMatcher.IsMatch(file.RelativePath, scanOptions.ExcludePatterns))
        {
            mode = CollectionMode.Excluded;
            reason = "excluded_pattern";
        }
        else if (scanOptions.IncludePatterns.Count > 0 && !GlobMatcher.IsMatch(file.RelativePath, scanOptions.IncludePatterns))
        {
            mode = CollectionMode.Excluded;
            reason = "not_included_pattern";
        }
        else if (file.IsHidden && !scanOptions.IncludeHidden)
        {
            mode = CollectionMode.Excluded;
            reason = "hidden_file";
        }
        else if (file.IsSystem && !scanOptions.IncludeSystem)
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

        if (mode != CollectionMode.Excluded && file.SizeBytes is > 0 && file.SizeBytes > scanOptions.MaxFileSizeBytes)
        {
            mode = CollectionMode.Listed;
            reason = "size_limit";
        }
        else if (mode != CollectionMode.Excluded && BinaryExtensions.Contains(extension))
        {
            mode = scanOptions.BinaryFileMode;
            reason = "binary_extension";
        }

        return new CollectionPlanItem(file.FullPath, file.RelativePath, mode, file.SizeBytes, reason);
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
