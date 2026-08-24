using CommunityToolkit.Mvvm.ComponentModel;
using FilesCollector.Core.Rules;

namespace FilesCollector.App;

public sealed partial class ExtensionRuleItem : ObservableObject
{
    public ExtensionRuleItem(string extension, int count, bool enabled, CollectionMode mode)
    {
        Extension = extension;
        Count = count;
        Enabled = enabled;
        Mode = mode;
    }

    public string Extension { get; }

    public int Count { get; set; }

    [ObservableProperty]
    private bool enabled;

    [ObservableProperty]
    private CollectionMode mode;

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
