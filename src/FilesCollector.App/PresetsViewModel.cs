using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.Core.Presets;

namespace FilesCollector.App;

public sealed partial class PresetsViewModel : ObservableObject
{
    [ObservableProperty]
    private string activePresetName = string.Empty;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private bool isDefaultPreset;

    [ObservableProperty]
    private Guid selectedPresetId;

    [ObservableProperty]
    private int pathRuleCount;

    [ObservableProperty]
    private int extensionRuleCount;

    [ObservableProperty]
    private int patternCount;

    [ObservableProperty]
    private string maxSizeText = string.Empty;

    [ObservableProperty]
    private string binaryModeText = string.Empty;

    [ObservableProperty]
    private string scanRootText = string.Empty;

    [ObservableProperty]
    private string prefixNameText = string.Empty;

    [ObservableProperty]
    private string updatedAtText = string.Empty;

    public ObservableCollection<PresetListItem> PresetItems { get; } = [];

    public event EventHandler<PresetNameRequestEventArgs>? NewPresetRequested;

    public event EventHandler? SavePresetRequested;

    public event EventHandler<PresetNameRequestEventArgs>? SavePresetAsRequested;

    public event EventHandler<PresetNameRequestEventArgs>? RenamePresetRequested;

    public event EventHandler<UnsavedChangesRequestEventArgs>? DeletePresetRequested;

    public event EventHandler? DiscardChangesRequested;

    public event EventHandler<Guid>? SwitchPresetRequested;

    [RelayCommand]
    private void NewPreset()
    {
        NewPresetRequested?.Invoke(this, new PresetNameRequestEventArgs("New preset", "Preset name:", string.Empty));
    }

    [RelayCommand]
    private void SavePreset()
    {
        SavePresetRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SavePresetAs()
    {
        SavePresetAsRequested?.Invoke(this, new PresetNameRequestEventArgs("Save preset as", "New preset name:", ActivePresetName));
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void RenamePreset()
    {
        RenamePresetRequested?.Invoke(this, new PresetNameRequestEventArgs("Rename preset", "Preset name:", ActivePresetName));
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeletePreset()
    {
        DeletePresetRequested?.Invoke(this, new UnsavedChangesRequestEventArgs(ActivePresetName));
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        DiscardChangesRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SwitchPreset(PresetListItem? item)
    {
        if (item is not null)
        {
            SwitchPresetRequested?.Invoke(this, item.Id);
        }
    }

    private bool CanRename()
    {
        return !IsDefaultPreset;
    }

    private bool CanDelete()
    {
        return !IsDefaultPreset;
    }

    partial void OnIsDefaultPresetChanged(bool value)
    {
        RenamePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
    }

    public void RefreshCard(int pathRules, int extensionRules, int patterns, string maxSizeText, string binaryModeText, string scanRootText, string prefixNameText, string updatedAtText)
    {
        PathRuleCount = pathRules;
        ExtensionRuleCount = extensionRules;
        PatternCount = patterns;
        MaxSizeText = maxSizeText;
        BinaryModeText = binaryModeText;
        ScanRootText = scanRootText;
        PrefixNameText = prefixNameText;
        UpdatedAtText = updatedAtText;
    }
}
