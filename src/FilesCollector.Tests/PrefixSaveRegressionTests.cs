using FilesCollector.App;
using FilesCollector.Core;
using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Prefixes;
using FilesCollector.Core.Reporting;
using FilesCollector.Core.Settings;
using FilesCollector.Extractors;
using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.FileSystem;
using FilesCollector.Infrastructure.Inventory;
using FilesCollector.Infrastructure.Prefixes;
using FilesCollector.Infrastructure.Presets;
using FilesCollector.Infrastructure.Reporting;
using FilesCollector.Infrastructure.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>
/// Regression tests for the prefix preset save crash: editing a prefix and pressing
/// Save used to throw NullReferenceException inside the save/activate chain.
/// The full MainWindowViewModel wiring is reproduced so the StateChanged handler
/// chain (UpdatePreview / UpdateDirtyState) runs exactly like in the real app.
/// </summary>
public sealed class PrefixSaveRegressionTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Editing_a_prefix_and_saving_does_not_fail()
    {
        var vm = CreateViewModel();
        try
        {
            var prefixes = vm.PrefixPresets;
            string? error = null;
            prefixes.ErrorOccurred += (_, message) => error = message;

            prefixes.NameRequested += (_, request) =>
            {
                request.Name = "Alpha";
                request.IsAccepted = true;
            };
            prefixes.NewCommand.Execute(null);

            prefixes.HasSelection.Should().BeTrue("a newly created prefix must become active");
            error.Should().BeNull("creating the prefix must succeed");

            prefixes.Content = "Edited prefix body";
            prefixes.SaveCommand.Execute(null);

            error.Should().BeNull($"saving the edited prefix must succeed, but got: {error}");
            prefixes.IsDirty.Should().BeFalse();

            var repository = CreatePrefixRepository();
            repository.GetAll().Should().Contain(preset => preset.Name == "Alpha" && preset.Content == "Edited prefix body");
        }
        finally
        {
            vm.Shutdown();
        }
    }

    private MainWindowViewModel CreateViewModel()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "files-collector"));

        var executablePath = Path.Combine(_tempRoot, "files-collector", "FilesCollector.exe");
        var appDataPath = Path.Combine(_tempRoot, "appdata");
        IAppPaths paths = new AppPaths(executablePath, appDataPath);
        IFileSystem fileSystem = new WindowsFileSystem();
        IPrefixPresetRepository prefixRepository = CreatePrefixRepository();

        return new MainWindowViewModel(
            paths,
            new ScanRootProvider(paths),
            fileSystem,
            new JsonFileInventoryStore(paths, fileSystem),
            new CollectionPlanner(fileSystem),
            new MarkdownReportWriter(paths, new SystemClock(), [new CSharpSignatureExtractor(), new JavaScriptTypeScriptSignatureExtractor(), new PythonSignatureExtractor(), new StructuredDataSignatureExtractor()]),
            new JsonPresetRepository(paths, NullLogger<JsonPresetRepository>.Instance),
            new JsonAppSessionStore(paths),
            new PrefixPresetsViewModel(prefixRepository),
            NullLogger<MainWindowViewModel>.Instance);
    }

    private JsonPrefixPresetRepository CreatePrefixRepository()
    {
        var executablePath = Path.Combine(_tempRoot, "files-collector", "FilesCollector.exe");
        var paths = new AppPaths(executablePath, Path.Combine(_tempRoot, "appdata"));
        return new JsonPrefixPresetRepository(paths, NullLogger<JsonPrefixPresetRepository>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }
}
