using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FilesCollector.App;

public sealed record BreadcrumbSegment(string Label, string Path);

public sealed partial class RootViewModel : ObservableObject
{
    [ObservableProperty]
    private string scanRoot = string.Empty;

    [ObservableProperty]
    private string inventoryStatus = "Inventory is not loaded.";

    [ObservableProperty]
    private bool isRefreshingInventory;

    [ObservableProperty]
    private bool hasUsableRoot;

    public ObservableCollection<BreadcrumbSegment> BreadcrumbSegments { get; } = [];

    public event EventHandler? RootChangeRequested;

    public event EventHandler? TreeReloadRequested;

    public event EventHandler? InventoryRefreshRequested;

    public event EventHandler<string>? SegmentActivated;

    [RelayCommand]
    private void ChangeRoot()
    {
        RootChangeRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ReloadTree()
    {
        TreeReloadRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RefreshInventory()
    {
        InventoryRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ActivateSegment(BreadcrumbSegment? segment)
    {
        if (segment is not null)
        {
            SegmentActivated?.Invoke(this, segment.Path);
        }
    }
}
