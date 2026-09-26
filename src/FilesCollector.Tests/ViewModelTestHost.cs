using FilesCollector.App;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Signatures;
using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.FileSystem;
using FilesCollector.Infrastructure.Prefixes;
using FilesCollector.Infrastructure.Presets;
using FilesCollector.Infrastructure.Reporting;
using FilesCollector.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FilesCollector.Tests;

/// <summary>
/// Creates a <see cref="MainWindowViewModel"/> on real infrastructure in a temporary
/// folder, laid out like the quick start: the application folder lives inside the
/// analysed project, so the project is the default scan root.
/// </summary>
/// <remarks>
/// Create the host inside the test method and dispose it there (<c>using var</c>): it
/// installs a synchronization context that drops every posted callback, so background
/// continuations (inventory refresh, timers, file watcher) never run and the view model
/// is changed by the test thread only. Dispose restores the previous context.
/// </remarks>
public sealed class ViewModelTestHost : IDisposable
{
    private readonly SynchronizationContext? _previousContext;

    public ViewModelTestHost(string testDirectory)
    {
        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DroppingSynchronizationContext());

        ProjectRoot = Path.GetFullPath(Path.Combine(testDirectory, "project"));
        ApplicationDirectory = Path.Combine(ProjectRoot, "files-collector");
        Directory.CreateDirectory(ApplicationDirectory);
        var sourceFile = Path.Combine(ProjectRoot, "src", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, "class Program { }\n");

        AppPaths = new AppPaths(Path.Combine(ApplicationDirectory, "FilesCollector.exe"), Path.Combine(testDirectory, "local"));
        var fileSystem = new WindowsFileSystem();
        Inventory = new StubInventoryStore(new FileInventorySnapshot
        {
            RootPath = ProjectRoot,
            Files = [new FileInventoryEntry(sourceFile, "src/Program.cs", ".cs", 18, false, false, true, null)],
            ExtensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [".cs"] = 1 }
        });
        ContentProbe = new SampledFileContentProbe();
        PresetRepository = new JsonPresetRepository(AppPaths, NullLogger<JsonPresetRepository>.Instance);
        ViewModel = new MainWindowViewModel(
            AppPaths,
            new ScanRootProvider(AppPaths),
            fileSystem,
            Inventory,
            new CollectionPlanner(fileSystem),
            new MarkdownReportWriter(AppPaths, new SystemClock(), Array.Empty<ISignatureExtractor>()),
            PresetRepository,
            new JsonAppSessionStore(AppPaths),
            new PrefixPresetsViewModel(new JsonPrefixPresetRepository(AppPaths, NullLogger<JsonPrefixPresetRepository>.Instance)),
            ContentProbe,
            NullLogger<MainWindowViewModel>.Instance);
    }

    public string ProjectRoot { get; }

    public string ApplicationDirectory { get; }

    public AppPaths AppPaths { get; }

    public StubInventoryStore Inventory { get; }

    public JsonPresetRepository PresetRepository { get; }

    public SampledFileContentProbe ContentProbe { get; }

    public MainWindowViewModel ViewModel { get; }

    public void Dispose()
    {
        ViewModel.Shutdown();
        SynchronizationContext.SetSynchronizationContext(_previousContext);
    }

    public sealed class StubInventoryStore : IFileInventoryStore
    {
        private readonly FileInventorySnapshot _snapshot;

        public StubInventoryStore(FileInventorySnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public List<string> LoadedRoots { get; } = [];

        /// <summary>The followReparsePoints argument of every Refresh call (called on a worker thread).</summary>
        public System.Collections.Concurrent.ConcurrentQueue<bool> RefreshFollowArguments { get; } = new();

        public FileInventorySnapshot? Load(string rootPath)
        {
            LoadedRoots.Add(rootPath);
            return string.Equals(rootPath, _snapshot.RootPath, StringComparison.OrdinalIgnoreCase) ? _snapshot : null;
        }

        public FileInventorySnapshot Refresh(string rootPath, string? excludedDirectoryPath, bool followReparsePoints, IProgress<InventoryRefreshProgress>? progress, CancellationToken cancellationToken)
        {
            RefreshFollowArguments.Enqueue(followReparsePoints);
            return string.Equals(rootPath, _snapshot.RootPath, StringComparison.OrdinalIgnoreCase)
                ? _snapshot
                : new FileInventorySnapshot { RootPath = rootPath };
        }
    }

    private sealed class DroppingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        public override SynchronizationContext CreateCopy()
        {
            return this;
        }
    }
}
