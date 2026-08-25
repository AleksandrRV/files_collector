using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.App.History;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Rules;

namespace FilesCollector.App.Inspector;

public enum InspectorTab
{
    Details,
    Plan,
    Preview
}

public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly PlanViewModel _plan = new();

    public InspectorViewModel()
    {
        Plan = _plan;
    }

    public PlanViewModel Plan { get; }

    [ObservableProperty]
    private InspectorTab activeTab = InspectorTab.Plan;

    [ObservableProperty]
    private FileTreeNode? selectedNode;

    [ObservableProperty]
    private string nodeIconGlyph = string.Empty;

    [ObservableProperty]
    private string nodeName = string.Empty;

    [ObservableProperty]
    private string nodeRelativePath = string.Empty;

    [ObservableProperty]
    private string nodeSizeText = string.Empty;

    [ObservableProperty]
    private string nodeAttributes = string.Empty;

    [ObservableProperty]
    private string nodeAvailability = string.Empty;

    [ObservableProperty]
    private CollectionMode effectiveMode = CollectionMode.Full;

    [ObservableProperty]
    private string sourceGlyph = "·";

    [ObservableProperty]
    private string sourceDescription = string.Empty;

    [ObservableProperty]
    private string reasonText = string.Empty;

    [ObservableProperty]
    private bool hasLocalRule;

    [ObservableProperty]
    private bool isDirectory;

    public ObservableCollection<RuleStepInfo> RuleSteps { get; } = [];

    public ObservableCollection<PreviewLine> PreviewLines { get; } = [];

    [ObservableProperty]
    private bool previewShowExcluded;

    [ObservableProperty]
    private bool previewShowReasons = true;

    [ObservableProperty]
    private bool previewShowYaml = true;

    [RelayCommand]
    private void ApplyMode(CollectionMode mode)
    {
        ApplyModeRequested?.Invoke(this, mode);
    }

    [RelayCommand]
    private void ResetLocalRule()
    {
        ResetLocalRuleRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        RevealInExplorerRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void OpenFile()
    {
        OpenFileRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CopyPath()
    {
        CopyPathRequested?.Invoke(this, CopyPathKind.Full);
    }

    [RelayCommand]
    private void CopyRelativePath()
    {
        CopyPathRequested?.Invoke(this, CopyPathKind.Relative);
    }

    [RelayCommand]
    private void RevealInTree(string? relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            RevealInTreeRequested?.Invoke(this, relativePath);
        }
    }

    [RelayCommand]
    private void RefreshPreview()
    {
        PreviewRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<CollectionMode>? ApplyModeRequested;

    public event EventHandler? ResetLocalRuleRequested;

    public event EventHandler? RevealInExplorerRequested;

    public event EventHandler? OpenFileRequested;

    public event EventHandler<CopyPathKind>? CopyPathRequested;

    public event EventHandler<string>? RevealInTreeRequested;

    public event EventHandler? PreviewRefreshRequested;

    [ObservableProperty]
    private bool canOpenFile;

    [ObservableProperty]
    private bool canExpand = false;

    [RelayCommand]
    private void SetTab(InspectorTab tab)
    {
        ActiveTab = tab;
    }

    public partial void OnActiveTabChanged(InspectorTab value)
    {
        CanExpand = value != InspectorTab.Details;
    }

    public partial void OnSelectedNodeChanged(FileTreeNode? value)
    {
        UpdateDetails();
    }

    public void UpdateDetails()
    {
        var node = SelectedNode;
        if (node is null || node.IsPlaceholder)
        {
            NodeIconGlyph = string.Empty;
            NodeName = string.Empty;
            NodeRelativePath = string.Empty;
            NodeSizeText = string.Empty;
            NodeAttributes = string.Empty;
            NodeAvailability = string.Empty;
            EffectiveMode = CollectionMode.Full;
            SourceGlyph = "·";
            SourceDescription = "Select a file or folder in the tree to see details.";
            ReasonText = string.Empty;
            HasLocalRule = false;
            IsDirectory = false;
            CanOpenFile = false;
            RuleSteps.Clear();
            return;
        }

        IsDirectory = node.IsDirectory;
        NodeIconGlyph = node.IsDirectory ? "" : "";
        NodeName = node.DisplayName;
        NodeRelativePath = node.RelativePath.Length == 0 ? "(scan root)" : node.RelativePath;
        NodeSizeText = string.IsNullOrEmpty(node.SizeDisplay) ? "—" : node.SizeDisplay;

        var attributes = new List<string>();
        if (node.IsReparsePoint)
        {
            attributes.Add("reparse point");
        }

        if (!node.IsDirectory)
        {
            var extension = Path.GetExtension(node.DisplayName).ToLowerInvariant();
            if (extension.Length > 0)
            {
                attributes.Add(extension);
            }

            if (node.IsBinaryFile)
            {
                attributes.Add("binary");
            }
        }

        NodeAttributes = attributes.Count > 0 ? string.Join(" · ", attributes) : string.Empty;
        NodeAvailability = node.IsAccessible ? string.Empty : node.AccessError ?? "Unavailable";
        EffectiveMode = node.EffectiveMode;
        SourceGlyph = node.SourceGlyph;
        SourceDescription = node.SourceDescription;
        ReasonText = string.IsNullOrEmpty(node.PlanReason) ? string.Empty : ReasonCatalog.Describe(node.PlanReason);
        HasLocalRule = node.HasLocalRule;
    }

    public void SetRuleSteps(IEnumerable<RuleStepInfo> steps)
    {
        RuleSteps.Clear();
        foreach (var step in steps)
        {
            RuleSteps.Add(step);
        }
    }

    public void RefreshPreview(
        string prefixContent,
        CollectionPlan? plan,
        string presetName,
        string prefixPresetName,
        string rootPath,
        bool redactRoot,
        bool includeMetadataBlocks)
    {
        PreviewLines.Clear();
        AddLine("# Files Collector report", PreviewLineKind.Header, null);
        if (!string.IsNullOrWhiteSpace(prefixContent))
        {
            foreach (var line in prefixContent.TrimEnd().Split('\n'))
            {
                AddLine(line, PreviewLineKind.Normal, null);
            }
        }

        if (PreviewShowYaml)
        {
            AddLine("## Report metadata", PreviewLineKind.Header, null);
            AddLine("```yaml", PreviewLineKind.Meta, null);
            AddLine($"preset: \"{presetName}\"", PreviewLineKind.Meta, null);
            AddLine($"prefix_preset: \"{prefixPresetName}\"", PreviewLineKind.Meta, null);
            AddLine($"root: \"{(redactRoot ? "<redacted>" : rootPath)}\"", PreviewLineKind.Meta, null);
            if (plan is not null)
            {
                AddLine($"included: full {_plan.FullCount}, signatures {_plan.SignaturesCount}, listed {_plan.ListedCount}", PreviewLineKind.Meta, null);
                AddLine($"excluded: {_plan.ExcludedCount}", PreviewLineKind.Meta, null);
            }

            AddLine("```", PreviewLineKind.Meta, null);
        }

        AddLine("## File index", PreviewLineKind.Header, null);
        if (plan is null)
        {
            AddLine("The plan is not available yet.", PreviewLineKind.Note, null);
            return;
        }

        var items = plan.Items
            .Where(item => PreviewShowExcluded || item.Mode != CollectionMode.Excluded)
            .Take(50)
            .ToList();

        foreach (var item in items)
        {
            var line = $"{(PreviewShowReasons && item.Reason is not null ? $"{item.RelativePath}  ({ReasonCatalog.ShortName(item.Reason)})" : item.RelativePath)}  ·  {item.Mode}  ·  {SizeFormatter.FormatCompact(item.SizeBytes ?? 0)}";
            AddLine(line, PreviewLineKind.Item, item.Mode);
        }

        var visibleTotal = plan.Items.Count(item => PreviewShowExcluded || item.Mode != CollectionMode.Excluded);
        if (visibleTotal > 50)
        {
            AddLine($"... {visibleTotal - 50} more item(s)", PreviewLineKind.Note, null);
        }

        if (!PreviewShowExcluded && _plan.ExcludedCount > 0)
        {
            AddLine($"{_plan.ExcludedCount} excluded item(s) hidden — enable “Show excluded” to list them.", PreviewLineKind.Note, null);
        }

        if (!includeMetadataBlocks)
        {
            AddLine("Per-file YAML metadata blocks are disabled.", PreviewLineKind.Note, null);
        }
    }

    public void RefreshPreviewToggles()
    {
        // The coordinator rebuilds the preview when toggles change.
        PreviewRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddLine(string text, PreviewLineKind kind, CollectionMode? mode)
    {
        PreviewLines.Add(new PreviewLine(text, kind, mode));
    }
}

public enum CopyPathKind
{
    Full,
    Relative
}
