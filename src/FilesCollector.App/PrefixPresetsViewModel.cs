using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.Core.Prefixes;

namespace FilesCollector.App;

public sealed partial class PrefixPresetsViewModel : ObservableObject
{
    private readonly IPrefixPresetRepository _repository;
    private PrefixPreset? _activePreset;
    private string _savedContent = string.Empty;
    private bool _suppressSelection;

    public PrefixPresetsViewModel(IPrefixPresetRepository repository)
    {
        _repository = repository;
        LoadItems();
    }

    public ObservableCollection<PrefixPresetListItem> Items { get; } = [];

    [ObservableProperty]
    private Guid? selectedId;

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private bool isDirty;

    public string SelectedName => _activePreset?.Name ?? "No prefix";

    public bool HasSelection => _activePreset is not null;

    public event EventHandler<PresetNameRequestEventArgs>? NameRequested;

    public event EventHandler<UnsavedChangesRequestEventArgs>? UnsavedChangesRequested;

    public event EventHandler<UnsavedChangesRequestEventArgs>? DeleteRequested;

    public event EventHandler? StateChanged;

    [RelayCommand]
    private void ClearSelection()
    {
        Select(null);
    }

    public bool Select(Guid? id)
    {
        if (_activePreset?.Id == id)
        {
            return true;
        }

        if (!ConfirmLeavingCurrent())
        {
            return false;
        }

        Activate(id is null ? null : _repository.Get(id.Value));
        return true;
    }

    [RelayCommand]
    private void New()
    {
        if (!ConfirmLeavingCurrent())
        {
            return;
        }

        var name = RequestName("New prefix preset", "Prefix preset name:", string.Empty);
        if (name is null)
        {
            return;
        }

        var validationError = PrefixPresetNameValidator.Validate(name, Items);
        if (validationError is not null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var preset = new PrefixPreset
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Content = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
        _repository.Save(preset);
        LoadItems();
        Activate(_repository.Get(preset.Id) ?? preset);
    }

    [RelayCommand]
    private void Save()
    {
        SaveCurrent();
    }

    [RelayCommand]
    private void SaveAs()
    {
        var name = RequestName("Save prefix preset as", "New prefix preset name:", SelectedName);
        if (name is null)
        {
            return;
        }

        var validationError = PrefixPresetNameValidator.Validate(name, Items);
        if (validationError is not null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var preset = new PrefixPreset
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Content = Content,
            CreatedAt = now,
            UpdatedAt = now
        };
        _repository.Save(preset);
        LoadItems();
        Activate(_repository.Get(preset.Id) ?? preset);
    }

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    private void Rename()
    {
        if (_activePreset is null)
        {
            return;
        }

        var name = RequestName("Rename prefix preset", "Prefix preset name:", _activePreset.Name);
        if (name is null)
        {
            return;
        }

        var validationError = PrefixPresetNameValidator.Validate(name, Items, _activePreset.Id);
        if (validationError is not null)
        {
            return;
        }

        _activePreset.Name = name.Trim();
        SaveCurrent();
    }

    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    private void Delete()
    {
        if (_activePreset is null)
        {
            return;
        }

        var request = new UnsavedChangesRequestEventArgs(_activePreset.Name);
        DeleteRequested?.Invoke(this, request);
        if (request.Decision != UnsavedChangesDecision.Discard)
        {
            return;
        }

        _repository.Delete(_activePreset.Id);
        LoadItems();
        Activate(null);
    }

    [RelayCommand]
    private void Discard()
    {
        Activate(_activePreset is null ? null : _repository.Get(_activePreset.Id));
    }

    partial void OnSelectedIdChanged(Guid? value)
    {
        if (_suppressSelection)
        {
            return;
        }

        if (!Select(value))
        {
            RestoreSelection();
        }
    }

    partial void OnContentChanged(string value)
    {
        IsDirty = _activePreset is not null && !string.Equals(value, _savedContent, StringComparison.Ordinal);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanModifySelected()
    {
        return _activePreset is not null;
    }

    private bool ConfirmLeavingCurrent()
    {
        if (!IsDirty)
        {
            return true;
        }

        var request = new UnsavedChangesRequestEventArgs(SelectedName);
        UnsavedChangesRequested?.Invoke(this, request);
        return request.Decision switch
        {
            UnsavedChangesDecision.Save => SaveCurrent(),
            UnsavedChangesDecision.Discard => true,
            _ => false
        };
    }

    private bool SaveCurrent()
    {
        if (_activePreset is null)
        {
            return true;
        }

        try
        {
            _activePreset.Content = Content;
            _repository.Save(_activePreset);
            LoadItems();
            Activate(_repository.Get(_activePreset.Id) ?? _activePreset);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void LoadItems()
    {
        Items.Clear();
        foreach (var preset in _repository.GetAll())
        {
            Items.Add(new PrefixPresetListItem(preset.Id, preset.Name));
        }
    }

    private void Activate(PrefixPreset? preset)
    {
        _activePreset = preset;
        _savedContent = preset?.Content ?? string.Empty;
        _suppressSelection = true;
        SelectedId = preset?.Id;
        Content = _savedContent;
        _suppressSelection = false;
        IsDirty = false;
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(HasSelection));
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string? RequestName(string title, string prompt, string initialName)
    {
        var request = new PresetNameRequestEventArgs(title, prompt, initialName);
        NameRequested?.Invoke(this, request);
        return request.IsAccepted ? request.Name : null;
    }

    private void RestoreSelection()
    {
        _suppressSelection = true;
        SelectedId = _activePreset?.Id;
        _suppressSelection = false;
    }
}
