using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.App.Controls;
using FilesCollector.App.Patterns;
using FilesCollector.Core.Rules;

namespace FilesCollector.App;

public sealed partial class FiltersViewModel : ObservableObject
{
    [ObservableProperty]
    private bool includeHidden;

    [ObservableProperty]
    private bool includeSystem;

    [ObservableProperty]
    private bool followReparsePoints;

    [ObservableProperty]
    private int maxFileSizeKiB = 5120;

    [ObservableProperty]
    private double histogramMaxKiB = 10240;

    [ObservableProperty]
    private IReadOnlyList<SizeBucket> histogramBuckets = [];

    [ObservableProperty]
    private CollectionMode binaryFileMode = CollectionMode.Listed;

    [ObservableProperty]
    private bool redactRootPath;

    [ObservableProperty]
    private bool includeFileMetadataBlocks = true;

    [ObservableProperty]
    private int inventoryRefreshMinutes = 1;

    [ObservableProperty]
    private bool hasActivePatterns;

    public ObservableCollection<PatternItem> IncludePatterns { get; } = [];

    public ObservableCollection<PatternItem> ExcludePatterns { get; } = [];

    public event EventHandler? OptionsChanged;

    public void NotifyOptionsChanged()
    {
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SetMaxSize(double value)
    {
        MaxFileSizeKiB = (int)Math.Clamp(value, 1, int.MaxValue);
    }

    [RelayCommand]
    private void RefreshInventoryNow()
    {
        // The coordinator subscribes to the inventory refresh command through RootViewModel.
    }

    public void LoadPatterns(IEnumerable<string> include, IEnumerable<string> exclude)
    {
        IncludePatterns.Clear();
        foreach (var pattern in include)
        {
            IncludePatterns.Add(new PatternItem(pattern));
        }

        ExcludePatterns.Clear();
        foreach (var pattern in exclude)
        {
            ExcludePatterns.Add(new PatternItem(pattern));
        }

        UpdateHasActivePatterns();
    }

    public void SetPatternMatchCounts(IReadOnlyDictionary<string, int> includeCounts, IReadOnlyDictionary<string, int> excludeCounts)
    {
        foreach (var item in IncludePatterns)
        {
            item.MatchCount = includeCounts.GetValueOrDefault(item.Text, 0);
        }

        foreach (var item in ExcludePatterns)
        {
            item.MatchCount = excludeCounts.GetValueOrDefault(item.Text, 0);
        }
    }

    public void UpdateHasActivePatterns()
    {
        HasActivePatterns = IncludePatterns.Any(item => item.IsEnabled) || ExcludePatterns.Any(item => item.IsEnabled);
    }

    public IEnumerable<string> GetIncludePatterns()
    {
        return IncludePatterns.Where(item => item.IsEnabled).Select(item => item.Text).ToList();
    }

    public IEnumerable<string> GetExcludePatterns()
    {
        return ExcludePatterns.Where(item => item.IsEnabled).Select(item => item.Text).ToList();
    }
}
