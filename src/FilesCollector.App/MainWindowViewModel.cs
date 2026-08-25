using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.App.History;
using FilesCollector.App.Inspector;
using FilesCollector.App.Palette;
using FilesCollector.App.Toasts;
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
    private readonly IReportHistoryStore _historyStore;
    private readonly UiSettingsStore _uiSettings;
    private readonly RuleSet _ruleSet = new();
    private readonly SynchronizationContext _uiContext;

    private Preset _activePreset = null!;
    private PresetState _savedPresetState = null!;
    private string? _excludedDirectoryPath;
    private bool _suppressPrefixPresetState;
    private bool _suppressFilterChanges;
    private CollectionPlan? _currentPlan;
    private FileInventorySnapshot? _inventorySnapshot;
    private Dictionary<string, CollectionPlanItem> _planItemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, (int Count, long SizeBytes)> _folderAggregates = new(StringComparer.OrdinalIgnoreCase);
    private List<ExtensionRow> _extensionRows = [];
    private CancellationTokenSource? _inventoryRefreshCancellation;
    private CancellationTokenSource? _generationCancellation;
    private Timer? _inventoryRefreshTimer;
    private Timer? _searchDebounceTimer;
    private Timer? _filterDebounceTimer;
    private Timer? _formatSearchDebounceTimer;
    private Timer? _paletteDebounceTimer;
    private FileSystemWatcher? _inventoryWatcher;
    private Timer? _watcherDebounceTimer;
    private (string Label, Action Undo)? _lastUndo;
    private TreeSection _lastNonExplorerSection = TreeSection.Filters;
    private bool _firstInventoryLoaded;

    public MainWindowViewModel(
        IAppPaths appPaths,
        IScanRootProvider scanRootProvider,
        IFileSystem fileSystem,
        IFileInventoryStore inventoryStore,
        CollectionPlanner collectionPlanner,
        IReportWriter reportWriter,
        IPresetRepository presetRepository,
        IAppSessionStore appSessionStore,
        PrefixPresetsViewModel prefixPresets,
        IReportHistoryStore historyStore,
        UiSettingsStore uiSettings)
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
        _historyStore = historyStore;
        _uiSettings = uiSettings;
        PrefixPresets = prefixPresets;
        Root = new RootViewModel();
        Presets = new PresetsViewModel();
        Formats = new FormatsViewModel();
        Filters = new FiltersViewModel();
        Tree = new TreeViewModel();
        Inspector = new InspectorViewModel();
        History = new HistoryViewModel();
        Palette = new PaletteViewModel();
        Toasts = new ToastService();

        StatusText = "Ready.";
        IsPortable = _appPaths.IsPortableMode;

        WireViewModels();

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
        UpdateOnboarding();
    }

    public RootViewModel Root { get; }

    public PresetsViewModel Presets { get; }

    public FormatsViewModel Formats { get; }

    public FiltersViewModel Filters { get; }

    public TreeViewModel Tree { get; }

    public InspectorViewModel Inspector { get; }

    public HistoryViewModel History { get; }

    public PaletteViewModel Palette { get; }

    public ToastService Toasts { get; }

    public PrefixPresetsViewModel PrefixPresets { get; }

    public IReadOnlyList<CollectionMode> CollectionModes { get; } = Enum.GetValues<CollectionMode>();

    public string ApplicationVersion => typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public string ApplicationDirectory => _appPaths.ApplicationDirectory;

    public string StorageMode => _appPaths.IsPortableMode ? "Portable" : "Local application data";

    public string OutputsDirectory => _appPaths.OutputsDirectory;

    public string DataDirectory => _appPaths.LocalDataDirectory;

    public string DocumentationPath => Path.Combine(_appPaths.DocumentationDirectory, "FILES_COLLECTOR_DOCUMENTATION.md");

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private bool isPortable;

    [ObservableProperty]
    private bool isGeneratingReport;

    [ObservableProperty]
    private int generationCompleted;

    [ObservableProperty]
    private int generationTotal;

    [ObservableProperty]
    private string generationCurrentPath = string.Empty;

    [ObservableProperty]
    private string? lastOutputPath;

    [ObservableProperty]
    private string lastReportName = string.Empty;

    [ObservableProperty]
    private bool isInspectorExpanded;

    [ObservableProperty]
    private bool isPaletteOpen;

    // ===== Commands (window level) =====

    [RelayCommand]
    private void ShowAbout()
    {
        AboutRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanGenerateReport), IncludeCancelCommand = true)]
    private async Task GenerateReport(CancellationToken cancellationToken)
    {
        UpdatePlan();
        if (_currentPlan is null)
        {
            Toasts.ShowWarning("The report plan is not available yet.");
            return;
        }

        _generationCancellation?.Cancel();
        _generationCancellation?.Dispose();
        _generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsGeneratingReport = true;
        GenerationCompleted = 0;
        GenerationTotal = _currentPlan.Items.Count;
        GenerationCurrentPath = string.Empty;
        var progress = new Progress<ReportGenerationProgress>(value =>
        {
            GenerationCompleted = value.CompletedFiles;
            GenerationTotal = value.TotalFiles;
            GenerationCurrentPath = value.CurrentPath;
        });

        var request = new ReportGenerationRequest(
            Root.ScanRoot,
            Presets.ActivePresetName,
            PrefixPresets.SelectedName,
            PrefixPresets.Content,
            _activePreset.ScanOptions.RedactRootPath,
            _activePreset.ScanOptions.IncludeFileMetadataBlocks,
            _currentPlan);

        try
        {
            var result = await Task.Run(() => _reportWriter.Write(request, progress, _generationCancellation.Token), _generationCancellation.Token);
            LastOutputPath = result.ReportPath;
            LastReportName = Path.GetFileName(result.ReportPath);
            Toasts.ShowSuccess(
                "Report created",
                $"{result.FullCount:N0} full · {result.SignaturesCount:N0} signatures · {result.ListedCount:N0} listed · {result.ExcludedCount:N0} excluded",
                "Open report",
                () => OutputOpenRequested?.Invoke(this, result.ReportPath),
                "Open outputs",
                () => OutputOpenRequested?.Invoke(this, _appPaths.OutputsDirectory));
            RefreshHistory();
            try
            {
                Inspector.Plan.SetDiagnostics(_historyStore.GetDiagnosticsGroups(result.ManifestPath), Path.GetFileName(result.ReportPath));
            }
            catch (Exception)
            {
                // Diagnostics are a nice-to-have; never fail generation because of them.
            }
        }
        catch (OperationCanceledException)
        {
            Toasts.Show("Report generation canceled.");
        }
        catch (IOException exception)
        {
            Toasts.ShowError("Report generation failed.", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Toasts.ShowError("Report generation failed.", exception.Message);
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

    [RelayCommand]
    private void OpenPalette()
    {
        Palette.Open();
    }

    [RelayCommand]
    private void ClosePalette()
    {
        Palette.Close();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleDensity()
    {
        DensityToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleLeftPanel()
    {
        if (Tree.ActiveSection == TreeSection.Explorer)
        {
            Tree.SetSectionCommand.Execute(_lastNonExplorerSection);
        }
        else
        {
            Tree.SetSectionCommand.Execute(TreeSection.Explorer);
        }
    }

    [RelayCommand]
    private void ToggleRightPanel()
    {
        Tree.IsRightPanelVisible = !Tree.IsRightPanelVisible;
    }

    [RelayCommand]
    private void ExpandInspector()
    {
        if (Inspector.ActiveTab != InspectorTab.Details)
        {
            IsInspectorExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseInspector()
    {
        IsInspectorExpanded = false;
    }

    [RelayCommand]
    private void CloseOnboarding()
    {
        PersistOnboardingSeen();
        UpdateOnboarding();
    }

    // ===== Public API for the window =====

    public event EventHandler? AboutRequested;

    public event EventHandler? RootChangeRequested;

    public event EventHandler<PresetNameRequestEventArgs>? PresetNameRequested;

    public event EventHandler<UnsavedChangesRequestEventArgs>? UnsavedChangesRequested;

    public event EventHandler<UnsavedChangesRequestEventArgs>? DeletePresetRequested;

    public event EventHandler<string>? OutputOpenRequested;

    public event EventHandler<FileTreeNode>? RevealRequested;

    public event EventHandler? ThemeToggleRequested;

    public event EventHandler? DensityToggleRequested;

    public void Shutdown()
    {
        Toasts.Stop();
        _inventoryRefreshCancellation?.Cancel();
        _inventoryRefreshCancellation?.Dispose();
        _generationCancellation?.Cancel();
        _generationCancellation?.Dispose();
        _inventoryRefreshTimer?.Dispose();
        _searchDebounceTimer?.Dispose();
        _filterDebounceTimer?.Dispose();
        _formatSearchDebounceTimer?.Dispose();
        _paletteDebounceTimer?.Dispose();
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
            Toasts.ShowError("The selected scan root does not exist.", normalizedRootPath);
            return;
        }

        Root.ScanRoot = normalizedRootPath;
        Root.HasUsableRoot = true;
        Root.BreadcrumbSegments.Clear();
        foreach (var segment in BuildBreadcrumbSegments(normalizedRootPath))
        {
            Root.BreadcrumbSegments.Add(segment);
        }

        _activePreset.ScanRootPath = normalizedRootPath;
        UpdateDirtyState();
        Tree.SelectedNode = null;
        Inspector.SelectedNode = null;
        UnloadAllNodes();

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
        Root.InventoryStatus = _inventorySnapshot is null
            ? "Inventory cache is unavailable. Refresh has been scheduled."
            : $"Inventory cache loaded: {_inventorySnapshot.Files.Count:N0} files";
        var rootNode = FileTreeNode.CreateRoot(normalizedRootPath);
        ApplyRuleResolution(rootNode);
        AddRootNode(rootNode);
        LoadChildren(rootNode);
        UpdatePlan();
        ConfigureInventoryRefreshTimer();
        ConfigureInventoryWatcher();
        _ = RefreshInventory(CancellationToken.None);
        StatusText = $"Scan root loaded: {normalizedRootPath}";
        UpdateOnboarding();
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
            child.Parent = node;
            ApplyRuleResolution(child);
            ApplyPlanData(child);
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

    public void ApplyModeToNodes(IEnumerable<FileTreeNode> nodes, CollectionMode mode)
    {
        var targets = nodes.Where(node => node is not null and not { IsPlaceholder: true }).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var previousRules = new List<(FileTreeNode Node, PathRule? Previous)>();
        foreach (var node in targets)
        {
            var kind = GetRuleKind(node);
            var previous = _ruleSet.Rules.LastOrDefault(rule => rule.Kind == kind && string.Equals(rule.RelativePath, node.RelativePath, StringComparison.OrdinalIgnoreCase));
            previousRules.Add((node, previous));
            _ruleSet.SetRule(node.RelativePath, kind, mode);
        }

        RefreshRulePresentation();
        UpdatePlan();
        UpdateDirtyState();
        UpdatePresetsCard();

        var changed = targets.Count;
        RegisterUndo($"Rule applied to {changed} item(s)", () =>
        {
            foreach (var (node, previous) in previousRules)
            {
                if (previous is null)
                {
                    _ruleSet.RemoveRule(node.RelativePath, GetRuleKind(node));
                }
                else
                {
                    _ruleSet.SetRule(node.RelativePath, previous.Kind, previous.Mode);
                }
            }

            RefreshRulePresentation();
            UpdatePlan();
            UpdateDirtyState();
            UpdatePresetsCard();
        });

        if (targets.Count == 1)
        {
            var node = targets[0];
            StatusText = node.IsDirectory
                ? $"Directory rule applied recursively: {node.DisplayName} → {mode}"
                : $"File rule applied: {node.DisplayName} → {mode}";
            Toasts.Show($"{node.DisplayName} → {mode}", node.IsDirectory ? "The rule applies to the whole folder." : null, ToastKind.Info, "Undo", RunUndo);
        }
        else
        {
            StatusText = $"Rule applied to {changed} item(s) → {mode}";
            Toasts.Show($"{changed} item(s) → {mode}", null, ToastKind.Info, "Undo", RunUndo);
        }
    }

    public void ResetLocalRuleFor(FileTreeNode? node)
    {
        if (node is null || node.IsPlaceholder)
        {
            return;
        }

        var kind = GetRuleKind(node);
        var previous = _ruleSet.Rules.LastOrDefault(rule => rule.Kind == kind && string.Equals(rule.RelativePath, node.RelativePath, StringComparison.OrdinalIgnoreCase));
        var removed = _ruleSet.RemoveRule(node.RelativePath, kind);
        if (!removed)
        {
            StatusText = "The selected item has no local rule.";
            return;
        }

        RefreshRulePresentation();
        UpdatePlan();
        UpdateDirtyState();
        UpdatePresetsCard();
        StatusText = $"Local rule reset: {node.DisplayName}";
        if (previous is not null)
        {
            RegisterUndo($"Rule reset on {node.DisplayName}", () =>
            {
                _ruleSet.SetRule(previous.RelativePath, previous.Kind, previous.Mode);
                RefreshRulePresentation();
                UpdatePlan();
                UpdateDirtyState();
                UpdatePresetsCard();
            });
            Toasts.Show($"Local rule reset: {node.DisplayName}", null, ToastKind.Info, "Undo", RunUndo);
        }
    }

    public void AddPattern(bool isInclude, string pattern)
    {
        var trimmed = pattern.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var collection = isInclude ? Filters.IncludePatterns : Filters.ExcludePatterns;
        if (collection.Any(item => string.Equals(item.Text, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var item = new Patterns.PatternItem(trimmed);
        collection.Add(item);
        Filters.UpdateHasActivePatterns();
        ApplyFilterChanges();
        RegisterUndo($"Pattern added: {trimmed}", () =>
        {
            collection.Remove(item);
            Filters.UpdateHasActivePatterns();
            ApplyFilterChanges();
        });
        Toasts.Show($"Pattern added: {trimmed}", null, ToastKind.Info, "Undo", RunUndo);
    }

    public void RemovePattern(bool isInclude, Patterns.PatternItem item)
    {
        var collection = isInclude ? Filters.IncludePatterns : Filters.ExcludePatterns;
        var index = collection.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        collection.RemoveAt(index);
        Filters.UpdateHasActivePatterns();
        ApplyFilterChanges();
        RegisterUndo($"Pattern removed: {item.Text}", () =>
        {
            collection.Insert(Math.Min(index, collection.Count), item);
            Filters.UpdateHasActivePatterns();
            ApplyFilterChanges();
        });
        Toasts.Show($"Pattern removed: {item.Text}", null, ToastKind.Info, "Undo", RunUndo);
    }

    public void TogglePattern(bool isInclude, Patterns.PatternItem item)
    {
        var wasEnabled = item.IsEnabled;
        item.IsEnabled = !wasEnabled;
        Filters.UpdateHasActivePatterns();
        ApplyFilterChanges();
        RegisterUndo($"Pattern {(wasEnabled ? "disabled" : "enabled")}: {item.Text}", () =>
        {
            item.IsEnabled = wasEnabled;
            Filters.UpdateHasActivePatterns();
            ApplyFilterChanges();
        });
    }

    public void BulkSetExtensionsEnabled(IReadOnlyList<ExtensionRow> rows, bool enable)
    {
        foreach (var row in rows)
        {
            row.Enabled = enable;
        }

        ApplyExtensionChanges();
    }

    public void BulkSetExtensionsMode(IReadOnlyList<ExtensionRow> rows, CollectionMode mode)
    {
        foreach (var row in rows)
        {
            row.Mode = mode;
            row.Enabled = true;
        }

        ApplyExtensionChanges();
    }

    public bool TryRevealInTree(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = RuleSet.NormalizeRelativePath(relativePath);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        FileTreeNode? current = null;
        foreach (var rootNode in GetRootNodes())
        {
            current = FindNodeBySegments(rootNode, segments, 0);
            if (current is not null)
            {
                break;
            }
        }

        if (current is null)
        {
            return false;
        }

        RevealRequested?.Invoke(this, current);
        return true;
    }

    public void RefreshHistory()
    {
        History.SetEntries(_historyStore.GetEntries(_appPaths.OutputsDirectory));
    }

    // ===== ViewModel wiring =====

    private void WireViewModels()
    {
        Root.RootChangeRequested += (_, _) => RootChangeRequested?.Invoke(this, EventArgs.Empty);
        Root.TreeReloadRequested += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(Root.ScanRoot))
            {
                SetRoot(Root.ScanRoot);
            }
        };
        Root.InventoryRefreshRequested += (_, _) => _ = RefreshInventory(CancellationToken.None);
        Root.SegmentActivated += (_, path) => SetRoot(path);

        Presets.NewPresetRequested += OnNewPresetRequested;
        Presets.SavePresetRequested += (_, _) => { SaveCurrentPreset(); };
        Presets.SavePresetAsRequested += OnSavePresetAsRequested;
        Presets.RenamePresetRequested += OnRenamePresetRequested;
        Presets.DeletePresetRequested += OnDeletePresetRequested;
        Presets.DiscardChangesRequested += (_, _) =>
        {
            ActivatePreset(_presetRepository.Get(_activePreset.Id) ?? GetDefaultPreset());
            RefreshRulePresentation();
            UpdatePlan();
            StatusText = "Unsaved preset changes discarded.";
        };
        Presets.SwitchPresetRequested += (_, id) => TrySwitchPreset(id);

        Formats.EnableAllRequested += (_, _) => { SetAllExtensionsEnabled(true); ApplyExtensionChanges(); };
        Formats.DisableAllRequested += (_, _) => { SetAllExtensionsEnabled(false); ApplyExtensionChanges(); };
        Formats.InvertRequested += (_, _) =>
        {
            foreach (var row in _extensionRows)
            {
                row.Enabled = !row.Enabled;
            }

            ApplyExtensionChanges();
        };
        Formats.PropertyChanged += OnFormatsPropertyChange;

        Filters.PropertyChanged += OnFiltersPropertyChange;

        Tree.PropertyChanged += OnTreePropertyChange;
        Tree.BulkApplyModeRequested += (_, mode) =>
        {
            var nodes = Tree.MultiSelected.ToList();
            Tree.SetMultiSelection([]);
            ApplyModeToNodes(nodes, mode);
        };
        Tree.BulkResetRequested += (_, _) =>
        {
            var nodes = Tree.MultiSelected.ToList();
            Tree.SetMultiSelection([]);
            foreach (var node in nodes)
            {
                ResetLocalRuleFor(node);
            }
        };
        Tree.ClearMultiSelectionRequested += (_, _) => Tree.SetMultiSelection([]);
        Tree.ToggleLeftPanelRequested += (_, _) => ToggleLeftPanelCommand.Execute(null);
        Tree.ToggleRightPanelRequested += (_, _) => ToggleRightPanelCommand.Execute(null);
        Tree.CloseOnboardingRequested += (_, _) =>
        {
            PersistOnboardingSeen();
            UpdateOnboarding();
        };

        Inspector.ApplyModeRequested += (_, mode) =>
        {
            if (Inspector.SelectedNode is { } node)
            {
                ApplyModeToNodes([node], mode);
            }
        };
        Inspector.ResetLocalRuleRequested += (_, _) => ResetLocalRuleFor(Inspector.SelectedNode);
        Inspector.RevealInTreeRequested += (_, path) => { TryRevealInTree(path); };
        Inspector.PreviewRefreshRequested += (_, _) => UpdatePreview();

        History.RefreshRequested += (_, _) => RefreshHistory();
        History.OpenReportRequested += (_, entry) => OutputOpenRequested?.Invoke(this, entry.ReportPath);
        History.OpenManifestRequested += (_, entry) =>
        {
            if (entry.ManifestPath is not null && File.Exists(entry.ManifestPath))
            {
                OutputOpenRequested?.Invoke(this, entry.ManifestPath);
            }
        };
        History.OpenFolderRequested += (_, _) => OutputOpenRequested?.Invoke(this, _appPaths.OutputsDirectory);

        Palette.Opened += (_, _) =>
        {
            IsPaletteOpen = true;
            RebuildPaletteCommands();
        };
        Palette.Closed += (_, _) =>
        {
            IsPaletteOpen = false;
        };
        Palette.QueryChanged += (_, _) =>
        {
            _paletteDebounceTimer?.Dispose();
            _paletteDebounceTimer = new Timer(_ =>
            {
                _uiContext.Post(_ => UpdatePaletteResults(), null);
            }, null, TimeSpan.FromMilliseconds(120), Timeout.InfiniteTimeSpan);
        };
        Palette.ExecuteRequested += (_, entry) =>
        {
            Palette.Close();
            entry.Action?.Invoke();
        };

        PrefixPresets.StateChanged += OnPrefixPresetStateChanged;
    }

    private void OnNewPresetRequested(object? sender, PresetNameRequestEventArgs e)
    {
        var request = e;
        PresetNameRequested?.Invoke(this, request);
        if (!request.IsAccepted)
        {
            return;
        }

        var name = request.Name;
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
            GlobalMode = _activePreset.GlobalMode,
            ExtensionRules = [],
            PathRules = [],
            ScanOptions = new ScanOptions(),
            ScanRootPath = Root.ScanRoot,
            PrefixPresetId = PrefixPresets.SelectedId
        };
        _presetRepository.Save(preset);
        LoadPresetList();
        ActivatePreset(_presetRepository.Get(preset.Id) ?? preset);
        RefreshRulePresentation();
        UpdatePlan();
        StatusText = $"Preset created: {preset.Name}";
    }

    private void OnSavePresetAsRequested(object? sender, PresetNameRequestEventArgs e)
    {
        PresetNameRequested?.Invoke(this, e);
        if (!e.IsAccepted || e.Name is null)
        {
            return;
        }

        var name = e.Name;
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

    private void OnRenamePresetRequested(object? sender, PresetNameRequestEventArgs e)
    {
        PresetNameRequested?.Invoke(this, e);
        if (!e.IsAccepted || e.Name is null)
        {
            return;
        }

        var name = e.Name;
        var validationError = ValidatePresetName(name, _activePreset.Id);
        if (validationError is not null)
        {
            StatusText = validationError;
            return;
        }

        _activePreset.Name = name.Trim();
        SaveCurrentPreset();
    }

    private void OnDeletePresetRequested(object? sender, UnsavedChangesRequestEventArgs e)
    {
        DeletePresetRequested?.Invoke(this, e);
        if (e.Decision != UnsavedChangesDecision.Discard)
        {
            return;
        }

        if (_presetRepository.Delete(_activePreset.Id))
        {
            LoadPresetList();
            ActivatePreset(GetDefaultPreset());
            RefreshRulePresentation();
            UpdatePlan();
            StatusText = "Preset deleted.";
        }
    }

    private void OnFormatsPropertyChange(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FormatsViewModel.SearchText))
        {
            _formatSearchDebounceTimer?.Dispose();
            _formatSearchDebounceTimer = new Timer(_ =>
            {
                _uiContext.Post(_ => RebuildFormatsDisplay(), null);
            }, null, TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
        }
        else if (e.PropertyName is nameof(FormatsViewModel.SortKey) or nameof(FormatsViewModel.FilterKey))
        {
            RebuildFormatsDisplay();
        }
        else if (e.PropertyName == nameof(FormatsViewModel.IncludeAllExtensions))
        {
            if (_suppressFilterChanges || _activePreset is null)
            {
                return;
            }

            _activePreset.ScanOptions.IncludeAllExtensions = Formats.IncludeAllExtensions;
            UpdateDirtyState();
            UpdatePresetsCard();
            UpdatePlan();
            ApplyTreeFilter();
        }
    }

    private void OnFiltersPropertyChange(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(FiltersViewModel.IncludeHidden) or
            nameof(FiltersViewModel.IncludeSystem) or
            nameof(FiltersViewModel.FollowReparsePoints) or
            nameof(FiltersViewModel.BinaryFileMode) or
            nameof(FiltersViewModel.RedactRootPath) or
            nameof(FiltersViewModel.IncludeFileMetadataBlocks) or
            nameof(FiltersViewModel.InventoryRefreshMinutes) or
            nameof(FiltersViewModel.MaxFileSizeKiB))
        {
            if (_suppressFilterChanges)
            {
                return;
            }

            _filterDebounceTimer?.Dispose();
            _filterDebounceTimer = new Timer(_ =>
            {
                _uiContext.Post(_ => ApplyFilterChanges(), null);
            }, null, TimeSpan.FromMilliseconds(e.PropertyName == nameof(FiltersViewModel.MaxFileSizeKiB) ? 350 : 60), Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTreePropertyChange(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TreeViewModel.SearchText))
        {
            _searchDebounceTimer?.Dispose();
            _searchDebounceTimer = new Timer(_ =>
            {
                _uiContext.Post(_ => ApplyTreeFilter(), null);
            }, null, TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
        }
        else if (e.PropertyName is
            nameof(TreeViewModel.ShowFiles) or
            nameof(TreeViewModel.ShowFolders) or
            nameof(TreeViewModel.ShowLocalRulesOnly) or
            nameof(TreeViewModel.ShowExcludedOnly) or
            nameof(TreeViewModel.ShowLargeOnly) or
            nameof(TreeViewModel.ShowBinaryOnly) or
            nameof(TreeViewModel.ShowNoExtensionOnly))
        {
            ApplyTreeFilter();
        }
        else if (e.PropertyName == nameof(TreeViewModel.SelectedNode))
        {
            var node = Tree.SelectedNode;
            Inspector.SelectedNode = node;
            if (node is not null && !node.IsPlaceholder)
            {
                Inspector.ActiveTab = InspectorTab.Details;
                Inspector.UpdateDetails();
                Inspector.SetRuleSteps(BuildRuleSteps(node));
                StatusText = node.ToolTipText;
            }
        }
        else if (e.PropertyName == nameof(TreeViewModel.ActiveSection))
        {
            if (Tree.ActiveSection != TreeSection.Explorer)
            {
                _lastNonExplorerSection = Tree.ActiveSection;
            }

            if (Tree.ActiveSection == TreeSection.History)
            {
                RefreshHistory();
            }
        }
    }

    private void OnPrefixPresetStateChanged(object? sender, EventArgs e)
    {
        UpdatePreview();
        if (!_suppressPrefixPresetState && _activePreset is not null)
        {
            UpdateDirtyState();
        }
    }

    // ===== Preset logic =====

    private void LoadPresetList()
    {
        Presets.PresetItems.Clear();
        foreach (var preset in _presetRepository.GetAll())
        {
            Presets.PresetItems.Add(new PresetListItem(preset.Id, preset.Name, preset.Id == PresetDefaults.DefaultPresetId));
        }
    }

    private Preset GetDefaultPreset()
    {
        return _presetRepository.Get(PresetDefaults.DefaultPresetId)
            ?? throw new InvalidOperationException("The default preset could not be loaded.");
    }

    private string? ValidatePresetName(string name, Guid? currentId)
    {
        return PresetNameValidator.Validate(name, Presets.PresetItems, currentId);
    }

    private void TrySwitchPreset(Guid id)
    {
        if (Presets.IsDirty)
        {
            var request = new UnsavedChangesRequestEventArgs(Presets.ActivePresetName);
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
            : Root.ScanRoot;
        SetRoot(presetRoot);
        ResetPresetDirtyState();
        RefreshRulePresentation();
        UpdatePlan();
        StatusText = $"Preset selected: {preset.Name}";
    }

    private bool SaveCurrentPreset()
    {
        try
        {
            var preset = CreateCurrentPreset(_activePreset.Id, _activePreset.Name, _activePreset.CreatedAt, _activePreset.UpdatedAt);
            _presetRepository.Save(preset);
            LoadPresetList();
            ActivatePreset(_presetRepository.Get(preset.Id) ?? preset);
            Presets.SelectedPresetId = preset.Id;
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
        Presets.ActivePresetName = preset.Name;
        Presets.IsDefaultPreset = preset.Id == PresetDefaults.DefaultPresetId;
        Presets.IsDirty = false;
        Presets.SelectedPresetId = preset.Id;
        UpdatePresetsCard();
    }

    private void UpdateDirtyState()
    {
        Presets.IsDirty = !ArePresetStatesEqual(CreatePresetState(), _savedPresetState);
    }

    private void ResetPresetDirtyState()
    {
        _savedPresetState = CreatePresetState();
        Presets.IsDirty = false;
    }

    private void UpdatePresetsCard()
    {
        Presets.RefreshCard(
            _ruleSet.Rules.Count,
            _activePreset.ExtensionRules.Count,
            Filters.IncludePatterns.Count + Filters.ExcludePatterns.Count,
            SizeFormatter.Format(_activePreset.ScanOptions.MaxFileSizeBytes),
            _activePreset.ScanOptions.BinaryFileMode.ToString(),
            string.IsNullOrEmpty(Root.ScanRoot) ? "—" : Root.ScanRoot,
            PrefixPresets.SelectedName,
            _activePreset.UpdatedAt.ToString("yyyy-MM-dd HH:mm"));
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
            ScanRootPath = Root.ScanRoot,
            PrefixPresetId = PrefixPresets.SelectedId
        };
    }

    private PresetState CreatePresetState()
    {
        return new PresetState(
            _activePreset.GlobalMode,
            Root.ScanRoot,
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
            Filters.GetIncludePatterns().ToArray(),
            Filters.GetExcludePatterns().ToArray());
    }

    private void LoadFilterSettings()
    {
        _suppressFilterChanges = true;
        Formats.IncludeAllExtensions = _activePreset.ScanOptions.IncludeAllExtensions;
        Filters.IncludeHidden = _activePreset.ScanOptions.IncludeHidden;
        Filters.IncludeSystem = _activePreset.ScanOptions.IncludeSystem;
        Filters.FollowReparsePoints = _activePreset.ScanOptions.FollowReparsePoints;
        Filters.MaxFileSizeKiB = Math.Max(1, (int)Math.Min(int.MaxValue, _activePreset.ScanOptions.MaxFileSizeBytes / 1024));
        Filters.BinaryFileMode = _activePreset.ScanOptions.BinaryFileMode;
        Filters.RedactRootPath = _activePreset.ScanOptions.RedactRootPath;
        Filters.IncludeFileMetadataBlocks = _activePreset.ScanOptions.IncludeFileMetadataBlocks;
        Filters.InventoryRefreshMinutes = _activePreset.ScanOptions.InventoryRefreshMinutes;
        Filters.LoadPatterns(_activePreset.ScanOptions.IncludePatterns, _activePreset.ScanOptions.ExcludePatterns);
        _suppressFilterChanges = false;
    }

    private void ApplyFilterChanges()
    {
        if (_suppressFilterChanges || _activePreset is null)
        {
            return;
        }

        _activePreset.ScanOptions.IncludeAllExtensions = Formats.IncludeAllExtensions;
        _activePreset.ScanOptions.IncludeHidden = Filters.IncludeHidden;
        _activePreset.ScanOptions.IncludeSystem = Filters.IncludeSystem;
        _activePreset.ScanOptions.FollowReparsePoints = Filters.FollowReparsePoints;
        _activePreset.ScanOptions.MaxFileSizeBytes = Math.Max(1, Filters.MaxFileSizeKiB) * 1024L;
        _activePreset.ScanOptions.BinaryFileMode = Filters.BinaryFileMode;
        _activePreset.ScanOptions.RedactRootPath = Filters.RedactRootPath;
        _activePreset.ScanOptions.IncludeFileMetadataBlocks = Filters.IncludeFileMetadataBlocks;
        _activePreset.ScanOptions.InventoryRefreshMinutes = Math.Max(0, Filters.InventoryRefreshMinutes);
        _activePreset.ScanOptions.IncludePatterns = Filters.GetIncludePatterns().ToList();
        _activePreset.ScanOptions.ExcludePatterns = Filters.GetExcludePatterns().ToList();
        UpdateDirtyState();
        UpdatePresetsCard();
        ConfigureInventoryRefreshTimer();
        UpdatePlan();
        ApplyTreeFilter();
        RebuildHistogram();
    }

    private void ApplyExtensionChanges()
    {
        if (_suppressFilterChanges || _activePreset is null)
        {
            return;
        }

        _activePreset.ExtensionRules = _extensionRows
            .Where(row => row.Enabled != _activePreset.ScanOptions.IncludeAllExtensions || row.Mode != CollectionMode.Full)
            .Select(row => new ExtensionRule(row.Extension, row.Enabled, row.Mode))
            .ToList();
        UpdateDirtyState();
        UpdatePresetsCard();
        UpdatePlan();
        ApplyTreeFilter();
    }

    private void OnExtensionRowChanged(object? sender, EventArgs e)
    {
        if (_suppressFilterChanges || sender is not ExtensionRow item)
        {
            return;
        }

        ApplyExtensionChanges();
    }

    private void SetAllExtensionsEnabled(bool enable)
    {
        foreach (var row in _extensionRows)
        {
            row.Enabled = enable;
        }
    }

    // ===== Inventory =====

    [RelayCommand]
    private async Task RefreshInventory(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Root.ScanRoot) || !Directory.Exists(Root.ScanRoot))
        {
            Root.InventoryStatus = "Inventory cannot be refreshed because the scan root is unavailable.";
            return;
        }

        _inventoryRefreshCancellation?.Cancel();
        _inventoryRefreshCancellation?.Dispose();
        _inventoryRefreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Root.IsRefreshingInventory = true;
        Root.InventoryStatus = "Refreshing inventory...";
        var rootPath = Root.ScanRoot;
        var excludedDirectoryPath = _excludedDirectoryPath;
        var progress = new Progress<InventoryRefreshProgress>(value =>
        {
            Root.InventoryStatus = $"Scanning… {value.DiscoveredFiles:N0} files, {value.DiscoveredDirectories:N0} folders";
        });

        try
        {
            var snapshot = await Task.Run(
                () => _inventoryStore.Refresh(rootPath, excludedDirectoryPath, progress, _inventoryRefreshCancellation.Token),
                _inventoryRefreshCancellation.Token);
            if (string.Equals(Root.ScanRoot, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                _inventorySnapshot = snapshot;
                _firstInventoryLoaded = true;
                Root.InventoryStatus = $"Inventory updated: {snapshot.Files.Count:N0} files";
                UpdatePlan();
                UpdateOnboarding();
            }
        }
        catch (OperationCanceledException)
        {
            Root.InventoryStatus = "Inventory refresh canceled.";
        }
        catch (IOException exception)
        {
            Root.InventoryStatus = $"Inventory refresh failed: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            Root.InventoryStatus = $"Inventory refresh failed: {exception.Message}";
        }
        finally
        {
            Root.IsRefreshingInventory = false;
        }
    }

    private void ConfigureInventoryRefreshTimer()
    {
        _inventoryRefreshTimer?.Dispose();
        var intervalMinutes = Math.Max(0, Filters.InventoryRefreshMinutes);
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
                if (!Root.IsRefreshingInventory)
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
        if (string.IsNullOrWhiteSpace(Root.ScanRoot) || !Directory.Exists(Root.ScanRoot))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(Root.ScanRoot)
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
            Root.InventoryStatus = "Inventory watcher is unavailable. Periodic refresh remains active.";
        }
        catch (ArgumentException)
        {
            Root.InventoryStatus = "Inventory watcher is unavailable. Periodic refresh remains active.";
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
                if (!Root.IsRefreshingInventory)
                {
                    _ = RefreshInventory(CancellationToken.None);
                }
            }, null);
        }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    // ===== Plan =====

    private void UpdatePlan()
    {
        if (_inventorySnapshot is null)
        {
            Inspector.Plan.MarkUnavailable();
            RebuildFormatsDisplay();
            UpdatePreview();
            return;
        }

        var snapshot = _inventorySnapshot;
        var plan = _collectionPlanner.CreatePlan(snapshot, _ruleSet, _activePreset.ExtensionRules, _activePreset.ScanOptions);
        _currentPlan = plan;
        _planItemsByPath = plan.Items.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        var extensionSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var totalSize = 0L;
        foreach (var file in snapshot.Files)
        {
            var size = file.SizeBytes ?? 0;
            totalSize += size;
            extensionSizes[file.Extension] = extensionSizes.GetValueOrDefault(file.Extension, 0) + size;
        }

        Inspector.Plan.Refresh(plan, extensionSizes, totalSize);

        BuildFolderAggregates();
        RebuildExtensionRows(snapshot, extensionSizes);
        Formats.EffectSummary = $"In report: {plan.FullCount + plan.SignaturesCount:N0} files · {SizeFormatter.Format(plan.EstimatedBytes)}";
        RebuildFormatsDisplay();
        UpdatePatternMatchCounts(snapshot);
        RebuildHistogram();

        foreach (var rootNode in GetRootNodes())
        {
            ApplyPlanDataToSubtree(rootNode);
        }

        UpdatePreview();
        GenerateReportCommand.NotifyCanExecuteChanged();
    }

    private void RebuildExtensionRows(FileInventorySnapshot snapshot, IReadOnlyDictionary<string, long> extensionSizes)
    {
        foreach (var row in _extensionRows)
        {
            row.Changed -= OnExtensionRowChanged;
        }

        _extensionRows = [];
        var allExtensions = snapshot.ExtensionCounts.Keys
            .Concat(_activePreset.ExtensionRules.Select(rule => rule.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalSize = Math.Max(1L, snapshot.Files.Sum(file => file.SizeBytes ?? 0));
        foreach (var extension in allExtensions)
        {
            var rule = _activePreset.ExtensionRules.LastOrDefault(item => string.Equals(item.Extension, extension, StringComparison.OrdinalIgnoreCase));
            var row = new ExtensionRow(
                extension,
                snapshot.ExtensionCounts.GetValueOrDefault(extension),
                extensionSizes.GetValueOrDefault(extension),
                rule?.Enabled ?? _activePreset.ScanOptions.IncludeAllExtensions,
                rule?.Mode ?? CollectionMode.Full);
            row.SizeShare = extensionSizes.GetValueOrDefault(extension) * 1.0 / totalSize;
            row.Changed += OnExtensionRowChanged;
            _extensionRows.Add(row);
        }

        Formats.DisabledCount = _extensionRows.Count(row => !row.Enabled);
        Formats.EffectSummary = string.Empty;
    }

    private void RebuildFormatsDisplay()
    {
        var rows = _extensionRows.ToList();

        // Apply the quick filter.
        switch (Formats.FilterKey)
        {
            case ExtensionFilter.Enabled:
                rows = rows.Where(row => row.Enabled).ToList();
                break;
            case ExtensionFilter.Disabled:
                rows = rows.Where(row => !row.Enabled).ToList();
                break;
            case ExtensionFilter.Binary:
                rows = rows.Where(row => row.IsBinary).ToList();
                break;
            case ExtensionFilter.NoExtension:
                rows = rows.Where(row => !row.HasExtension).ToList();
                break;
        }

        var search = Formats.SearchText.Trim();
        if (search.Length > 0)
        {
            rows = rows.Where(row => row.Extension.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        rows = Formats.SortKey switch
        {
            ExtensionSort.Files => rows.OrderByDescending(row => row.Count).ThenBy(row => row.Extension, StringComparer.OrdinalIgnoreCase).ToList(),
            ExtensionSort.Size => rows.OrderByDescending(row => row.SizeBytes).ThenBy(row => row.Extension, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => rows.OrderBy(row => row.Extension, StringComparer.OrdinalIgnoreCase).ToList()
        };

        Formats.Rows.Clear();
        foreach (var row in rows)
        {
            Formats.Rows.Add(row);
        }
    }

    private void UpdatePatternMatchCounts(FileInventorySnapshot snapshot)
    {
        var includeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Filters.IncludePatterns)
        {
            includeCounts[item.Text] = GlobStats.CountMatches(snapshot, item.Text);
        }

        var excludeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Filters.ExcludePatterns)
        {
            excludeCounts[item.Text] = GlobStats.CountMatches(snapshot, item.Text);
        }

        Filters.SetPatternMatchCounts(includeCounts, excludeCounts);
    }

    private void RebuildHistogram()
    {
        if (_inventorySnapshot is null)
        {
            Filters.HistogramBuckets = [];
            Filters.HistogramMaxKiB = 10240;
            return;
        }

        var maxFileKiB = 0.0;
        foreach (var file in _inventorySnapshot.Files)
        {
            if (file.SizeBytes is { } size)
            {
                maxFileKiB = Math.Max(maxFileKiB, size / 1024d);
            }
        }

        var target = Math.Max(10240.0, maxFileKiB);
        var power = 1024.0;
        while (power * 2 < target && power < 1048576.0)
        {
            power *= 2;
        }

        var maxKiB = Math.Min(1048576.0, Math.Max(target, power));

        const int bucketCount = 48;
        var counts = new long[bucketCount];
        foreach (var file in _inventorySnapshot.Files)
        {
            if (file.SizeBytes is not { } size)
            {
                continue;
            }

            var kiB = size / 1024d;
            if (kiB >= maxKiB)
            {
                continue;
            }

            var index = (int)(kiB / maxKiB * bucketCount);
            if (index >= 0 && index < bucketCount)
            {
                counts[index]++;
            }
        }

        var maxCount = counts.Max();
        var thresholdKiB = (double)Filters.MaxFileSizeKiB;
        Filters.HistogramBuckets = counts
            .Select((count, index) =>
            {
                var bucketStart = index / (double)bucketCount * maxKiB;
                return new Controls.SizeBucket(index, count, maxCount > 0 ? (double)count / maxCount : 0, bucketStart > thresholdKiB);
            })
            .ToList();
        Filters.HistogramMaxKiB = maxKiB;
    }

    private void UpdatePreview()
    {
        Inspector.RefreshPreview(
            PrefixPresets.Content,
            _currentPlan,
            Presets.ActivePresetName,
            PrefixPresets.SelectedName,
            Root.ScanRoot,
            _activePreset.ScanOptions.RedactRootPath,
            _activePreset.ScanOptions.IncludeFileMetadataBlocks);
    }

    // ===== Tree presentation =====

    private IEnumerable<FileTreeNode> GetRootNodes()
    {
        // The tree root collection is owned by the view model through the window;
        // we keep it accessible via a dedicated field populated in SetRoot.
        return _rootNodes;
    }

    private readonly List<FileTreeNode> _rootNodes = [];

    private void AddRootNode(FileTreeNode node)
    {
        _rootNodes.Clear();
        _rootNodes.Add(node);
        Tree.RootNodes.Add(node);
    }

    private void UnloadAllNodes()
    {
        _rootNodes.Clear();
        Tree.RootNodes.Clear();
    }

    private void RefreshRulePresentation()
    {
        foreach (var rootNode in _rootNodes)
        {
            RefreshRulePresentation(rootNode);
        }

        UpdatePlan();
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

    private void ApplyPlanDataToSubtree(FileTreeNode node)
    {
        if (!node.IsPlaceholder)
        {
            ApplyPlanData(node);
        }

        foreach (var child in node.Children)
        {
            ApplyPlanDataToSubtree(child);
        }
    }

    private void ApplyPlanData(FileTreeNode node)
    {
        if (node.IsDirectory)
        {
            var key = node.RelativePath.Length == 0 ? string.Empty : RuleSet.NormalizeRelativePath(node.RelativePath);
            if (_folderAggregates.TryGetValue(key, out var aggregate))
            {
                node.SizeDisplay = $"{aggregate.Count:N0} · {SizeFormatter.FormatCompact(aggregate.SizeBytes)}";
            }
            else if (_inventorySnapshot is not null)
            {
                node.SizeDisplay = string.Empty;
            }

            return;
        }

        if (!_planItemsByPath.TryGetValue(node.RelativePath, out var item))
        {
            node.SizeDisplay = string.Empty;
            return;
        }

        node.PlanSizeBytes = item.SizeBytes;
        node.PlanReason = item.Reason;
        if (item.Mode != node.EffectiveMode)
        {
            node.EffectiveMode = item.Mode;
            node.NotifyDerivedPropertiesChanged();
        }

        var extensionExtension = Path.GetExtension(node.DisplayName).ToLowerInvariant();
        var isExtensionDriven = node.RuleSource == RuleSource.Global && item.Mode != _activePreset.GlobalMode;
        node.SourceGlyph = node.RuleSource switch
        {
            RuleSource.Local => "L",
            RuleSource.Inherited => "I",
            RuleSource.System => "S",
            _ => isExtensionDriven ? "E" : "·"
        };
        node.SourceDescription = node.RuleSource switch
        {
            RuleSource.Local => "A local rule is set on this item.",
            RuleSource.Inherited => "Inherited from a folder rule.",
            RuleSource.System => "System exclusion.",
            _ => isExtensionDriven ? $"Extension rule for {extensionExtension}." : $"Global default mode ({_activePreset.GlobalMode})."
        };

        node.SizeDisplay = item.SizeBytes is { } size ? SizeFormatter.FormatCompact(size) : string.Empty;
    }

    private void BuildFolderAggregates()
    {
        var aggregates = new Dictionary<string, (int Count, long SizeBytes)>(StringComparer.OrdinalIgnoreCase);
        if (_inventorySnapshot is null)
        {
            _folderAggregates = aggregates;
            return;
        }

        foreach (var file in _inventorySnapshot.Files)
        {
            var current = Path.GetDirectoryName(file.RelativePath.Replace('\\', '/')) ?? string.Empty;
            while (true)
            {
                if (aggregates.TryGetValue(current, out var value))
                {
                    aggregates[current] = (value.Count + 1, value.SizeBytes + (file.SizeBytes ?? 0));
                }
                else
                {
                    aggregates[current] = (1, file.SizeBytes ?? 0);
                }

                var parent = Path.GetDirectoryName(current);
                if (parent is null || parent.Length == 0)
                {
                    break;
                }

                current = parent;
            }
        }

        _folderAggregates = aggregates;
    }

    private void ApplyTreeFilter()
    {
        foreach (var rootNode in _rootNodes)
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

        var matchesText = string.IsNullOrWhiteSpace(Tree.SearchText) ||
            node.DisplayName.Contains(Tree.SearchText, StringComparison.OrdinalIgnoreCase) ||
            node.RelativePath.Contains(Tree.SearchText, StringComparison.OrdinalIgnoreCase);
        var matchesKind = node.IsDirectory ? Tree.ShowFolders : Tree.ShowFiles;
        var matchesRule = !Tree.ShowLocalRulesOnly || node.HasLocalRule;
        var matchesMode = !Tree.ShowExcludedOnly || node.EffectiveMode == CollectionMode.Excluded;

        var matchesFileSpecific = true;
        if (Tree.ShowLargeOnly || Tree.ShowBinaryOnly || Tree.ShowNoExtensionOnly)
        {
            if (node.IsDirectory)
            {
                matchesFileSpecific = false;
            }
            else
            {
                if (Tree.ShowLargeOnly && !node.IsLargeFile)
                {
                    matchesFileSpecific = false;
                }

                if (Tree.ShowBinaryOnly && !node.IsBinaryFile)
                {
                    matchesFileSpecific = false;
                }

                if (Tree.ShowNoExtensionOnly && node.HasExtension)
                {
                    matchesFileSpecific = false;
                }
            }
        }

        node.IsVisible = (matchesText && matchesKind && matchesRule && matchesMode && matchesFileSpecific) || childMatches;
        return node.IsVisible;
    }

    private FileTreeNode? FindNodeBySegments(FileTreeNode start, string[] segments, int index)
    {
        if (index == segments.Length)
        {
            return start;
        }

        if (start.IsDirectory && !start.AreChildrenLoaded && start.IsAccessible && !start.IsReparsePoint)
        {
            LoadChildren(start);
        }

        foreach (var child in start.Children)
        {
            if (child.IsPlaceholder)
            {
                continue;
            }

            if (string.Equals(child.DisplayName, segments[index], StringComparison.OrdinalIgnoreCase))
            {
                var found = FindNodeBySegments(child, segments, index + 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private string GetRelativePath(string fullPath)
    {
        return RuleSet.NormalizeRelativePath(Path.GetRelativePath(Root.ScanRoot, fullPath));
    }

    // ===== Rule chain (inspector) =====

    public IEnumerable<RuleStepInfo> BuildRuleSteps(FileTreeNode node)
    {
        var steps = new List<RuleStepInfo>();
        var kind = GetRuleKind(node);
        var relativePath = RuleSet.NormalizeRelativePath(node.RelativePath);

        steps.Add(new RuleStepInfo("System exclusion", "The application folder is never collected.", CollectionMode.Excluded, false));

        var localRule = _ruleSet.Rules.LastOrDefault(rule => rule.Kind == kind && string.Equals(rule.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        steps.Add(new RuleStepInfo("Local rule", localRule is null ? null : "Set on this item", localRule?.Mode, false));

        var inheritedRule = _ruleSet.Rules
            .Where(rule => rule.Kind == PathRuleKind.Directory && (string.IsNullOrEmpty(rule.RelativePath) || relativePath.StartsWith(rule.RelativePath + "/", StringComparison.OrdinalIgnoreCase) || string.Equals(relativePath, rule.RelativePath, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(rule => rule.RelativePath.Length)
            .FirstOrDefault();
        steps.Add(new RuleStepInfo("Folder rule (inherited)", inheritedRule is null ? null : inheritedRule.RelativePath.Length == 0 ? "(root folder)" : inheritedRule.RelativePath, inheritedRule?.Mode, false));

        var extension = Path.GetExtension(node.DisplayName).ToLowerInvariant();
        var normalizedExtension = extension.Length == 0 ? "[no extension]" : extension;
        var extensionRule = _activePreset.ExtensionRules.LastOrDefault(rule => string.Equals(CheckExtension(rule.Extension), normalizedExtension, StringComparison.OrdinalIgnoreCase));
        steps.Add(new RuleStepInfo(
            "Extension rule",
            extensionRule is null ? null : extensionRule.Enabled ? null : "disabled",
            extensionRule is { Enabled: true } ? extensionRule.Mode : null,
            false));

        steps.Add(new RuleStepInfo("Global default", null, _activePreset.GlobalMode, false));

        CollectionMode? finalMode = null;
        string? finalDetail = null;
        if (!node.IsDirectory && _planItemsByPath.TryGetValue(node.RelativePath, out var item))
        {
            finalMode = item.Mode;
            finalDetail = string.IsNullOrEmpty(item.Reason) ? null : ReasonCatalog.Describe(item.Reason);
        }
        else
        {
            finalMode = node.EffectiveMode;
        }

        steps.Add(new RuleStepInfo("Final plan decision", finalDetail, finalMode, false));

        // Mark the winner.
        var winnerIndex = node.RuleSource switch
        {
            RuleSource.System => 0,
            RuleSource.Local => 1,
            RuleSource.Inherited => 2,
            _ => -1
        };

        if (winnerIndex < 0)
        {
            var extensionApplied = extensionRule is { Enabled: true } &&
                node.EffectiveMode == extensionRule.Mode &&
                extensionRule.Mode != _activePreset.GlobalMode;
            winnerIndex = extensionApplied ? 3 : 4;
        }

        // If the final plan decision overrides the rule winner (size limit, binary mode, patterns), the final step wins.
        if (finalMode is { } final && final != steps[winnerIndex].Mode)
        {
            winnerIndex = steps.Count - 1;
        }

        steps[winnerIndex] = steps[winnerIndex] with { IsWinner = true };
        return steps;
    }

    private static string CheckExtension(string extension)
    {
        if (string.Equals(extension, "[no extension]", StringComparison.OrdinalIgnoreCase))
        {
            return extension;
        }

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    // ===== Breadcrumbs, paths, onboarding =====

    private static List<BreadcrumbSegment> BuildBreadcrumbSegments(string rootPath)
    {
        var segments = new List<BreadcrumbSegment>();
        try
        {
            var directory = new DirectoryInfo(rootPath.TrimEnd(Path.DirectorySeparatorChar));
            var labels = new List<string>();
            var paths = new List<string>();
            while (directory is not null)
            {
                labels.Add(directory.Name.Length == 0 ? directory.FullName : directory.Name);
                paths.Add(directory.FullName);
                directory = directory.Parent;
            }

            labels.Reverse();
            paths.Reverse();
            if (labels.Count > 5)
            {
                var visible = new List<(string Label, string Path)>
                {
                    (labels[0], paths[0]),
                    ("…", paths[labels.Count - 3])
                };
                for (var i = labels.Count - 3; i < labels.Count; i++)
                {
                    visible.Add((labels[i], paths[i]));
                }

                return visible.Select(item => new BreadcrumbSegment(item.Label, item.Path)).ToList();
            }

            for (var i = 0; i < labels.Count; i++)
            {
                segments.Add(new BreadcrumbSegment(labels[i], paths[i]));
            }
        }
        catch (IOException)
        {
            segments.Add(new BreadcrumbSegment(rootPath, rootPath));
        }
        catch (ArgumentException)
        {
            segments.Add(new BreadcrumbSegment(rootPath, rootPath));
        }

        return segments;
    }

    private void UpdateOnboarding()
    {
        var show = !Root.HasUsableRoot || (!_uiSettings.Settings.HasSeenOnboarding && !_firstInventoryLoaded);
        Tree.IsOnboardingVisible = show;
    }

    private void PersistOnboardingSeen()
    {
        var settings = _uiSettings.Settings;
        if (!settings.HasSeenOnboarding)
        {
            _uiSettings.Save(settings with { HasSeenOnboarding = true });
        }
    }

    // ===== Undo =====

    private void RegisterUndo(string label, Action undo)
    {
        _lastUndo = (label, undo);
    }

    private void RunUndo()
    {
        if (_lastUndo is { } entry)
        {
            _lastUndo = null;
            entry.Undo();
            StatusText = $"Undone: {entry.Label}";
        }
    }

    // ===== Palette =====

    private void RebuildPaletteCommands()
    {
        var commands = new List<PaletteEntry>
        {
            new("Change scan root…", "Pick a different folder to scan", null, PaletteEntryKind.Command, () => RootChangeRequested?.Invoke(this, EventArgs.Empty)),
            new("Refresh tree", "Re-read the visible tree", "F5", PaletteEntryKind.Command, () => SetRoot(Root.ScanRoot)),
            new("Refresh inventory", "Full background scan of the root", null, PaletteEntryKind.Command, () => _ = RefreshInventory(CancellationToken.None)),
            new("Create report", "Generate the Markdown report and manifest", null, PaletteEntryKind.Command, () => { if (GenerateReportCommand.CanExecute(null)) { _ = GenerateReport(CancellationToken.None); } }),
            new("Open last report", $"Last: {(string.IsNullOrEmpty(LastReportName) ? "none" : LastReportName)}", "Ctrl+O", PaletteEntryKind.Command, () => { if (CanOpenOutput()) { OpenOutput(); } }),
            new("Open outputs folder", _appPaths.OutputsDirectory, "Ctrl+Shift+O", PaletteEntryKind.Command, OpenOutputs),
            new("Save preset", "Save the active preset", "Ctrl+S", PaletteEntryKind.Command, () => { SaveCurrentPreset(); }),
            new("Save preset as…", "Copy the active preset under a new name", "Ctrl+Shift+S", PaletteEntryKind.Command, () => Presets.SavePresetAsCommand.Execute(null)),
            new("Discard preset changes", "Restore the saved preset state", null, PaletteEntryKind.Command, () => Presets.DiscardChangesCommand.Execute(null)),
            new("Toggle theme", ThemeManager.IsDark ? "Switch to the light theme" : "Switch to the dark theme", null, PaletteEntryKind.Command, () => ThemeToggleRequested?.Invoke(this, EventArgs.Empty)),
            new("Toggle density", ThemeManager.AppliedDensity == DensityMode.Compact ? "Comfortable density" : "Compact density", null, PaletteEntryKind.Command, () => DensityToggleRequested?.Invoke(this, EventArgs.Empty)),
            new("Toggle left panel", "Show/hide the settings panel", "Ctrl+B", PaletteEntryKind.Command, () => ToggleLeftPanelCommand.Execute(null)),
            new("Toggle right panel", "Show/hide the inspector", "Ctrl+J", PaletteEntryKind.Command, () => ToggleRightPanelCommand.Execute(null)),
            new("Show plan full-screen", "Expand the plan to the whole workspace", null, PaletteEntryKind.Command, () => { Inspector.ActiveTab = InspectorTab.Plan; IsInspectorExpanded = true; }),
            new("Show preview full-screen", "Expand the preview to the whole workspace", null, PaletteEntryKind.Command, () => { Inspector.ActiveTab = InspectorTab.Preview; IsInspectorExpanded = true; }),
            new("About", $"Version {ApplicationVersion}", null, PaletteEntryKind.Command, () => AboutRequested?.Invoke(this, EventArgs.Empty)),
        };

        if (Tree.SelectedNode is { IsPlaceholder: false } selected)
        {
            var name = selected.DisplayName;
            commands.Add(new($"Exclude {name}", "Set the collection mode to Excluded", null, PaletteEntryKind.Command, () => ApplyModeToNodes([selected], CollectionMode.Excluded)));
            commands.Add(new($"Reset rule on {name}", "Remove the local rule", null, PaletteEntryKind.Command, () => ResetLocalRuleFor(selected)));
        }

        Palette.SetEntries(commands);
    }

    private void UpdatePaletteResults()
    {
        if (!Palette.IsOpen)
        {
            return;
        }

        var query = Palette.Query.Trim();
        if (query.Length == 0)
        {
            RebuildPaletteCommands();
            return;
        }

        var results = new List<PaletteEntry>();
        RebuildPaletteCommands();
        foreach (var command in Palette.Entries.ToList())
        {
            if (command.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (command.Detail?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                results.Add(command);
            }
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_inventorySnapshot is not null)
        {
            foreach (var file in _inventorySnapshot.Files)
            {
                if (results.Count >= 20)
                {
                    break;
                }

                if (file.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(file.RelativePath).Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    if (seenPaths.Add(file.RelativePath))
                    {
                        var path = file.RelativePath;
                        results.Add(new(Path.GetFileName(file.RelativePath), path, null, PaletteEntryKind.File, () => TryRevealInTree(path)));
                    }
                }
            }
        }

        Palette.SetEntries(results.Take(25).ToList());
    }

    // ===== Helpers =====

    private bool CanGenerateReport()
    {
        return !IsGeneratingReport && _currentPlan is not null;
    }

    private bool CanOpenOutput()
    {
        return !string.IsNullOrWhiteSpace(LastOutputPath) && File.Exists(LastOutputPath);
    }

    partial void OnLastOutputPathChanged(string? value)
    {
        OpenOutputCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsGeneratingReportChanged(bool value)
    {
        GenerateReportCommand.NotifyCanExecuteChanged();
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

    private void RestorePresetSelection()
    {
        Presets.SelectedPresetId = _activePreset.Id;
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
