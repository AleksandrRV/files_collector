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

    public FileTreeNode? Parent { get; internal set; }

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

    [ObservableProperty]
    private bool isExpandedState;

    [ObservableProperty]
    private bool isMultiSelected;

    [ObservableProperty]
    private string? planReason;

    [ObservableProperty]
    private long? planSizeBytes;

    [ObservableProperty]
    private string sourceGlyph = "·";

    [ObservableProperty]
    private string sourceDescription = "Global default mode.";

    [ObservableProperty]
    private string sizeDisplay = string.Empty;

    public bool HasLocalRule => RuleSource == RuleSource.Local;

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

    public bool IsProblem => !IsAccessible && !IsPlaceholder;

    public string IconText => IsPlaceholder ? "…" : IsDirectory ? "Folder" : "File";

    public string IconGlyph => IsPlaceholder ? "…" : IsDirectory ? "\uE8B7" : "\uE8A5";

    public bool IsLargeFile => PlanSizeBytes is { } size && size > 1024 * 1024;

    public bool IsBinaryFile => BinaryExtensions.IsBinary(Path.GetExtension(DisplayName).ToLowerInvariant());

    public bool HasExtension => !string.IsNullOrEmpty(Path.GetExtension(DisplayName));

    public string StatusText =>
        IsPlaceholder ? "Loading" :
        !IsAccessible ? "Unavailable" :
        IsReparsePoint ? "Reparse point" :
        IsDirectory ? "Folder" : "File";

    public string ToolTipText => !string.IsNullOrWhiteSpace(AccessError)
        ? AccessError
        : IsReparsePoint
            ? "Reparse points are not traversed."
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

    public void NotifyDerivedPropertiesChanged()
    {
        OnPropertyChanged(nameof(EffectiveModeText));
        OnPropertyChanged(nameof(RuleSourceText));
        OnPropertyChanged(nameof(HasLocalRule));
        OnPropertyChanged(nameof(IsDimmed));
    }

    public void ApplyRuleResolution(RuleResolution resolution)
    {
        EffectiveMode = resolution.Mode;
        RuleSource = resolution.Source;
        OnPropertyChanged(nameof(EffectiveModeText));
        OnPropertyChanged(nameof(RuleSourceText));
        OnPropertyChanged(nameof(HasLocalRule));
        OnPropertyChanged(nameof(IsDimmed));
    }

    public void MarkChildrenUnloaded()
    {
        if (!IsDirectory || !IsAccessible || IsReparsePoint)
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
        OnPropertyChanged(nameof(IsProblem));
    }
}
