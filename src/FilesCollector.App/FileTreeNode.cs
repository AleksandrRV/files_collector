using CommunityToolkit.Mvvm.ComponentModel;
using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Rules;

namespace FilesCollector.App;

public sealed partial class FileTreeNode : ObservableObject
{
    private FileTreeNode(
        string fullPath,
        string relativePath,
        string displayName,
        EntryKind kind,
        bool isReparsePoint,
        bool isAccessible,
        string? accessError,
        bool isPlaceholder)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        DisplayName = displayName;
        Kind = kind;
        IsReparsePoint = isReparsePoint;
        IsAccessible = isAccessible;
        AccessError = accessError;
        IsPlaceholder = isPlaceholder;
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    public string DisplayName { get; }

    public EntryKind Kind { get; }

    public bool IsDirectory => Kind == EntryKind.Directory;

    public bool IsReparsePoint { get; }

    public bool IsPlaceholder { get; }

    public ObservableCollection<FileTreeNode> Children { get; } = [];

    [ObservableProperty]
    private bool isAccessible;

    [ObservableProperty]
    private string? accessError;

    [ObservableProperty]
    private bool hasUnloadedChildren;

    [ObservableProperty]
    private bool areChildrenLoaded;

    [ObservableProperty]
    private bool isVisible = true;

    [ObservableProperty]
    private CollectionMode effectiveMode = CollectionMode.Full;

    [ObservableProperty]
    private RuleSource ruleSource = RuleSource.Global;

    /// <summary>Mode given by the rules alone (path, inherited, default).</summary>
    [ObservableProperty]
    private CollectionMode ruleMode = CollectionMode.Full;

    /// <summary>
    /// Why the plan gives the file another mode than its rules (hidden file, pattern,
    /// size limit, binary content, ...); <c>null</c> when the rules decide.
    /// </summary>
    [ObservableProperty]
    private string? planReason;

    public bool HasLocalRule => RuleSource == RuleSource.Local;

    /// <summary><c>true</c> when a filter, not a rule, decided the effective mode.</summary>
    public bool IsDecidedByFilter => PlanReason is not null && EffectiveMode != RuleMode;

    /// <summary>Text of the source column: the rule source, or "Filter" when a filter decided.</summary>
    public string SourceDisplayText => IsDecidedByFilter ? "Filter" : RuleSourceText;

    public string EffectiveModeText => EffectiveMode switch
    {
        CollectionMode.Full => "Full",
        CollectionMode.Signatures => "Signatures",
        CollectionMode.Listed => "Listed",
        CollectionMode.Excluded => "Excluded",
        _ => throw new ArgumentOutOfRangeException()
    };

    public string RuleSourceText => RuleSource switch
    {
        RuleSource.System => "System",
        RuleSource.Local => "Local",
        RuleSource.Inherited => "Inherited",
        RuleSource.Global => "Global",
        _ => throw new ArgumentOutOfRangeException()
    };

    public bool IsDimmed => !IsAccessible || IsReparsePoint || IsPlaceholder || EffectiveMode == CollectionMode.Excluded;

    public string IconText => IsPlaceholder ? "…" : IsDirectory ? "Folder" : "File";

    public string StatusText =>
        IsPlaceholder ? "Loading" :
        !IsAccessible ? "Unavailable" :
        IsReparsePoint ? "Reparse point" :
        IsDirectory ? "Folder" : "File";

    public string ToolTipText => !string.IsNullOrWhiteSpace(AccessError)
        ? AccessError
        : IsReparsePoint
            ? $"{FullPath}{Environment.NewLine}Reparse point (symbolic link or junction); it is traversed only when \"Follow reparse points\" is enabled."
            : PlanReason is not null
                ? $"{FullPath}{Environment.NewLine}{EffectiveModeText}: {PlanReason}"
                : FullPath;

    public static FileTreeNode FromEntry(FileSystemEntry entry, string relativePath)
    {
        return new FileTreeNode(
            entry.FullPath,
            relativePath,
            entry.Name,
            entry.Kind,
            entry.IsReparsePoint,
            entry.IsAccessible,
            entry.AccessError,
            false);
    }

    public static FileTreeNode CreateRoot(string rootPath)
    {
        return new FileTreeNode(rootPath, string.Empty, rootPath, EntryKind.Directory, false, true, null, false);
    }

    public static FileTreeNode CreatePlaceholder()
    {
        return new FileTreeNode(string.Empty, string.Empty, "Loading...", EntryKind.File, false, true, null, true);
    }

    /// <summary>
    /// Shows the rule resolution and, for files, the outcome of the plan. The effective
    /// mode is the plan's mode when the file is in the plan, so the tree shows what the
    /// report will contain; otherwise it is the rule mode.
    /// </summary>
    public void ApplyResolution(RuleResolution resolution, CollectionMode? plannedMode = null, string? plannedReason = null)
    {
        RuleMode = resolution.Mode;
        RuleSource = resolution.Source;
        EffectiveMode = plannedMode ?? resolution.Mode;
        PlanReason = plannedMode is null ? null : plannedReason;
        OnPropertyChanged(nameof(EffectiveModeText));
        OnPropertyChanged(nameof(RuleSourceText));
        OnPropertyChanged(nameof(HasLocalRule));
        OnPropertyChanged(nameof(IsDimmed));
        OnPropertyChanged(nameof(IsDecidedByFilter));
        OnPropertyChanged(nameof(SourceDisplayText));
        OnPropertyChanged(nameof(ToolTipText));
    }

    /// <param name="allowReparsePoint">Whether a reparse point may be expanded (Follow reparse points).</param>
    public void MarkChildrenUnloaded(bool allowReparsePoint = false)
    {
        if (!IsDirectory || !IsAccessible || (IsReparsePoint && !allowReparsePoint))
        {
            return;
        }

        AreChildrenLoaded = false;
        HasUnloadedChildren = true;
        Children.Clear();
        Children.Add(CreatePlaceholder());
    }

    public void MarkChildrenLoaded()
    {
        HasUnloadedChildren = false;
        AreChildrenLoaded = true;
    }

    public void MarkUnavailable(string error)
    {
        IsAccessible = false;
        AccessError = error;
        HasUnloadedChildren = false;
        AreChildrenLoaded = true;
        Children.Clear();
        OnPropertyChanged(nameof(IsDimmed));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToolTipText));
    }
}
