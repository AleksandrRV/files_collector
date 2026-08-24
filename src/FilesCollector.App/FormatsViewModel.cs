using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.Core.Rules;

namespace FilesCollector.App;

public enum ExtensionSort
{
    Files,
    Size,
    Name
}

public enum ExtensionFilter
{
    All,
    Enabled,
    Disabled,
    Binary,
    NoExtension
}

public sealed partial class ExtensionRow : ObservableObject
{
    public ExtensionRow(string extension, int count, long sizeBytes, bool enabled, CollectionMode mode)
    {
        Extension = extension;
        Count = count;
        SizeBytes = sizeBytes;
        IsBinary = BinaryExtensions.IsBinary(extension);
        HasExtension = !string.Equals(extension, "[no extension]", StringComparison.OrdinalIgnoreCase);
        Enabled = enabled;
        Mode = mode;
    }

    public string Extension { get; }

    public int Count { get; }

    public long SizeBytes { get; }

    public bool IsBinary { get; }

    public bool HasExtension { get; }

    public double SizeShare { get; set; }

    [ObservableProperty]
    private bool enabled;

    [ObservableProperty]
    private CollectionMode mode = CollectionMode.Full;

    public event EventHandler? Changed;

    partial void OnEnabledChanged(bool value)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnModeChanged(CollectionMode value)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public static class BinaryExtensions
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".bmp", ".dll", ".exe", ".gif", ".ico", ".jar", ".jpeg", ".jpg", ".pdf", ".png", ".zip"
    };

    public static bool IsBinary(string extension)
    {
        return Known.Contains(extension);
    }
}

public sealed partial class FormatsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool includeAllExtensions = true;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private ExtensionSort sortKey = ExtensionSort.Files;

    [ObservableProperty]
    private ExtensionFilter filterKey = ExtensionFilter.All;

    [ObservableProperty]
    private string effectSummary = string.Empty;

    [ObservableProperty]
    private int disabledCount;

    public static IReadOnlyList<ExtensionSort> SortOptions { get; } = Enum.GetValues<ExtensionSort>();

    public static IReadOnlyList<ExtensionFilter> FilterOptions { get; } = Enum.GetValues<ExtensionFilter>();

    public ObservableCollection<ExtensionRow> Rows { get; } = [];


    public event EventHandler? EnableAllRequested;

    public event EventHandler? DisableAllRequested;

    public event EventHandler? InvertRequested;

    public event EventHandler<bool> BulkEnableRequested;

    public event EventHandler<CollectionMode>? BulkModeRequested;

    [RelayCommand]
    private void EnableAll()
    {
        EnableAllRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DisableAll()
    {
        DisableAllRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void InvertAll()
    {
        InvertRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void EnableSelected()
    {
        BulkEnableRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void DisableSelected()
    {
        BulkEnableRequested?.Invoke(this, false);
    }

    [RelayCommand]
    private void SetModeSelected(CollectionMode mode)
    {
        BulkModeRequested?.Invoke(this, mode);
    }
}
