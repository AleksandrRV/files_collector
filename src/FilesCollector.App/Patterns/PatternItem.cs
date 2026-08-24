using CommunityToolkit.Mvvm.ComponentModel;

namespace FilesCollector.App.Patterns;

public sealed partial class PatternItem : ObservableObject
{
    public PatternItem(string text, bool isEnabled = true)
    {
        Text = text;
        IsEnabled = isEnabled;
    }

    public string Text { get; }

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private int matchCount;

    [ObservableProperty]
    private bool? isMatchingSample;

    public string MatchCountText => MatchCount > 0 ? $"{MatchCount:N0} matches" : "0 matches";

    partial void OnMatchCountChanged(int value)
    {
        OnPropertyChanged(nameof(MatchCountText));
    }
}
