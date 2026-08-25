using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.Core.Rules;

namespace FilesCollector.App;

public enum TreeSection
{
    Explorer,
    Extensions,
    Filters,
    Prefix,
    Presets,
    History
}

public sealed partial class TreeViewModel : ObservableObject
{
    [ObservableProperty]
    private TreeSection activeSection = TreeSection.Explorer;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool showFiles = true;

    [ObservableProperty]
    private bool showFolders = true;

    [ObservableProperty]
    private bool showLocalRulesOnly;

    [ObservableProperty]
    private bool showExcludedOnly;

    [ObservableProperty]
    private bool showLargeOnly;

    [ObservableProperty]
    private bool showBinaryOnly;

    [ObservableProperty]
    private bool showNoExtensionOnly;

    [ObservableProperty]
    private FileTreeNode? selectedNode;

    [ObservableProperty]
    private bool hasMultiSelection;

    [ObservableProperty]
    private int selectedCount;

    [ObservableProperty]
    private bool isOnboardingVisible;

    [ObservableProperty]
    private bool isLeftPanelVisible;

    [ObservableProperty]
    private bool isRightPanelVisible = true;

    public ObservableCollection<FileTreeNode> RootNodes { get; } = [];

    public ObservableCollection<FileTreeNode> MultiSelected { get; } = [];


    public event EventHandler<TreeSection>? SectionChanged;

    public event EventHandler? CloseOnboardingRequested;

    public event EventHandler<CollectionMode>? BulkApplyModeRequested;

    public event EventHandler? BulkResetRequested;

    public event EventHandler? ClearMultiSelectionRequested;

    public event EventHandler? ToggleLeftPanelRequested;

    public event EventHandler? ToggleRightPanelRequested;

    [RelayCommand]
    private void SetSection(TreeSection section)
    {
        ActiveSection = section;
    }

    [RelayCommand]
    private void BulkApplyMode(CollectionMode mode)
    {
        BulkApplyModeRequested?.Invoke(this, mode);
    }

    [RelayCommand]
    private void BulkReset()
    {
        BulkResetRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearMultiSelection()
    {
        ClearMultiSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleLeftPanel()
    {
        ToggleLeftPanelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleRightPanel()
    {
        ToggleRightPanelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CloseOnboarding()
    {
        CloseOnboardingRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActiveSectionChanged(TreeSection value)
    {
        IsLeftPanelVisible = value != TreeSection.Explorer;
        SectionChanged?.Invoke(this, value);
    }

    public void SetMultiSelection(IEnumerable<FileTreeNode> nodes)
    {
        var list = nodes.ToList();
        foreach (var node in MultiSelected)
        {
            node.IsMultiSelected = false;
        }

        MultiSelected.Clear();
        foreach (var node in list)
        {
            node.IsMultiSelected = true;
            MultiSelected.Add(node);
        }

        HasMultiSelection = list.Count > 1;
        SelectedCount = list.Count;
    }

}
