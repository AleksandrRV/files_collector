using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.Core;
using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Reporting;
using FilesCollector.Core.Rules;
using FilesCollector.Core.Settings;

namespace FilesCollector.App;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAppPaths _appPaths;
    private readonly IScanRootProvider _scanRootProvider;
    private readonly IFileSystem _fileSystem;
    private readonly IFileInventoryStore _inventoryStore;
    private readonly CollectionPlanner _collectionPlanner;
    private readonly IReportWriter _reportWriter;
    private readonly IPresetRepository _presetRepository;
    private readonly IAppSessionStore _appSessionStore;
    private readonly RuleSet _ruleSet = new();
    private Preset _activePreset = null!;
    private PresetState _savedPresetState = null!;
    private string? _excludedDirectoryPath;
    private bool _suppressPresetSelection;
    private bool _suppressPrefixPresetState;
    private bool _suppressFilterChanges;
    private CollectionPlan? _currentPlan;
    private FileInventorySnapshot? _inventorySnapshot;
    private CancellationTokenSource? _inventoryRefreshCancellation;
    private Timer? _inventoryRefreshTimer;
    private Timer? _searchDebounceTimer;
    private Timer? _filterDebounceTimer;
    private FileSystemWatcher? _inventoryWatcher;
    private Timer? _watcherDebounceTimer;
    private readonly SynchronizationContext _uiContext;

    public MainWindowViewModel(
        IAppPaths appPaths,
        IScanRootProvider scanRootProvider,
        IFileSystem fileSystem,
        IFileInventoryStore inventoryStore,
        CollectionPlanner collectionPlanner,
        IReportWriter reportWriter,
        IPresetRepository presetRepository,
        IAppSessionStore appSessionStore,
        PrefixPresetsViewModel prefixPresets)
    {
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _appPaths = appPaths;
        _scanRootProvider = scanRootProvider;
        _fileSystem = fileSystem;
        _inventoryStore = inventoryStore;
        _collectionPlanner = collectionPlanner;
        _reportWriter = reportWriter;
        _presetRepository = presetRepository;
        _appSessionStore = appSessionStore;
        PrefixPresets = prefixPresets;
        PrefixPresets.StateChanged += OnPrefixPresetStateChanged;
        ScanRoot = string.Empty;
        StatusText = "Ready.";
        LoadPresetList();
        var lastPreset = _appSessionStore.GetLastPresetId() is { } lastPresetId
            ? _presetRepository.Get(lastPresetId)
            : null;
        ActivatePreset(lastPreset ?? GetDefaultPreset());
        var startupRoot = !string.IsNullOrWhiteSpace(_activePreset.ScanRootPath) && Directory.Exists(_activePreset.ScanRootPath)
            ? _activePreset.ScanRootPath
            : _scanRootProvider.GetDefaultRoot();
        SetRoot(startupRoot);
        ResetPresetDirtyState();
    }

    public ObservableCollection<FileTreeNode> RootNodes { get; } = [];

    public PrefixPresetsViewModel PrefixPresets { get; }

    public ObservableCollection<PresetListItem> PresetItems { get; } = [];

    public ObservableCollection<ExtensionRuleItem> ExtensionRuleItems { get; } = [];

    public IReadOnlyList<CollectionMode> CollectionModes { get; } = Enum.GetValues<CollectionMode>();

    public string ApplicationVersion => typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public string ApplicationDirectory => _appPaths.ApplicationDirectory;

    public string StorageMode => _appPaths.IsPortableMode ? "Portable" : "Local application data";

    public int RuleCount => _ruleSet.Rules.Count;

    [ObservableProperty]
    private string scanRoot;

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
    private string selectedNodeDetails = "Select a file or folder to view details.";

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySelectedModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetLocalRuleCommand))]
    private FileTreeNode? selectedNode;

    [ObservableProperty]
    private CollectionMode selectedMode = CollectionMode.Full;

    [ObservableProperty]
    private Guid selectedPresetId;

    [ObservableProperty]
    private string activePresetName = string.Empty;

    [ObservableProperty]
    private bool isPresetDirty;

    [ObservableProperty]
    private string planSummary = "Plan not calculated.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshInventoryCommand))]
    private bool isRefreshingInventory;

    [ObservableProperty]
    private string inventoryStatus = "Inventory is not loaded.";

    [ObservableProperty]
    private int inventoryRefreshMinutes = 1;

    [ObservableProperty]
    private string previewText = "Refresh the plan to preview the report structure.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateReportCommand))]
    private bool isGeneratingReport;

    [ObservableProperty]
    private string generationStatus = "No report has been created.";

    [ObservableProperty]
    private string? lastOutputPath;

    [ObservableProperty]
    private bool includeAllExtensions = true;

    [ObservableProperty]
    private bool includeHidden;

    [ObservableProperty]
    private bool includeSystem;

    [ObservableProperty]
    private bool followReparsePoints;

    [ObservableProperty]
    private string includePatternsText = string.Empty;

    [ObservableProperty]
    private string excludePatternsText = string.Empty;

    [ObservableProperty]
    private int maxFileSizeKiB = 5120;

    [ObservableProperty]
    private CollectionMode binaryFileMode = CollectionMode.Listed;

    [ObservableProperty]
    private bool redactRootPath;

    [ObservableProperty]
    private bool includeFileMetadataBlocks = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenamePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    private bool isDefaultPreset;

    [RelayCommand]
    private void ShowAbout()
    {
        AboutRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RequestRootChange()
    {
        RootChangeRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Reload()
    {
        if (!string.IsNullOrWhiteSpace(ScanRoot))
        {
            SetRoot(ScanRoot);
        }
    }

    [RelayCommand]
    private void RefreshPlan()
    {
        UpdatePlan();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshInventory), IncludeCancelCommand = true)]
    private async Task RefreshInventory(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ScanRoot) || !Directory.Exists(ScanRoot))
        {
            InventoryStatus = "Inventory cannot be refreshed because the scan root is unavailable.";
            return;
        }

        _inventoryRefreshCancellation?.Cancel();
        _inventoryRefreshCancellation?.Dispose();
        _inventoryRefreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsRefreshingInventory = true;
        InventoryStatus = "Refreshing inventory...";
        var rootPath = ScanRoot;
        var excludedDirectoryPath = _excludedDirectoryPath;
        var progress = new Progress<InventoryRefreshProgress>(value =>
        {
            InventoryStatus = $"Inventory: {value.DiscoveredFiles} files, {value.DiscoveredDirectories} folders";
        });

        try
        {
            var snapshot = await Task.Run(
                () => _inventoryStore.Refresh(rootPath, excludedDirectoryPath, progress, _inventoryRefreshCancellation.Token),
                _inventoryRefreshCancellation.Token);
            if (string.Equals(ScanRoot, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                _inventorySnapshot = snapshot;
                InventoryStatus = $"Inventory updated: {snapshot.Files.Count} files";
                UpdatePlan();
            }
        }
        catch (OperationCanceledException)
        {
            InventoryStatus = "Inventory refresh canceled.";
        }
        catch (IOException exception)
        {
            InventoryStatus = $"Inventory refresh failed: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            InventoryStatus = $"Inventory refresh failed: {exception.Message}";
        }
        finally
        {
            IsRefreshingInventory = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerateReport), IncludeCancelCommand = true)]
    private async Task GenerateReport(CancellationToken cancellationToken)
    {
        UpdatePlan();
        if (_currentPlan is null)
        {
            GenerationStatus = "The report plan is not available.";
            return;
        }

        IsGeneratingReport = true;
        GenerationStatus = "Preparing report generation...";
        var progress = new Progress<ReportGenerationProgress>(value =>
        {
            GenerationStatus = $"Processing {value.CompletedFiles}/{value.TotalFiles}: {value.CurrentPath}";
        });
        var request = new ReportGenerationRequest(
            ScanRoot,
            ActivePresetName,
            PrefixPresets.SelectedName,
            PrefixPresets.Content,
            _activePreset.ScanOptions.RedactRootPath,
            _activePreset.ScanOptions.IncludeFileMetadataBlocks,
            _currentPlan);

        try
        {
            var result = await Task.Run(() => _reportWriter.Write(request, progress, cancellationToken), cancellationToken);
            LastOutputPath = result.ReportPath;
            GenerationStatus = $"Report created: {Path.GetFileName(result.ReportPath)}";
        }
        catch (OperationCanceledException)
        {
            GenerationStatus = "Report generation canceled.";
        }
        catch (IOException exception)
        {
            GenerationStatus = $"Report generation failed: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            GenerationStatus = $"Report generation failed: {exception.Message}";
        }
        finally
        {
            IsGeneratingReport = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenOutput))]
    private void OpenOutput()
    {
        if (!string.IsNullOrWhiteSpace(LastOutputPath))
        {
            OutputOpenRequested?.Invoke(this, LastOutputPath);
        }
    }

    [RelayCommand]
    private void OpenOutputs()
    {
        Directory.CreateDirectory(_appPaths.OutputsDirectory);
        OutputOpenRequested?.Invoke(this, _appPaths.OutputsDirectory);
    }

    [RelayCommand(CanExecute = nameof(CanApplyRule))]
    private void ApplySelectedMode()
    {
        ApplyMode(SelectedMode);
    }

    [RelayCommand(CanExecute = nameof(CanResetLocalRule))]
    private void ResetLocalRule()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var removed = _ruleSet.RemoveRule(SelectedNode.RelativePath, GetRuleKind(SelectedNode));
        if (!removed)
        {
            StatusText = "The selected item has no local rule.";
            return;
        }

        RefreshRulePresentation();
        OnPropertyChanged(nameof(RuleCount));
        ResetLocalRuleCommand.NotifyCanExecuteChanged();
        UpdateDirtyState();
        UpdatePlan();
        StatusText = $"Local rule reset: {SelectedNode.DisplayName}";
    }

    [RelayCommand]
    private void NewPreset()
    {
        var name = RequestPresetName("New preset", "Preset name:", string.Empty);
        if (name is null)
        {
            return;
        }

        var validationError = ValidatePresetName(name, null);
        if (validationError is not null)
        {
            StatusText = validationError;
            return;
        }

        var now = DateTimeOffset.Now;
        var preset = new Preset
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            GlobalMode = CollectionMode.Full,
            ExtensionRules = [],
            PathRules = [],
            ScanOptions = new ScanOptions(),
            ScanRootPath = ScanRoot,
            PrefixPresetId = PrefixPresets.SelectedId
        };
        _presetRepository.Save(preset);
        LoadPresetList();
        ActivatePreset(_presetRepository.Get(preset.Id) ?? preset);
        RefreshRulePresentation();
        StatusText = $"Preset created: {preset.Name}";
    }

    [RelayCommand]
    private void SavePreset()
    {
        SaveCurrentPreset();
    }

    [RelayCommand]
    private void SavePresetAs()
    {
        var name = RequestPresetName("Save preset as", "New preset name:", ActivePresetName);
        if (name is null)
        {
            return;
        }

        var validationError = ValidatePresetName(name, null);
        if (validationError is not null)
        {
            StatusText = validationError;
            return;
        }

        var now = DateTimeOffset.Now;
        var preset = CreateCurrentPreset(Guid.NewGuid(), name.Trim(), now, now);
        _presetRepository.Save(preset);
        LoadPresetList();
        ActivatePreset(_presetRepository.Get(preset.Id) ?? preset);
        StatusText = $"Preset saved as: {preset.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanRenamePreset))]
    private void RenamePreset()
    {
        var name = RequestPresetName("Rename preset", "Preset name:", ActivePresetName);
        if (name is null)
        {
            return;
        }

        var validationError = ValidatePresetName(name, _activePreset.Id);
        if (validationError is not null)
        {
            StatusText = validationError;
            return;
        }

        _activePreset.Name = name.Trim();
        SaveCurrentPreset();
    }

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        var request = new UnsavedChangesRequestEventArgs(ActivePresetName);
        DeletePresetRequested?.Invoke(this, request);
        if (request.Decision != UnsavedChangesDecision.Discard)
        {
            return;
        }

        if (_presetRepository.Delete(_activePreset.Id))
        {
            LoadPresetList();
            ActivatePreset(GetDefaultPreset());
            RefreshRulePresentation();
            StatusText = "Preset deleted.";
        }
    }

    [RelayCommand]
    private void DiscardPresetChanges()
    {
        ActivatePreset(_presetRepository.Get(_activePreset.Id) ?? GetDefaultPreset());
        RefreshRulePresentation();
        StatusText = "Unsaved preset changes discarded.";
    }

    public event EventHandler? AboutRequested;

    public event EventHandler? RootChangeRequested;

    public event EventHandler<PresetNameRequestEventArgs>? PresetNameRequested;

    public event EventHandler<UnsavedChangesRequestEventArgs>? UnsavedChangesRequested;

    public event EventHandler<UnsavedChangesRequestEventArgs>? DeletePresetRequested;

    public event EventHandler<string>? OutputOpenRequested;

    public void Shutdown()
    {
        _inventoryRefreshCancellation?.Cancel();
        _inventoryRefreshCancellation?.Dispose();
        _inventoryRefreshTimer?.Dispose();
        _searchDebounceTimer?.Dispose();
        _filterDebounceTimer?.Dispose();
        _inventoryWatcher?.Dispose();
        _watcherDebounceTimer?.Dispose();
    }

    public void SetRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var normalizedRootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(normalizedRootPath))
        {
            StatusText = "The selected scan root does not exist.";
            return;
        }

        ScanRoot = normalizedRootPath;
        if (_activePreset is not null)
        {
            UpdateDirtyState();
        }
        RootNodes.Clear();
        SelectedNode = null;

        if (IsApplicationDirectory(normalizedRootPath))
        {
            _excludedDirectoryPath = normalizedRootPath;
            StatusText = "The application directory is excluded from scanning.";
            return;
        }

        _excludedDirectoryPath = _scanRootProvider.IsApplicationDirectoryInsideRoot(normalizedRootPath)
            ? _appPaths.ApplicationDirectory
            : null;

        _inventorySnapshot = _inventoryStore.Load(normalizedRootPath);
        InventoryStatus = _inventorySnapshot is null
            ? "Inventory cache is unavailable. Refresh has been scheduled."
            : $"Inventory cache loaded: {_inventorySnapshot.Files.Count} files";
        var rootNode = FileTreeNode.CreateRoot(normalizedRootPath);
        ApplyRuleResolution(rootNode);
        RootNodes.Add(rootNode);
        LoadChildren(rootNode);
        UpdatePlan();
        ConfigureInventoryRefreshTimer();
        ConfigureInventoryWatcher();
        _ = RefreshInventory(CancellationToken.None);
        StatusText = $"Scan root loaded: {normalizedRootPath}";
    }

    public void LoadChildren(FileTreeNode? node)
    {
        if (node is null || !node.IsDirectory || !node.IsAccessible || node.IsReparsePoint || node.AreChildrenLoaded)
        {
            return;
        }

        var result = _fileSystem.GetChildren(node.FullPath, _excludedDirectoryPath);
        node.Children.Clear();
        node.MarkChildrenLoaded();

        foreach (var entry in result.Entries)
        {
            var child = FileTreeNode.FromEntry(entry, GetRelativePath(entry.FullPath));
            ApplyRuleResolution(child);
            if (child.IsDirectory && child.IsAccessible && !child.IsReparsePoint)
            {
                child.MarkChildrenUnloaded();
            }

            node.Children.Add(child);
        }

        if (!result.IsSuccessful)
        {
            node.MarkUnavailable(result.ErrorMessage ?? "The directory could not be read.");
            StatusText = $"Directory read failed: {node.FullPath}";
            return;
        }

        ApplyTreeFilter();
        StatusText = $"Loaded {result.Entries.Count} item(s) from {node.FullPath}";
    }

    public void ApplyMode(CollectionMode mode)
    {
        if (!CanApplyRule())
        {
            return;
        }

        var node = SelectedNode!;
        _ruleSet.SetRule(node.RelativePath, GetRuleKind(node), mode);
        SelectedMode = mode;
        RefreshRulePresentation();
        OnPropertyChanged(nameof(RuleCount));
        ResetLocalRuleCommand.NotifyCanExecuteChanged();
        UpdateDirtyState();
        UpdatePlan();
        StatusText = node.IsDirectory
            ? $"Directory rule applied recursively: {node.DisplayName} → {GetModeText(mode)}"
            : $"File rule applied: {node.DisplayName} → {GetModeText(mode)}";
    }

    partial void OnLastOutputPathChanged(string? value)
    {
        OpenOutputCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = new Timer(_ =>
        {
            _uiContext.Post(_ => ApplyTreeFilter(), null);
        }, null, TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
    }

    partial void OnShowFilesChanged(bool value)
    {
        ApplyTreeFilter();
    }

    partial void OnShowFoldersChanged(bool value)
    {
        ApplyTreeFilter();
    }

    partial void OnShowLocalRulesOnlyChanged(bool value)
    {
        ApplyTreeFilter();
    }

    partial void OnShowExcludedOnlyChanged(bool value)
    {
        ApplyTreeFilter();
    }

    partial void OnSelectedNodeChanged(FileTreeNode? value)
    {
        ApplySelectedModeCommand.NotifyCanExecuteChanged();
        ResetLocalRuleCommand.NotifyCanExecuteChanged();

        if (value is not null && !value.IsPlaceholder)
        {
            SelectedMode = value.EffectiveMode;
            StatusText = value.ToolTipText;
            SelectedNodeDetails = $"Path: {value.RelativePath}{Environment.NewLine}Mode: {value.EffectiveModeText}{Environment.NewLine}Source: {value.RuleSourceText}{Environment.NewLine}Status: {value.StatusText}";
        }
        else
        {
            SelectedNodeDetails = "Select a file or folder to view details.";
        }
    }

    partial void OnSelectedPresetIdChanged(Guid value)
    {
        if (!_suppressPresetSelection && value != Guid.Empty && value != _activePreset.Id)
        {
            TrySwitchPreset(value);
        }
    }

    private bool CanRefreshInventory()
    {
        return !IsRefreshingInventory && !string.IsNullOrWhiteSpace(ScanRoot) && Directory.Exists(ScanRoot);
    }

    private bool CanGenerateReport()
    {
        return !IsGeneratingReport && _currentPlan is not null;
    }

    private bool CanOpenOutput()
    {
        return !string.IsNullOrWhiteSpace(LastOutputPath) && File.Exists(LastOutputPath);
    }

    private bool CanApplyRule()
    {
        return SelectedNode is { IsPlaceholder: false };
    }

    private bool CanResetLocalRule()
    {
        return SelectedNode is { IsPlaceholder: false, HasLocalRule: true };
    }

    private bool CanRenamePreset()
    {
        return !IsDefaultPreset;
    }

    private bool CanDeletePreset()
    {
        return !IsDefaultPreset;
    }

    private void TrySwitchPreset(Guid id)
    {
        if (IsPresetDirty)
        {
            var request = new UnsavedChangesRequestEventArgs(ActivePresetName);
            UnsavedChangesRequested?.Invoke(this, request);
            if (request.Decision == UnsavedChangesDecision.Save && !SaveCurrentPreset())
            {
                RestorePresetSelection();
                return;
            }

            if (request.Decision == UnsavedChangesDecision.Cancel)
            {
                RestorePresetSelection();
                return;
            }
        }

        var preset = _presetRepository.Get(id);
        if (preset is null)
        {
            StatusText = "The selected preset is no longer available.";
            RestorePresetSelection();
            return;
        }

        ActivatePreset(preset);
        var presetRoot = !string.IsNullOrWhiteSpace(preset.ScanRootPath) && Directory.Exists(preset.ScanRootPath)
            ? preset.ScanRootPath
            : ScanRoot;
        SetRoot(presetRoot);
        ResetPresetDirtyState();
        RefreshRulePresentation();
        StatusText = $"Preset selected: {preset.Name}";
    }

    private bool SaveCurrentPreset()
    {
        try
        {
            var preset = CreateCurrentPreset(_activePreset.Id, _activePreset.Name, _activePreset.CreatedAt, _activePreset.UpdatedAt);
            _presetRepository.Save(preset);
            _suppressPresetSelection = true;
            LoadPresetList();
            ActivatePreset(_presetRepository.Get(preset.Id) ?? preset);
            _suppressPresetSelection = false;
            OnPropertyChanged(nameof(SelectedPresetId));
            StatusText = $"Preset saved: {preset.Name}";
            return true;
        }
        catch (ArgumentException exception)
        {
            StatusText = exception.Message;
            return false;
        }
        catch (IOException exception)
        {
            StatusText = $"Preset save failed: {exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            StatusText = $"Preset save failed: {exception.Message}";
            return false;
        }
    }

    private void ActivatePreset(Preset preset)
    {
        _activePreset = preset;
        _ruleSet.LoadRules(preset.PathRules);
        _suppressPrefixPresetState = true;
        PrefixPresets.Select(preset.PrefixPresetId);
        _suppressPrefixPresetState = false;
        LoadFilterSettings();
        _savedPresetState = CreatePresetState();
        ActivePresetName = preset.Name;
        IsDefaultPreset = preset.Id == PresetDefaults.DefaultPresetId;
        IsPresetDirty = false;
        var wasSuppressingPresetSelection = _suppressPresetSelection;
        _suppressPresetSelection = true;
        SelectedPresetId = preset.Id;
        OnPropertyChanged(nameof(SelectedPresetId));
        _suppressPresetSelection = wasSuppressingPresetSelection;
        OnPropertyChanged(nameof(RuleCount));
        ResetLocalRuleCommand.NotifyCanExecuteChanged();
        _appSessionStore.SetLastPresetId(preset.Id);
    }

    private void OnPrefixPresetStateChanged(object? sender, EventArgs e)
    {
        UpdatePreview();
        if (!_suppressPrefixPresetState && _activePreset is not null)
        {
            UpdateDirtyState();
        }
    }

    private void LoadPresetList()
    {
        PresetItems.Clear();
        foreach (var preset in _presetRepository.GetAll())
        {
            PresetItems.Add(new PresetListItem(preset.Id, preset.Name, preset.Id == PresetDefaults.DefaultPresetId));
        }
    }

    private Preset GetDefaultPreset()
    {
        return _presetRepository.Get(PresetDefaults.DefaultPresetId)
            ?? throw new InvalidOperationException("The default preset could not be loaded.");
    }

    private string? RequestPresetName(string title, string prompt, string initialName)
    {
        var request = new PresetNameRequestEventArgs(title, prompt, initialName);
        PresetNameRequested?.Invoke(this, request);
        return request.IsAccepted ? request.Name : null;
    }

    private string? ValidatePresetName(string name, Guid? currentId)
    {
        return PresetNameValidator.Validate(name, PresetItems, currentId);
    }

    private void RestorePresetSelection()
    {
        _suppressPresetSelection = true;
        SelectedPresetId = _activePreset.Id;
        _suppressPresetSelection = false;
    }

    private void UpdateDirtyState()
    {
        IsPresetDirty = !ArePresetStatesEqual(CreatePresetState(), _savedPresetState);
    }

    private void ResetPresetDirtyState()
    {
        _savedPresetState = CreatePresetState();
        IsPresetDirty = false;
    }

    private static bool ArePresetStatesEqual(PresetState left, PresetState right)
    {
        return left.GlobalMode == right.GlobalMode &&
            string.Equals(left.ScanRootPath, right.ScanRootPath, StringComparison.OrdinalIgnoreCase) &&
            left.PrefixPresetId == right.PrefixPresetId &&
            left.IncludeAllExtensions == right.IncludeAllExtensions &&
            left.IncludeHidden == right.IncludeHidden &&
            left.IncludeSystem == right.IncludeSystem &&
            left.FollowReparsePoints == right.FollowReparsePoints &&
            left.MaxFileSizeBytes == right.MaxFileSizeBytes &&
            left.BinaryFileMode == right.BinaryFileMode &&
            left.RedactRootPath == right.RedactRootPath &&
            left.IncludeFileMetadataBlocks == right.IncludeFileMetadataBlocks &&
            left.InventoryRefreshMinutes == right.InventoryRefreshMinutes &&
            left.ExtensionRules.SequenceEqual(right.ExtensionRules) &&
            left.PathRules.SequenceEqual(right.PathRules) &&
            left.IncludePatterns.SequenceEqual(right.IncludePatterns) &&
            left.ExcludePatterns.SequenceEqual(right.ExcludePatterns);
    }

    private Preset CreateCurrentPreset(Guid id, string name, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        return new Preset
        {
            Id = id,
            Name = name,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            GlobalMode = _activePreset.GlobalMode,
            ExtensionRules = _activePreset.ExtensionRules.ToList(),
            PathRules = _ruleSet.Rules.ToList(),
            ScanOptions = CloneScanOptions(_activePreset.ScanOptions),
            ScanRootPath = ScanRoot,
            PrefixPresetId = PrefixPresets.SelectedId
        };
    }

    private PresetState CreatePresetState()
    {
        return new PresetState(
            _activePreset.GlobalMode,
            ScanRoot,
            PrefixPresets.SelectedId,
            _activePreset.ExtensionRules.ToArray(),
            _ruleSet.Rules.ToArray(),
            _activePreset.ScanOptions.IncludeAllExtensions,
            _activePreset.ScanOptions.IncludeHidden,
            _activePreset.ScanOptions.IncludeSystem,
            _activePreset.ScanOptions.FollowReparsePoints,
            _activePreset.ScanOptions.MaxFileSizeBytes,
            _activePreset.ScanOptions.BinaryFileMode,
            _activePreset.ScanOptions.RedactRootPath,
            _activePreset.ScanOptions.IncludeFileMetadataBlocks,
            _activePreset.ScanOptions.InventoryRefreshMinutes,
            _activePreset.ScanOptions.IncludePatterns.ToArray(),
            _activePreset.ScanOptions.ExcludePatterns.ToArray());
    }

    private void LoadFilterSettings()
    {
        _suppressFilterChanges = true;
        IncludeAllExtensions = _activePreset.ScanOptions.IncludeAllExtensions;
        IncludeHidden = _activePreset.ScanOptions.IncludeHidden;
        IncludeSystem = _activePreset.ScanOptions.IncludeSystem;
        FollowReparsePoints = _activePreset.ScanOptions.FollowReparsePoints;
        MaxFileSizeKiB = Math.Max(1, (int)Math.Min(int.MaxValue, _activePreset.ScanOptions.MaxFileSizeBytes / 1024));
        BinaryFileMode = _activePreset.ScanOptions.BinaryFileMode;
        RedactRootPath = _activePreset.ScanOptions.RedactRootPath;
        IncludeFileMetadataBlocks = _activePreset.ScanOptions.IncludeFileMetadataBlocks;
        InventoryRefreshMinutes = _activePreset.ScanOptions.InventoryRefreshMinutes;
        IncludePatternsText = string.Join(Environment.NewLine, _activePreset.ScanOptions.IncludePatterns);
        ExcludePatternsText = string.Join(Environment.NewLine, _activePreset.ScanOptions.ExcludePatterns);
        _suppressFilterChanges = false;
    }

    private void UpdatePlan()
    {
        if (_inventorySnapshot is null)
        {
            PlanSummary = "Plan is unavailable until the inventory is loaded.";
            return;
        }

        var plan = _collectionPlanner.CreatePlan(_inventorySnapshot, _ruleSet, _activePreset.ExtensionRules, _activePreset.ScanOptions);
        _currentPlan = plan;
        GenerateReportCommand.NotifyCanExecuteChanged();
        LoadExtensionRuleItems(plan.ExtensionCounts);
        var warning = plan.EstimatedBytes > 25L * 1024 * 1024 ? " · Large report warning" : string.Empty;
        PlanSummary = $"Full: {plan.FullCount} · Signatures: {plan.SignaturesCount} · Listed: {plan.ListedCount} · Excluded: {plan.ExcludedCount} · Estimated: {FormatSize(plan.EstimatedBytes)}{warning}";
        UpdatePreview();
    }

    [RelayCommand]
    private void RefreshPreview()
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var lines = new List<string>
        {
            "<!-- files-collector-report-preview: 1 -->",
            "# Files Collector report preview",
            string.Empty
        };
        if (!string.IsNullOrWhiteSpace(PrefixPresets.Content))
        {
            lines.Add(PrefixPresets.Content.TrimEnd());
            lines.Add(string.Empty);
        }

        lines.Add("## Report metadata");
        lines.Add($"preset: {ActivePresetName}");
        lines.Add($"prefix_preset: {PrefixPresets.SelectedName}");
        lines.Add($"root: {ScanRoot}");
        lines.Add(string.Empty);
        lines.Add("## File index");
        if (_currentPlan is null)
        {
            lines.Add("Plan is not available.");
        }
        else
        {
            foreach (var item in _currentPlan.Items.Take(50))
            {
                lines.Add($"- [{item.Mode}] {item.RelativePath}{(item.Reason is null ? string.Empty : $" ({item.Reason})")}");
            }

            if (_currentPlan.Items.Count > 50)
            {
                lines.Add($"- ... {_currentPlan.Items.Count - 50} more item(s)");
            }
        }

        PreviewText = string.Join(Environment.NewLine, lines);
    }

    private void LoadExtensionRuleItems(IReadOnlyDictionary<string, int> extensionCounts)
    {
        _suppressFilterChanges = true;
        foreach (var item in ExtensionRuleItems)
        {
            item.Changed -= OnExtensionRuleChanged;
        }

        ExtensionRuleItems.Clear();
        var extensions = extensionCounts.Keys
            .Concat(_activePreset.ExtensionRules.Select(rule => rule.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase);
        foreach (var extension in extensions)
        {
            var rule = _activePreset.ExtensionRules.LastOrDefault(item => string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase));
            var item = new ExtensionRuleItem(extension, extensionCounts.GetValueOrDefault(extension), rule?.Enabled ?? IncludeAllExtensions, rule?.Mode ?? CollectionMode.Full);
            item.Changed += OnExtensionRuleChanged;
            ExtensionRuleItems.Add(item);
        }

        _suppressFilterChanges = false;
    }

    private void OnExtensionRuleChanged(object? sender, EventArgs e)
    {
        if (_suppressFilterChanges || sender is not ExtensionRuleItem item)
        {
            return;
        }

        _activePreset.ExtensionRules.RemoveAll(rule => string.Equals(rule.Extension, item.Extension, StringComparison.OrdinalIgnoreCase));
        if (item.Enabled != IncludeAllExtensions || item.Mode != CollectionMode.Full)
        {
            _activePreset.ExtensionRules.Add(new ExtensionRule(item.Extension, item.Enabled, item.Mode));
        }

        UpdateDirtyState();
        UpdatePlan();
    }

    partial void OnIncludeAllExtensionsChanged(bool value)
    {
        ApplyFilterChanges();
    }

    partial void OnIncludeHiddenChanged(bool value)
    {
        ApplyFilterChanges();
    }

    partial void OnIncludeSystemChanged(bool value)
    {
        ApplyFilterChanges();
    }

    partial void OnFollowReparsePointsChanged(bool value)
    {
        ApplyFilterChanges();
    }

    partial void OnIncludePatternsTextChanged(string value)
    {
        ScheduleFilterChanges();
    }

    partial void OnExcludePatternsTextChanged(string value)
    {
        ScheduleFilterChanges();
    }

    partial void OnMaxFileSizeKiBChanged(int value)
    {
        ApplyFilterChanges();
    }

    partial void OnBinaryFileModeChanged(CollectionMode value)
    {
        ApplyFilterChanges();
    }

    partial void OnRedactRootPathChanged(bool value)
    {
        ApplyFilterChanges();
    }

    partial void OnIncludeFileMetadataBlocksChanged(bool value)
    {
        ApplyFilterChanges();
    }

    partial void OnInventoryRefreshMinutesChanged(int value)
    {
        ApplyFilterChanges();
        ConfigureInventoryRefreshTimer();
    }

    private void ScheduleFilterChanges()
    {
        _filterDebounceTimer?.Dispose();
        _filterDebounceTimer = new Timer(_ =>
        {
            _uiContext.Post(_ => ApplyFilterChanges(), null);
        }, null, TimeSpan.FromMilliseconds(600), Timeout.InfiniteTimeSpan);
    }

    private void ApplyFilterChanges()
    {
        if (_suppressFilterChanges || _activePreset is null)
        {
            return;
        }

        _activePreset.ScanOptions.IncludeAllExtensions = IncludeAllExtensions;
        _activePreset.ScanOptions.IncludeHidden = IncludeHidden;
        _activePreset.ScanOptions.IncludeSystem = IncludeSystem;
        _activePreset.ScanOptions.FollowReparsePoints = FollowReparsePoints;
        _activePreset.ScanOptions.MaxFileSizeBytes = Math.Max(1, MaxFileSizeKiB) * 1024L;
        _activePreset.ScanOptions.BinaryFileMode = BinaryFileMode;
        _activePreset.ScanOptions.RedactRootPath = RedactRootPath;
        _activePreset.ScanOptions.IncludeFileMetadataBlocks = IncludeFileMetadataBlocks;
        _activePreset.ScanOptions.InventoryRefreshMinutes = Math.Max(0, InventoryRefreshMinutes);
        _activePreset.ScanOptions.IncludePatterns = ParsePatterns(IncludePatternsText);
        _activePreset.ScanOptions.ExcludePatterns = ParsePatterns(ExcludePatternsText);
        UpdateDirtyState();
        UpdatePlan();
    }

    private static List<string> ParsePatterns(string value)
    {
        return value
            .Split(['\r', '\n', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatSize(long sizeBytes)
    {
        return sizeBytes switch
        {
            < 1024 => $"{sizeBytes} B",
            < 1024 * 1024 => $"{sizeBytes / 1024d:F1} KiB",
            _ => $"{sizeBytes / 1024d / 1024d:F1} MiB"
        };
    }

    private void ConfigureInventoryRefreshTimer()
    {
        _inventoryRefreshTimer?.Dispose();
        var intervalMinutes = Math.Max(0, InventoryRefreshMinutes);
        if (intervalMinutes == 0)
        {
            _inventoryRefreshTimer = null;
            return;
        }

        var interval = TimeSpan.FromMinutes(intervalMinutes);
        _inventoryRefreshTimer = new Timer(_ =>
        {
            _uiContext.Post(_ =>
            {
                if (!IsRefreshingInventory)
                {
                    _ = RefreshInventory(CancellationToken.None);
                }
            }, null);
        }, null, interval, interval);
    }

    private void ConfigureInventoryWatcher()
    {
        _inventoryWatcher?.Dispose();
        _inventoryWatcher = null;
        if (string.IsNullOrWhiteSpace(ScanRoot) || !Directory.Exists(ScanRoot))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(ScanRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                InternalBufferSize = 32768,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnInventoryWatcherChanged;
            watcher.Created += OnInventoryWatcherChanged;
            watcher.Deleted += OnInventoryWatcherChanged;
            watcher.Renamed += OnInventoryWatcherRenamed;
            _inventoryWatcher = watcher;
        }
        catch (IOException)
        {
            InventoryStatus = "Inventory watcher is unavailable. Periodic refresh remains active.";
        }
        catch (ArgumentException)
        {
            InventoryStatus = "Inventory watcher is unavailable. Periodic refresh remains active.";
        }
    }

    private void OnInventoryWatcherChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsApplicationPath(e.FullPath))
        {
            ScheduleWatcherRefresh();
        }
    }

    private void OnInventoryWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsApplicationPath(e.FullPath) && !IsApplicationPath(e.OldFullPath))
        {
            ScheduleWatcherRefresh();
        }
    }

    private void ScheduleWatcherRefresh()
    {
        _watcherDebounceTimer?.Dispose();
        _watcherDebounceTimer = new Timer(_ =>
        {
            _uiContext.Post(_ =>
            {
                if (!IsRefreshingInventory)
                {
                    _ = RefreshInventory(CancellationToken.None);
                }
            }, null);
        }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    private void ApplyTreeFilter()
    {
        foreach (var rootNode in RootNodes)
        {
            ApplyTreeFilter(rootNode);
        }
    }

    private bool ApplyTreeFilter(FileTreeNode node)
    {
        if (node.IsPlaceholder)
        {
            node.IsVisible = false;
            return false;
        }

        var childMatches = false;
        foreach (var child in node.Children)
        {
            childMatches |= ApplyTreeFilter(child);
        }

        var matchesText = string.IsNullOrWhiteSpace(SearchText) ||
            node.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
            node.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        var matchesKind = node.IsDirectory ? ShowFolders : ShowFiles;
        var matchesRule = !ShowLocalRulesOnly || node.HasLocalRule;
        var matchesMode = !ShowExcludedOnly || node.EffectiveMode == CollectionMode.Excluded;
        node.IsVisible = (matchesText && matchesKind && matchesRule && matchesMode) || childMatches;
        return node.IsVisible;
    }

    private void RefreshRulePresentation()
    {
        foreach (var rootNode in RootNodes)
        {
            RefreshRulePresentation(rootNode);
        }

        ApplyTreeFilter();
    }

    private void RefreshRulePresentation(FileTreeNode node)
    {
        if (node.IsPlaceholder)
        {
            return;
        }

        ApplyRuleResolution(node);
        foreach (var child in node.Children)
        {
            RefreshRulePresentation(child);
        }
    }

    private void ApplyRuleResolution(FileTreeNode node)
    {
        node.ApplyRuleResolution(_ruleSet.Resolve(node.RelativePath, GetRuleKind(node)));
    }

    private string GetRelativePath(string fullPath)
    {
        return RuleSet.NormalizeRelativePath(Path.GetRelativePath(ScanRoot, fullPath));
    }

    private static ScanOptions CloneScanOptions(ScanOptions source)
    {
        return new ScanOptions
        {
            IncludeAllExtensions = source.IncludeAllExtensions,
            IncludeHidden = source.IncludeHidden,
            IncludeSystem = source.IncludeSystem,
            FollowReparsePoints = source.FollowReparsePoints,
            MaxFileSizeBytes = source.MaxFileSizeBytes,
            BinaryFileMode = source.BinaryFileMode,
            RedactRootPath = source.RedactRootPath,
            IncludeFileMetadataBlocks = source.IncludeFileMetadataBlocks,
            InventoryRefreshMinutes = source.InventoryRefreshMinutes,
            IncludePatterns = source.IncludePatterns.ToList(),
            ExcludePatterns = source.ExcludePatterns.ToList()
        };
    }

    private static PathRuleKind GetRuleKind(FileTreeNode node)
    {
        return node.IsDirectory ? PathRuleKind.Directory : PathRuleKind.File;
    }

    private static string GetModeText(CollectionMode mode)
    {
        return mode switch
        {
            CollectionMode.Full => "Full",
            CollectionMode.Signatures => "Signatures",
            CollectionMode.Listed => "Listed",
            CollectionMode.Excluded => "Excluded",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }

    private bool IsApplicationPath(string path)
    {
        var normalizedApplicationDirectory = Path.TrimEndingDirectorySeparator(_appPaths.ApplicationDirectory) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedApplicationDirectory, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.TrimEndingDirectorySeparator(normalizedPath), Path.TrimEndingDirectorySeparator(_appPaths.ApplicationDirectory), StringComparison.OrdinalIgnoreCase);
    }

    private bool IsApplicationDirectory(string rootPath)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(rootPath),
            Path.TrimEndingDirectorySeparator(_appPaths.ApplicationDirectory),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PresetState(
        CollectionMode GlobalMode,
        string ScanRootPath,
        Guid? PrefixPresetId,
        IReadOnlyList<ExtensionRule> ExtensionRules,
        IReadOnlyList<PathRule> PathRules,
        bool IncludeAllExtensions,
        bool IncludeHidden,
        bool IncludeSystem,
        bool FollowReparsePoints,
        long MaxFileSizeBytes,
        CollectionMode BinaryFileMode,
        bool RedactRootPath,
        bool IncludeFileMetadataBlocks,
        int InventoryRefreshMinutes,
        IReadOnlyList<string> IncludePatterns,
        IReadOnlyList<string> ExcludePatterns);
}
